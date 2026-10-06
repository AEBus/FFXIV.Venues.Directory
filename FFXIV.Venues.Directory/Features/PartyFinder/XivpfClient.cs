using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIV.Venues.Directory.Features.PartyFinder;

// A Party Finder listing in the category without a duty, which is where venues advertise.
internal sealed record PartyFinderAd(
    long Id,
    string Recruiter,
    string HomeWorld,
    string Text,
    DateTimeOffset PostedAt,
    DateTimeOffset ExpiresAt);

// The Party Finder listings xivpf.com collects from players' game clients, read from its public listings endpoint. All data centres come in one response, so the plugin asks for it rarely.
internal sealed class XivpfClient(HttpClient httpClient)
{
    public const string ListingsUrl = "https://xivpf.com/api/listings";

    private const string NoDutyCategory = "None";

    public async Task<IReadOnlyList<PartyFinderAd>> GetAdsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(ListingsUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var listings = await JsonSerializer.DeserializeAsync<List<ListingContainer>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return SelectAds(listings ?? [], DateTimeOffset.UtcNow);
    }

    // The ads still up at now from the category without a duty, once each: the same recruiter often posts the same text on several worlds.
    internal static IReadOnlyList<PartyFinderAd> SelectAds(IEnumerable<ListingContainer> listings, DateTimeOffset now)
    {
        var ads = new Dictionary<(string, string), PartyFinderAd>();
        foreach (var container in listings)
        {
            var listing = container.Listing;
            var text = listing?.Description?.En?.Trim();
            if (listing == null || listing.Category != NoDutyCategory || string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(listing.Recruiter))
            {
                continue;
            }

            var expiresAt = container.UpdatedAt + TimeSpan.FromSeconds(listing.SecondsRemaining);
            if (expiresAt <= now)
            {
                continue;
            }

            var ad = new PartyFinderAd(listing.Id, listing.Recruiter.Trim(), listing.HomeWorld?.Name ?? string.Empty, text, container.CreatedAt, expiresAt);
            var key = (ad.Recruiter, ad.Text);
            if (!ads.TryGetValue(key, out var known) || known.ExpiresAt < ad.ExpiresAt)
            {
                ads[key] = ad;
            }
        }

        return ads.Values.OrderBy(ad => ad.PostedAt).ToList();
    }

    internal sealed class ListingContainer
    {
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        [JsonPropertyName("listing")]
        public Listing? Listing { get; set; }
    }

    internal sealed class Listing
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("recruiter")]
        public string? Recruiter { get; set; }

        [JsonPropertyName("description")]
        public LocalizedText? Description { get; set; }

        [JsonPropertyName("home_world")]
        public NamedWorld? HomeWorld { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("seconds_remaining")]
        public int SecondsRemaining { get; set; }
    }

    internal sealed class LocalizedText
    {
        [JsonPropertyName("en")]
        public string? En { get; set; }
    }

    internal sealed class NamedWorld
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
