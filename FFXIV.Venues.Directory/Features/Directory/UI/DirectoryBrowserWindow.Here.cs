using System;
using System.Collections.Generic;
using Dalamud.Interface;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Places;
using FFXIV.Venues.Directory.Features.Events;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

// "You're here": when the character stands on a listed venue's plot or is inside it, the top bar names the venue, opens it on a click and offers to mark it visited.
internal sealed partial class DirectoryBrowserWindow
{
    private static readonly TimeSpan HereCheckInterval = TimeSpan.FromSeconds(1);

    private DateTimeOffset _hereCheckedAt;
    private PreparedVenue? _venueHere;
    private readonly Dictionary<string, PreparedVenue> _venuesByAddress = new(StringComparer.OrdinalIgnoreCase);
    private PreparedVenue[]? _addressIndexVenues;
    private PreparedVenue[]? _addressIndexPartakeVenues;

    // Once a second: where the character is, and which venue (if any) is there.
    private void UpdateVenueHere()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _hereCheckedAt < HereCheckInterval)
        {
            return;
        }

        _hereCheckedAt = now;
        EnsureAddressIndex();
        _venueHere = CurrentAddress.Read() is { } location &&
                     EventVenueMatches.AddressKey(location) is { } key &&
                     _venuesByAddress.TryGetValue(key, out var venue)
            ? venue
            : null;
        OverrideVenueHere(ref _venueHere);
    }

    // Dev build: lets a check pretend the character is at a venue.
    partial void OverrideVenueHere(ref PreparedVenue? venue);

    private void EnsureAddressIndex()
    {
        var partakeVenues = _eventVenueMatches.UnlistedVenues;
        if (ReferenceEquals(_addressIndexVenues, _preparedVenues) && ReferenceEquals(_addressIndexPartakeVenues, partakeVenues))
        {
            return;
        }

        _addressIndexVenues = _preparedVenues;
        _addressIndexPartakeVenues = partakeVenues;
        _venuesByAddress.Clear();
        foreach (var venue in (IEnumerable<PreparedVenue>?)_preparedVenues ?? [])
        {
            AddToAddressIndex(venue);
        }

        foreach (var venue in partakeVenues)
        {
            AddToAddressIndex(venue);
        }
    }

    // A listed venue wins over a Partake-only one at the same address (it is indexed first).
    private void AddToAddressIndex(PreparedVenue venue)
    {
        if (venue.Source.Location is { } location && EventVenueMatches.AddressKey(location) is { } key)
        {
            _venuesByAddress.TryAdd(key, venue);
        }
    }

    private bool IsVenueHere(PreparedVenue venue) =>
        _venueHere != null && string.Equals(_venueHere.Id, venue.Id, StringComparison.Ordinal);

    private void DrawHereChips(float rightEdge, ref bool first)
    {
        if (_venueHere is not { } venue)
        {
            return;
        }

        var label = $"You're at {venue.DisplayName}";
        var size = MeasureChipButton(label, FontAwesomeIcon.MapMarkerAlt);
        PlaceWrapped(size.X, rightEdge, ref first);
        if (DrawChipButton(-3, label, FontAwesomeIcon.MapMarkerAlt, size, UiChipTone.Positive, "Show this venue"))
        {
            OpenVenue(venue.Id);
        }

        if (IsPreferredVenue(_visitedVenueIds, venue.Id))
        {
            return;
        }

        const string markLabel = "Mark visited";
        var markSize = MeasureChipButton(markLabel, FontAwesomeIcon.Check);
        PlaceWrapped(markSize.X, rightEdge, ref first);
        if (DrawChipButton(-4, markLabel, FontAwesomeIcon.Check, markSize, UiChipTone.Neutral, $"Mark {venue.DisplayName} as visited"))
        {
            SetPreferredVenue(_configuration.VisitedVenueIds, _visitedVenueIds, venue.Id, true);
        }
    }
}
