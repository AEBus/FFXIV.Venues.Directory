using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace FFXIV.Venues.Directory.Infrastructure.Media;

// Images by URL, loaded in the background when first asked for with Get and kept for the most recently used ones (Peek looks without loading); animated images (GIF, WebP, APNG) keep every frame and play. Textures are only released in Trim, which the window calls before drawing, so nothing it drew last frame is freed while the frame may still be rendering. A download that stalls gives up after LoadTimeout, so the image shows as failed instead of loading forever; a failed one is tried again when shown after RetryAfter. Only https addresses are fetched: any other shows as failed at once, and so does an image the host answered is not there (a client error other than a timeout or rate limit), without being tried again.
internal sealed partial class RemoteImageCache : IDisposable
{
    private const int MaxImages = 40;

    // Texture memory all kept images may take together, counted as 4 bytes per pixel of every frame.
    private const long MaxCachedBytes = 256L * 1024 * 1024;
    private const long MaxImageBytes = 12L * 1024 * 1024;
    private const int MaxParallelDownloads = 4;
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    // Sent to every other host: some refuse requests without a User-Agent, and this generic one says nothing about the plugin.
    private const string AnonymousUserAgent = "Mozilla/5.0";

    private readonly HttpClient _httpClient;
    private readonly string _userAgent;
    private readonly string[] _identifiedDomains;
    private readonly ITextureProvider _textureProvider;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recentlyUsed = new();
    private readonly List<IDalamudTextureWrap> _released = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _downloads = new(MaxParallelDownloads);
    private long _cachedBytes;
    private int _version;
    private bool _disposed;

    // userAgent: sent only to identifiedDomains and their subdomains; other hosts get AnonymousUserAgent, so they cannot tell who uses the plugin.
    public RemoteImageCache(HttpClient httpClient, ITextureProvider textureProvider, string userAgent, string[] identifiedDomains)
    {
        _httpClient = httpClient;
        _textureProvider = textureProvider;
        _userAgent = userAgent;
        _identifiedDomains = identifiedDomains;
    }

    // Goes up every time an image finishes loading (or fails), so a layout that sized a placeholder knows to redo it.
    public int Version => Volatile.Read(ref _version);

    // Returns the loaded texture (for an animated image, its frame for this moment), or null while it loads; failed tells whether it never will.
    public IDalamudTextureWrap? Get(string url, out bool failed)
    {
        failed = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (_entries.TryGetValue(url, out var entry))
            {
                _recentlyUsed.Remove(entry.Node);
                _recentlyUsed.AddFirst(entry.Node);
                if (entry.Failed && !entry.Final && DateTimeOffset.UtcNow - entry.FailedAt >= RetryAfter)
                {
                    entry.Failed = false;
                    entry.Loading = true;
                    _ = LoadAsync(url, entry, _disposeCts.Token);
                }

                failed = entry.Failed;
                return entry.CurrentFrame();
            }

            entry = new Entry(_recentlyUsed.AddFirst(url));
            _entries[url] = entry;
            if (!ImageAddress.CanFetch(url))
            {
                entry.Loading = false;
                entry.Failed = entry.Final = true;
                failed = true;
                return null;
            }

            _ = LoadAsync(url, entry, _disposeCts.Token);
            return null;
        }
    }

    // Returns what is known of an image without loading it or counting it as used (the texture once loaded, and whether it failed), so a layout can size the images it does not draw yet without bringing evicted ones back.
    public IDalamudTextureWrap? Peek(string url, out bool failed)
    {
        lock (_gate)
        {
            if (!_disposed && _entries.TryGetValue(url, out var entry))
            {
                failed = entry.Failed;
                return entry.CurrentFrame();
            }
        }

        failed = !ImageAddress.CanFetch(url);
        return null;
    }

    // Frees the textures beyond the limits; call before drawing any image in the frame.
    public void Trim()
    {
        lock (_gate)
        {
            foreach (var texture in _released)
            {
                texture.Dispose();
            }

            _released.Clear();
            while ((_entries.Count > MaxImages || _cachedBytes > MaxCachedBytes) && _recentlyUsed.Last is { } oldest)
            {
                var entry = _entries[oldest.Value];
                if (entry.Loading)
                {
                    break;
                }

                _recentlyUsed.RemoveLast();
                _entries.Remove(oldest.Value);
                _cachedBytes -= entry.Bytes;
                entry.DisposeFrames();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disposeCts.Cancel();
            foreach (var entry in _entries.Values)
            {
                entry.DisposeFrames();
            }

            foreach (var texture in _released)
            {
                texture.Dispose();
            }

            _entries.Clear();
            _recentlyUsed.Clear();
            _released.Clear();
            _cachedBytes = 0;
        }

        _disposeCts.Dispose();
    }

    private async Task LoadAsync(string url, Entry entry, CancellationToken cancellationToken)
    {
        var downloading = false;
        try
        {
            // A few at a time, so the images at the top of a long description arrive first.
            await _downloads.WaitAsync(cancellationToken).ConfigureAwait(false);
            downloading = true;

            // HttpClient's own timeout ends when the headers arrive; this one also covers reading the body.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(LoadTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(ImageAddress.IsOnDomain(request.RequestUri!, _identifiedDomains) ? _userAgent : AnonymousUserAgent);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                throw new InvalidOperationException($"image larger than {MaxImageBytes} bytes");
            }

            // The size limit also holds when the server does not say the length up front.
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes)
                {
                    throw new InvalidOperationException($"image larger than {MaxImageBytes} bytes");
                }

                buffer.Write(chunk, 0, read);
            }

            var bytes = buffer.ToArray();
            _downloads.Release();
            downloading = false;
            var (frames, frameEnds, frameBytes) = await CreateFramesAsync(url, bytes, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_entries.GetValueOrDefault(url), entry))
                {
                    _released.AddRange(frames);
                    return;
                }

                entry.SetFrames(frames, frameEnds, frameBytes);
                _cachedBytes += frameBytes;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Debug("Could not load image {Url}: {Reason}", url, ex is OperationCanceledException ? "timed out" : ex.Message);
            lock (_gate)
            {
                entry.Failed = true;
                entry.FailedAt = DateTimeOffset.UtcNow;
                entry.Final = ex is HttpRequestException { StatusCode: { } status } &&
                              (int)status is >= 400 and < 500 and not 408 and not 429;
            }
        }
        finally
        {
            if (downloading)
            {
                _downloads.Release();
            }

            lock (_gate)
            {
                entry.Loading = false;
            }

            Interlocked.Increment(ref _version);
        }
    }

    // An animated image becomes one texture per frame; anything else, one texture through Dalamud's own decoder.
    private async Task<(IDalamudTextureWrap[] Frames, long[]? FrameEnds, long Bytes)> CreateFramesAsync(string url, byte[] bytes, CancellationToken cancellationToken)
    {
        var debugName = $"FFXIV.Venues.Directory.Image.{url}";
        var animation = await Task.Run(() => AnimatedImageDecoder.TryDecode(bytes), cancellationToken).ConfigureAwait(false);
        if (animation == null)
        {
            var texture = await _textureProvider.CreateFromImageAsync(bytes, debugName, cancellationToken).ConfigureAwait(false);
            return ([texture], null, (long)texture.Width * texture.Height * 4);
        }

        var frames = new List<IDalamudTextureWrap>(animation.Frames.Count);
        try
        {
            var frameEnds = new long[animation.Frames.Count];
            var elapsed = 0L;
            var specification = RawImageSpecification.Rgba32(animation.Width, animation.Height);
            for (var i = 0; i < animation.Frames.Count; i++)
            {
                frames.Add(await _textureProvider.CreateFromRawAsync(specification, animation.Frames[i].Rgba, debugName, cancellationToken).ConfigureAwait(false));
                elapsed += animation.Frames[i].DurationMs;
                frameEnds[i] = elapsed;
            }

            return (frames.ToArray(), frameEnds, (long)animation.Width * animation.Height * 4 * frames.Count);
        }
        catch
        {
            lock (_gate)
            {
                _released.AddRange(frames);
            }

            throw;
        }
    }

    private sealed class Entry(LinkedListNode<string> node)
    {
        private IDalamudTextureWrap[]? _frames;

        // When each frame of an animated image ends, in milliseconds from the start of the loop; null for a still image.
        private long[]? _frameEnds;
        private long _startedAt;

        public LinkedListNode<string> Node { get; } = node;
        public long Bytes { get; private set; }
        public bool Failed { get; set; }
        public DateTimeOffset FailedAt { get; set; }

        // Never tried again: not an https address, or the host answered that the image is not there.
        public bool Final { get; set; }
        public bool Loading { get; set; } = true;
        public int FrameCount => _frames?.Length ?? 0;

        public void SetFrames(IDalamudTextureWrap[] frames, long[]? frameEnds, long bytes)
        {
            _frames = frames;
            _frameEnds = frameEnds;
            _startedAt = Environment.TickCount64;
            Bytes = bytes;
        }

        // An animation starts from its first frame when it arrives and loops from there.
        public IDalamudTextureWrap? CurrentFrame()
        {
            if (_frames == null || _frames.Length == 0)
            {
                return null;
            }

            if (_frameEnds == null || _frames.Length == 1 || _frameEnds[^1] <= 0)
            {
                return _frames[0];
            }

            var position = (Environment.TickCount64 - _startedAt) % _frameEnds[^1];
            var index = Array.BinarySearch(_frameEnds, position + 1);
            return _frames[Math.Min(index < 0 ? ~index : index, _frames.Length - 1)];
        }

        public void DisposeFrames()
        {
            if (_frames == null)
            {
                return;
            }

            foreach (var frame in _frames)
            {
                frame.Dispose();
            }

            _frames = null;
            _frameEnds = null;
        }
    }
}
