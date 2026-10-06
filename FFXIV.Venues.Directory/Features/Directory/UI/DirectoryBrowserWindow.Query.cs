using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Bindings.ImGui;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using static FFXIV.Venues.Directory.Features.Directory.Text.VenueText;

namespace FFXIV.Venues.Directory.Features.Directory.Ui;

// Which venues the list shows and in what order: the search and the filter cards applied to the prepared venues (and the Partake-only ones), then the table's sort.
internal sealed partial class DirectoryBrowserWindow
{
    private void RefreshFilteredVenuesIfNeeded()
    {
        if (!_filteredVenuesDirty)
        {
            return;
        }

        _filteredVenues.Clear();
        if (_preparedVenues == null)
        {
            _filteredVenuesDirty = false;
            _sortedVenuesDirty = true;
            return;
        }

        var normalizedSearch = NormalizeForSearch(_searchText.Trim());
        var now = DateTimeOffset.Now;
        foreach (var venue in _preparedVenues)
        {
            if (MatchesCurrentFilters(venue, normalizedSearch, now))
            {
                _filteredVenues.Add(venue);
            }
        }

        if (UnlistedVenuesShown)
        {
            foreach (var venue in _eventVenueMatches.UnlistedVenues)
            {
                if (MatchesCurrentFilters(venue, normalizedSearch, now))
                {
                    _filteredVenues.Add(venue);
                }
            }
        }

        _filteredVenuesDirty = false;
        _sortedVenuesDirty = true;
        _sortedVenueRowMetricsDirty = true;
    }

    private bool MatchesCurrentFilters(PreparedVenue venue, string normalizedSearch, DateTimeOffset now)
    {
        var filters = _filters;

        // Hidden venues only show up in the Hidden view, and that view shows all of them (search still applies), whatever the other filters say: it is where a venue is brought back from.
        if (filters.HiddenOnly != _hiddenVenueIds.Contains(venue.Id))
        {
            return false;
        }

        if (normalizedSearch.Length > 0 &&
            CultureInfo.CurrentCulture.CompareInfo.IndexOf(venue.SearchText, normalizedSearch, CompareOptions.IgnoreCase) < 0)
        {
            return false;
        }

        if (filters.HiddenOnly)
        {
            return true;
        }

        foreach (var tag in filters.IncludedTags)
        {
            if (!venue.Tags.Contains(tag))
            {
                return false;
            }
        }

        foreach (var tag in filters.ExcludedTags)
        {
            if (venue.Tags.Contains(tag))
            {
                return false;
            }
        }

        if (!string.IsNullOrEmpty(filters.Region) &&
            !string.Equals(venue.Region, filters.Region, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filters.DataCenter) &&
            !string.Equals(venue.DataCenter, filters.DataCenter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filters.World) &&
            !string.Equals(venue.World, filters.World, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var matchesTime = filters.Time switch
        {
            VenueTimeFilter.OpenNow => venue.IsOpen,
            VenueTimeFilter.Soon => venue.IsOpen || IsOpeningSoon(venue, now),
            _ => true,
        };
        if (!matchesTime)
        {
            return false;
        }

        if (filters.FavoritesOnly && !IsPreferredVenue(_favoriteVenueIds, venue.Id))
        {
            return false;
        }

        var visited = IsPreferredVenue(_visitedVenueIds, venue.Id);
        if ((filters.Visited == VenueVisitedFilter.Visited && !visited) ||
            (filters.Visited == VenueVisitedFilter.NotVisited && visited))
        {
            return false;
        }

        if ((filters.Content == VenueContentFilter.SfwOnly && venue.IsNsfw) ||
            (filters.Content == VenueContentFilter.NsfwOnly && !venue.IsNsfw))
        {
            return false;
        }

        return filters.ShowsEveryPlace() || filters.ShowsPlace(venue.IsApartment, venue.PlotSize);
    }

    // Whether the venue opens within SoonWindow, by its last computed status.
    private static bool IsOpeningSoon(PreparedVenue venue, DateTimeOffset now) =>
        !venue.IsOpen &&
        venue.Status.Opening is { } opening &&
        opening.Start > now &&
        opening.Start - now <= SoonWindow;

    private void EnsureSortedVenues(ImGuiTableSortSpecsPtr sortSpecs)
    {
        var sortSpecsChanged = HaveSortSpecsChanged(sortSpecs);
        if (!_sortedVenuesDirty && !sortSpecsChanged)
        {
            return;
        }

        _sortedVenues.Clear();
        _sortedVenues.AddRange(_filteredVenues);
        _sortedVenues.Sort((left, right) => ComparePreparedVenues(left, right, _sortSpecSnapshots));
        _sortedVenuesDirty = false;
        _sortedVenueRowMetricsDirty = true;

        if (sortSpecs.SpecsCount > 0)
        {
            sortSpecs.SpecsDirty = false;
        }

    }

    private bool HaveSortSpecsChanged(ImGuiTableSortSpecsPtr sortSpecs)
    {
        var nextSortSpecs = new List<SortSpecSnapshot>(sortSpecs.SpecsCount);
        for (var i = 0; i < sortSpecs.SpecsCount; i++)
        {
            var spec = sortSpecs.Specs[i];
            if (spec.SortDirection == ImGuiSortDirection.None)
            {
                continue;
            }

            nextSortSpecs.Add(new SortSpecSnapshot(spec.ColumnIndex, spec.SortDirection));
        }

        var changed = nextSortSpecs.Count != _sortSpecSnapshots.Count;
        if (!changed)
        {
            for (var i = 0; i < nextSortSpecs.Count; i++)
            {
                if (nextSortSpecs[i] != _sortSpecSnapshots[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            _sortSpecSnapshots.Clear();
            _sortSpecSnapshots.AddRange(nextSortSpecs);
        }

        return _sortedVenuesDirty || sortSpecs.SpecsDirty || changed;
    }

    private static int ComparePreparedVenues(
        PreparedVenue left,
        PreparedVenue right,
        IReadOnlyList<SortSpecSnapshot> sortSpecs)
    {
        if (sortSpecs.Count == 0)
        {
            return ComparePreparedVenueColumn(left, right, 0);
        }

        foreach (var sortSpec in sortSpecs)
        {
            var comparison = ComparePreparedVenueColumn(left, right, sortSpec.ColumnIndex);
            if (comparison == 0)
            {
                continue;
            }

            return sortSpec.Direction == ImGuiSortDirection.Descending
                ? -comparison
                : comparison;
        }

        return ComparePreparedVenueColumn(left, right, 0);
    }

    private static int ComparePreparedVenueColumn(PreparedVenue left, PreparedVenue right, int columnIndex) =>
        columnIndex switch
        {
            0 => CompareText(left.DisplayName, right.DisplayName),
            1 => CompareText(left.LocationSortKey, right.LocationSortKey),
            2 => left.VenueTypeSortKey.CompareTo(right.VenueTypeSortKey),
            3 => CompareStatus(left, right),
            _ => CompareText(left.DisplayName, right.DisplayName)
        };

    // Open venues first (closing soonest first), then by the next opening; venues without a schedule last.
    private static int CompareStatus(PreparedVenue left, PreparedVenue right) =>
        left.IsOpen != right.IsOpen
            ? (left.IsOpen ? -1 : 1)
            : left.StatusSortKey.CompareTo(right.StatusSortKey);

    private static int CompareText(string? left, string? right) =>
        StringComparer.OrdinalIgnoreCase.Compare(left ?? string.Empty, right ?? string.Empty);

    private void InvalidateFilteredVenues()
    {
        _filteredVenuesDirty = true;
        _filteredEventsDirty = true;
        _sortedVenuesDirty = true;
        _sortedVenueRowMetricsDirty = true;
        _sortedVenueRowMetricsWrapWidth = -1f;
    }
}
