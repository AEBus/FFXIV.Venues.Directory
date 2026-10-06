using System;
using System.Collections.Generic;
using System.Linq;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Infrastructure;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

// The venues the user starred, visited or hid, kept in the configuration and mirrored in lookups for the filters; hiding a venue can be undone for a moment from the top bar.
internal sealed partial class DirectoryBrowserWindow
{
    private void EnsurePreferenceCollectionsInitialized()
    {
        _configuration.FavoriteVenueIds ??= [];
        _configuration.VisitedVenueIds ??= [];
        _configuration.HiddenVenueIds ??= [];
        _configuration.CharacterLocations ??= [];
        _filters.IncludedTags ??= [];
        _filters.ExcludedTags ??= [];
    }

    private void SyncPreferredVenueLookups()
    {
        _favoriteVenueIds.Clear();
        _visitedVenueIds.Clear();
        _hiddenVenueIds.Clear();
        _hiddenVenueIds.UnionWith(_configuration.HiddenVenueIds.Where(id => !string.IsNullOrWhiteSpace(id)));

        foreach (var id in _configuration.FavoriteVenueIds ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                _favoriteVenueIds.Add(id);
            }
        }

        foreach (var id in _configuration.VisitedVenueIds ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                _visitedVenueIds.Add(id);
            }
        }
    }

    private static bool IsPreferredVenue(HashSet<string> venueIds, string? venueId) =>
        !string.IsNullOrWhiteSpace(venueId) && venueIds.Contains(venueId);

    private void SetPreferredVenue(List<string>? venueIds, HashSet<string> lookup, string? venueId, bool enabled)
    {
        if (venueIds == null || string.IsNullOrWhiteSpace(venueId))
        {
            return;
        }

        var changed = false;
        if (enabled)
        {
            if (lookup.Add(venueId))
            {
                venueIds.Add(venueId);
                changed = true;
            }
        }
        else if (lookup.Remove(venueId))
        {
            venueIds.RemoveAll(id => string.Equals(id, venueId, StringComparison.Ordinal));
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        _configuration.Save(DalamudServices.PluginInterface);
        _filteredEventsDirty = true;
        if (_filters.FavoritesOnly || _filters.Visited != VenueVisitedFilter.Any || ReferenceEquals(lookup, _hiddenVenueIds))
        {
            InvalidateFilteredVenues();
        }
    }

    private void SetVenueHidden(PreparedVenue venue, bool hidden)
    {
        if (hidden && string.Equals(_selectedVenueId, venue.Id, StringComparison.Ordinal))
        {
            // Moves the selection to the next venue in the list instead of losing it.
            var index = _sortedVenues.IndexOf(venue);
            var next = index >= 0 && index + 1 < _sortedVenues.Count ? _sortedVenues[index + 1]
                : index > 0 ? _sortedVenues[index - 1]
                : null;
            _selectedVenueId = next?.Id;
        }

        SetPreferredVenue(_configuration.HiddenVenueIds, _hiddenVenueIds, venue.Id, hidden);
        _lastHiddenVenueId = hidden ? venue.Id : null;
        _lastHiddenVenueName = venue.DisplayName;
        _lastHiddenAt = DateTimeOffset.UtcNow;
    }

    private void UndoLastHide()
    {
        if (_lastHiddenVenueId == null)
        {
            return;
        }

        var id = _lastHiddenVenueId;
        _lastHiddenVenueId = null;
        SetPreferredVenue(_configuration.HiddenVenueIds, _hiddenVenueIds, id, false);
        _selectedVenueId = id;
    }
}
