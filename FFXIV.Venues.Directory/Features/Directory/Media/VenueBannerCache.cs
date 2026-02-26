using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FFXIV.Venues.Directory.Features.Directory.Media;

public sealed class VenueBannerCache : IVenueBannerCache, IDisposable
{
    private static readonly byte[] TransparentFallbackPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/w8AAgMBAp+N7WAAAAAASUVORK5CYII=");

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly HttpClient _httpClient;
    private readonly ITextureProvider _textureProvider;
    private readonly Dictionary<string, IDalamudTextureWrap?> _bannerByUri = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private IDalamudTextureWrap _placeholderTexture;
    private bool _disposed;

    public VenueBannerCache(IDalamudPluginInterface pluginInterface, HttpClient httpClient, ITextureProvider textureProvider)
    {
        _pluginInterface = pluginInterface;
        _httpClient = httpClient;
        _textureProvider = textureProvider;
        _placeholderTexture = LoadPlaceholderTexture();
    }

    public IDalamudTextureWrap? GetVenueBanner(string venueId, Uri? bannerUri)
    {
        if (_disposed)
        {
            return null;
        }

        var requestUri = BuildRequestUri(venueId, bannerUri);
        lock (_gate)
        {
            if (_bannerByUri.TryGetValue(requestUri, out var texture))
            {
                return texture;
            }

            if (!_pendingRequests.ContainsKey(requestUri))
            {
                _pendingRequests[requestUri] = FetchBannerAsync(requestUri);
            }
        }

        return _placeholderTexture;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            foreach (var texture in _bannerByUri.Values)
            {
                texture?.Dispose();
            }

            _bannerByUri.Clear();
            _pendingRequests.Clear();
        }

        _placeholderTexture.Dispose();
    }

    private async Task FetchBannerAsync(string requestUri)
    {
        try
        {
            using var response = await _httpClient.GetAsync(requestUri).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                lock (_gate)
                {
                    _bannerByUri[requestUri] = null;
                }

                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var texture = await _textureProvider.CreateFromImageAsync(bytes, $"FFXIVVenues.Banner.{requestUri}").ConfigureAwait(false);

            lock (_gate)
            {
                if (_bannerByUri.TryGetValue(requestUri, out var existing))
                {
                    existing?.Dispose();
                }

                _bannerByUri[requestUri] = texture;
            }
        }
        catch
        {
            lock (_gate)
            {
                _bannerByUri[requestUri] = null;
            }
        }
        finally
        {
            lock (_gate)
            {
                _pendingRequests.Remove(requestUri);
            }
        }
    }

    private IDalamudTextureWrap LoadPlaceholderTexture()
    {
        foreach (var path in EnumeratePlaceholderCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            return LoadTexture(File.ReadAllBytes(path), $"FFXIVVenues.Loading.{Path.GetFileName(path)}");
        }

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith("loading.png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return LoadTexture(buffer.ToArray(), "FFXIVVenues.Loading.Embedded");
        }

        return LoadTexture(TransparentFallbackPng, "FFXIVVenues.Loading.Fallback");
    }

    private IEnumerable<string> EnumeratePlaceholderCandidates()
    {
        var assemblyDir = _pluginInterface.AssemblyLocation.Directory?.FullName;
        if (string.IsNullOrWhiteSpace(assemblyDir))
        {
            yield break;
        }

        yield return Path.Combine(assemblyDir, "Assets", "loading.png");
        yield return Path.Combine(assemblyDir, "loading.png");
    }

    private IDalamudTextureWrap LoadTexture(byte[] imageBytes, string debugName) =>
        _textureProvider.CreateFromImageAsync(imageBytes, debugName).GetAwaiter().GetResult();

    private static string BuildRequestUri(string venueId, Uri? bannerUri)
    {
        if (bannerUri != null)
        {
            return bannerUri.ToString();
        }

        return $"venue/{venueId}/media";
    }
}

public interface IVenueBannerCache
{
    IDalamudTextureWrap? GetVenueBanner(string venueId, Uri? bannerUri);
}
