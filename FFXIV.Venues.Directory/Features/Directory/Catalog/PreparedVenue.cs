using System;
using System.Collections.Generic;
using System.Linq;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Directory.Places;
using FFXIV.Venues.Directory.Infrastructure.RichText;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;

namespace FFXIV.Venues.Directory.Features.Directory.Catalog;

// One route to a venue. Place is set for a spot outside housing, which can be flagged on the map.
internal sealed record VenueRouteOption(string DisplayText, string CopyText, string? LifestreamArguments, OpenWorldPlace? Place = null);

// One row of the schedule card. A row that merges several schedules holds all their occurrences.
internal sealed record PreparedScheduleRow(string Label, string TimeRange, VenueOpening[] Occurrences)
{
    public bool IsActiveAt(DateTimeOffset now) => Occurrences.Any(occurrence => occurrence.IsOpenAt(now));
}

internal sealed record PreparedNotice(string Message, bool IsWarning);

// A venue with what the list, the filters and the details show of it, prepared once per load in the background instead of every frame.
internal sealed record PreparedVenue(
    DirectoryVenue Source,
    string Id,
    string DisplayName,
    string SearchText,
    HashSet<string> Tags,
    string? Region,
    string? DataCenter,
    string? World,
    VenueStatus Status,
    bool IsNsfw,
    HousingPlotSize? PlotSize,
    bool IsApartment,
    string VenueTypeLabel,
    int VenueTypeSortKey,
    string LocationSortKey,
    string TableAddress,
    string DetailedAddress,
    VenueRouteOption[] RouteOptions,
    PreparedScheduleRow[] ScheduleRows,
    string? WarningText)
{
    public bool IsOpen => Status.IsOpen;

    public string StatusLine => Status.Line;

    public DateTimeOffset StatusSortKey => Status.SortKey;
}

// A venue's opening as of the last status update, which runs once a minute, so the list, the filters and the sort agree between updates.
internal sealed class VenueStatus
{
    public const string AdvertisedLine = "In the Party Finder now";

    public VenueOpening? Opening { get; private set; }

    public bool IsOpen { get; private set; }

    // Open by a live Party Finder ad, with no closing time known; Opening is then the ad's lifetime, which only keeps the venue in order.
    public bool Advertised { get; private set; }

    public string Line { get; private set; } = string.Empty;

    // Open venues sort by when they close, the others by when they open; no opening set sorts last.
    public DateTimeOffset SortKey { get; private set; } = DateTimeOffset.MaxValue;

    public static VenueStatus For(DirectoryVenue venue, DateTimeOffset now)
    {
        var status = new VenueStatus();
        status.Update(venue, now);
        return status;
    }

    // Returns true when the venue opened, closed or got another opening, which can move it in the list or change what the filters keep.
    public bool Update(DirectoryVenue venue, DateTimeOffset now)
    {
        var opening = VenueOpenings.Current(venue, now);
        var isOpen = opening?.IsOpenAt(now) == true;
        var advertised = !isOpen && venue.Advertisement is { } ad && ad.IsOpenAt(now);
        if (advertised)
        {
            opening = venue.Advertisement;
            isOpen = true;
        }

        var changed = opening != Opening || isOpen != IsOpen || advertised != Advertised;
        Opening = opening;
        IsOpen = isOpen;
        Advertised = advertised;
        Line = advertised ? AdvertisedLine : FormatStatusLine(opening, isOpen);
        SortKey = opening is not { } known ? DateTimeOffset.MaxValue : isOpen ? known.End : known.Start;
        return changed;
    }
}

internal sealed record PreparedVenueDetails(
    VenueRouteOption[] RouteOptions,
    RichDocument Description,
    PreparedScheduleRow[] ScheduleRows,
    PreparedScheduleRow[] ScheduleAmendments,
    PreparedNotice[] Notices,
    bool SchedulePending);
