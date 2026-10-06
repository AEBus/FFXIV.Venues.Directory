using System;
using System.Collections.Generic;
using FFXIV.Venues.Directory.Features.Directory.Domain;

namespace FFXIV.Venues.Directory.Features.Events;

// Finds the FFXIV Venues listing a community event takes place at: the venue at the event's address, or else the one venue on the event's world whose name matches the host's. The Events tab and the notifications both go through it, so they agree on an event's venue.
internal sealed class ListedVenueFinder<TVenue>
    where TVenue : class
{
    // Names shorter than this are too likely to match by chance.
    private const int MinNameKeyLength = 4;

    private readonly Dictionary<string, TVenue> _byAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string NameKey, TVenue Venue)>> _byWorld = new(StringComparer.OrdinalIgnoreCase);

    // source: the API venue behind each entry.
    public ListedVenueFinder(IEnumerable<TVenue> venues, Func<TVenue, DirectoryVenue> source)
    {
        foreach (var venue in venues)
        {
            var listing = source(venue);
            if (listing.Location is not { } location)
            {
                continue;
            }

            if (EventVenueMatches.AddressKey(location) is { } key)
            {
                _byAddress.TryAdd(key, venue);
            }

            var nameKey = EventVenueMatches.NameKey(listing.Name);
            if (!string.IsNullOrWhiteSpace(location.World) && nameKey.Length >= MinNameKeyLength)
            {
                if (!_byWorld.TryGetValue(location.World, out var list))
                {
                    _byWorld[location.World] = list = [];
                }

                list.Add((nameKey, venue));
            }
        }
    }

    // Returns the listed venue of the event, or null when no listing fits.
    public TVenue? Find(CommunityEvent communityEvent)
    {
        if (communityEvent.Address != null && EventVenueMatches.AddressKey(communityEvent.Address) is { } key &&
            _byAddress.TryGetValue(key, out var atAddress))
        {
            return atAddress;
        }

        return FindByName(communityEvent);
    }

    private TVenue? FindByName(CommunityEvent communityEvent)
    {
        if (communityEvent.World == null || !_byWorld.TryGetValue(communityEvent.World, out var venues))
        {
            return null;
        }

        var team = EventVenueMatches.NameKey(communityEvent.TeamName);
        if (team.Length < MinNameKeyLength)
        {
            return null;
        }

        TVenue? found = null;
        foreach (var (name, venue) in venues)
        {
            if (!(name == team || name.StartsWith(team, StringComparison.Ordinal) || team.StartsWith(name, StringComparison.Ordinal)))
            {
                continue;
            }

            if (found != null)
            {
                // Two venues fit the name: better no link than a wrong one.
                return null;
            }

            found = venue;
        }

        return found;
    }
}
