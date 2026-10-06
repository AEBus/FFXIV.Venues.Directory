using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using static FFXIV.Venues.Directory.Features.Directory.Text.VenueText;

namespace FFXIV.Venues.Directory.Features.Events;

// Which venue each community event (a Partake event or a Party Finder ad) takes place at: the FFXIV Venues listing at the same world, district, ward and plot (or apartment), or else a host whose name matches a venue on the same world. Events at an address no listing has make up an unlisted venue, one per address. Venues show their upcoming events; events link to their venue.
internal sealed class EventVenueMatches
{
    public const string UnlistedVenueIdPrefix = "partake:";

    public const string UnnamedVenueName = "Unnamed venue";

    private readonly Dictionary<int, PreparedVenue> _listedVenues = [];
    private readonly Dictionary<int, PreparedVenue> _unlistedVenues = [];
    private readonly HashSet<int> _adsRepeatingPartakeEvents = [];
    private readonly Dictionary<string, List<CommunityEvent>> _venueEvents = new(StringComparer.Ordinal);

    private EventVenueMatches()
    {
    }

    public static EventVenueMatches Empty { get; } = new();

    public PreparedVenue[] UnlistedVenues { get; private set; } = [];

    public int MatchedEventCount => _listedVenues.Count;

    public int VenuesWithEvents => _venueEvents.Count;

    public static bool IsUnlistedVenue(PreparedVenue venue) =>
        venue.Id.StartsWith(UnlistedVenueIdPrefix, StringComparison.Ordinal);

    public PreparedVenue? ListedVenueOf(int eventId) => _listedVenues.GetValueOrDefault(eventId);

    // The venue an event takes place at, listed on FFXIV Venues or not; null when it is not known.
    public PreparedVenue? VenueOf(int eventId) => _listedVenues.GetValueOrDefault(eventId) ?? _unlistedVenues.GetValueOrDefault(eventId);

    // Whether a Party Finder ad is up while its venue has an event on Partake.
    public bool RepeatsPartakeEvent(int eventId) => _adsRepeatingPartakeEvents.Contains(eventId);

    public int AdsRepeatingPartakeEvents => _adsRepeatingPartakeEvents.Count;

    // Returns the id of the venue an event takes place at, listed on FFXIV Venues or not; null when it is not known.
    public string? VenueIdOf(int eventId) => VenueOf(eventId)?.Id;

    // Gets a venue's events, soonest first.
    public bool TryGetEvents(string venueId, out List<CommunityEvent> events) =>
        _venueEvents.TryGetValue(venueId, out events!);

    // prepare: turns a venue made up from events into what the list shows (VenuePreparer.CreatePreparedVenue).
    public static EventVenueMatches Build(
        IReadOnlyList<CommunityEvent> events,
        IReadOnlyList<PreparedVenue> venues,
        Func<DirectoryVenue, PreparedVenue> prepare,
        DateTimeOffset now)
    {
        var matches = new EventVenueMatches();
        if (events.Count == 0)
        {
            return matches;
        }

        var finder = new ListedVenueFinder<PreparedVenue>(venues, venue => venue.Source);
        foreach (var communityEvent in events)
        {
            if (finder.Find(communityEvent) is not { } venue)
            {
                continue;
            }

            matches._listedVenues[communityEvent.Id] = venue;
            if (!matches._venueEvents.TryGetValue(venue.Id, out var venueEvents))
            {
                matches._venueEvents[venue.Id] = venueEvents = [];
            }

            venueEvents.Add(communityEvent);
        }

        foreach (var venueEvents in matches._venueEvents.Values)
        {
            venueEvents.Sort((left, right) => left.StartsAt.CompareTo(right.StartsAt));
        }

        matches.UnlistedVenues = matches.BuildUnlistedVenues(events, prepare, now);
        foreach (var venueEvents in matches._venueEvents.Values)
        {
            foreach (var ad in venueEvents.Where(e => e.Source == EventSource.PartyFinder))
            {
                if (venueEvents.Any(e => e.Source == EventSource.Partake && e.StartsAt < ad.EndsAt && ad.StartsAt < e.EndsAt))
                {
                    matches._adsRepeatingPartakeEvents.Add(ad.Id);
                }
            }
        }

        return matches;
    }

    // Venues that post events on Partake or advertise in the Party Finder but are not listed on FFXIV Venues, one per address, described by what their events say: the host's name (Partake's first), the address, the events' tags and banner, and the events as openings.
    private PreparedVenue[] BuildUnlistedVenues(IReadOnlyList<CommunityEvent> events, Func<DirectoryVenue, PreparedVenue> prepare, DateTimeOffset now)
    {
        var venues = new List<PreparedVenue>();
        var groups = events
            .Where(e => !_listedVenues.ContainsKey(e.Id) && e.Address != null && AddressKey(e.Address) != null)
            .GroupBy(e => AddressKey(e.Address!)!, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var venueEvents = group.OrderBy(e => e.StartsAt).ToList();
            var next = venueEvents.FirstOrDefault(e => e.EndsAt > now) ?? venueEvents[0];
            // Only Partake events give opening hours; an ad only says the venue advertises while it is up.
            var partakeEvents = venueEvents.Where(e => e.Source == EventSource.Partake).ToList();
            var nextPartake = partakeEvents.FirstOrDefault(e => e.EndsAt > now) ?? partakeEvents.FirstOrDefault();
            var ad = venueEvents.LastOrDefault(e => e.Source == EventSource.PartyFinder);
            // A Partake event without a host team names the venue by its own title; ads without a name leave it unnamed.
            var name = venueEvents.OrderBy(e => e.Source).Select(e => e.TeamName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? (next.Source == EventSource.Partake ? next : venueEvents.FirstOrDefault(e => e.Source == EventSource.Partake))?.Title;
            var onPartake = venueEvents.Any(e => e.Source == EventSource.Partake);
            var inPartyFinder = venueEvents.Any(e => e.Source == EventSource.PartyFinder);
            var known = onPartake && inPartyFinder ? "posts its events on Partake and advertises in the Party Finder"
                : onPartake ? "posts its events on Partake"
                : "advertises in the Party Finder";
            var from = onPartake && inPartyFinder ? "those events and ads" : onPartake ? "those events" : "those ads";
            var source = new DirectoryVenue
            {
                Id = UnlistedVenueIdPrefix + group.Key,
                Name = name ?? UnnamedVenueName,
                Description = [$"{name ?? "This venue"} {known} and is not listed on FFXIV Venues. What is shown here comes from {from}."],
                Tags = venueEvents.SelectMany(e => e.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Location = next.Address,
                Resolution = nextPartake != null ? new DirectoryResolution { IsNow = nextPartake.IsLive(now), Start = nextPartake.StartsAt, End = nextPartake.EndsAt } : null,
                KnownOpenings = partakeEvents.Select(e => new VenueOpening(e.StartsAt, e.EndsAt)).ToList(),
                Advertisement = ad != null ? new VenueOpening(ad.StartsAt, ad.EndsAt) : null,
                Sfw = venueEvents.All(e => e.AgeRating != "ADULT"),
                BannerUri = next.BannerUrl != null ? new Uri(next.BannerUrl) : null,
            };
            var prepared = prepare(source);
            venues.Add(prepared);
            _venueEvents[prepared.Id] = venueEvents;
            foreach (var communityEvent in venueEvents)
            {
                _unlistedVenues[communityEvent.Id] = prepared;
            }
        }

        return venues.ToArray();
    }

    // A comparable key for an address: world, district without a leading "The" or "(Subdivision)", ward, and plot or apartment. Apartments are numbered per building, so an apartment's key also says whether it is in the subdivision.
    public static string? AddressKey(DirectoryLocation location)
    {
        if (string.IsNullOrWhiteSpace(location.World) || string.IsNullOrWhiteSpace(location.District) || location.Ward <= 0)
        {
            return null;
        }

        var district = location.District.Replace("(Subdivision)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (district.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            district = district[4..];
        }

        var unit = location.Apartment > 0 ? $"a{location.Apartment}{(location.Subdivision ? "s" : string.Empty)}" : $"p{location.Plot}";
        return $"{location.World.Trim()}|{district}|{location.Ward}|{unit}";
    }

    // A comparable key for a name: letters and digits only, with stylized letters folded to plain ones.
    public static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var c in NormalizeForSearch(name))
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
