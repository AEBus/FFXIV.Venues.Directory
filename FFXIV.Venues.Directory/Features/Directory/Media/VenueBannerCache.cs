using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace FFXIV.Venues.Directory.Features.Directory.Media;

public sealed class VenueBannerCache : IVenueBannerCache, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ITextureProvider _textureProvider;
    private readonly Dictionary<string, IDalamudTextureWrap?> _bannerByUri = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private bool _disposed;

    public VenueBannerCache(HttpClient httpClient, ITextureProvider textureProvider)
    {
        _httpClient = httpClient;
        _textureProvider = textureProvider;
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
                _pendingRequests[requestUri] = FetchBannerAsync(requestUri, _disposeCts.Token);
            }

            return null;
        }
    }

    public bool IsVenueBannerLoading(string venueId, Uri? bannerUri)
    {
        if (_disposed)
        {
            return false;
        }

        lock (_gate)
        {
            return _pendingRequests.ContainsKey(BuildRequestUri(venueId, bannerUri));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();

        lock (_gate)
        {
            foreach (var texture in _bannerByUri.Values)
            {
                texture?.Dispose();
            }

            _bannerByUri.Clear();
            _pendingRequests.Clear();
        }

        _disposeCts.Dispose();
    }

    private async Task FetchBannerAsync(string requestUri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                lock (_gate)
                {
                    _bannerByUri[requestUri] = null;
                }

                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var texture = await _textureProvider.CreateFromImageAsync(bytes, $"FFXIVVenues.Banner.{requestUri}").ConfigureAwait(false);

            lock (_gate)
            {
                if (_disposed || cancellationToken.IsCancellationRequested)
                {
                    texture.Dispose();
                    return;
                }

                if (_bannerByUri.TryGetValue(requestUri, out var existing))
                {
                    existing?.Dispose();
                }

                _bannerByUri[requestUri] = texture;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            lock (_gate)
            {
                if (_disposed || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

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

    bool IsVenueBannerLoading(string venueId, Uri? bannerUri);
}
