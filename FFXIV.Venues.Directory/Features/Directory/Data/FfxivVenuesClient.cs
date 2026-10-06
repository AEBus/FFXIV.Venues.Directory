using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Infrastructure.Net;

namespace FFXIV.Venues.Directory.Features.Directory.Data;

// Client for the approved venue list of the FFXIV Venues API.
internal sealed class FfxivVenuesClient(HttpClient httpClient)
{
    public const string ApiBaseAddress = "https://api.ffxivvenues.com/v1/";

    public async Task<DirectoryVenue[]> GetVenuesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("venue?approved=true", cancellationToken).ConfigureAwait(false);

        // Cloudflare answers blocked regions and networks with a challenge page instead of JSON; this reports it as a readable error rather than a bare HTTP 403.
        if (response.Headers.TryGetValues("cf-mitigated", out var mitigations) &&
            mitigations.Contains("challenge", StringComparer.OrdinalIgnoreCase))
        {
            throw new ServiceRefusedException(
                "FFXIV Venues is refusing requests from your network or region. Try again later or from a different network.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DirectoryVenue[]>(cancellationToken).ConfigureAwait(false)
               ?? [];
    }
}
