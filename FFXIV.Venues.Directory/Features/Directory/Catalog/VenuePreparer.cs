using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Directory.Places;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIV.Venues.Directory.Infrastructure.RichText;
using static FFXIV.Venues.Directory.Features.Directory.Catalog.VenueAddresses;
using static FFXIV.Venues.Directory.Features.Directory.Text.DirectoryTime;
using static FFXIV.Venues.Directory.Features.Directory.Text.VenueText;

namespace FFXIV.Venues.Directory.Features.Directory.Catalog;

// Turns the venues from the API into what the directory shows: the list entries (names, search text, tags, house size, address, status) for the whole list at once in the background, and the details of a venue (description, routes, schedule, amendments, notices) when it is first selected. A venue that fails to prepare gets a plain fallback instead of breaking the list.
internal sealed class VenuePreparer
{
    private readonly PlotSizeLookup _plotSizes;
    private readonly OpenWorldPlaces _places;

    public VenuePreparer(PlotSizeLookup plotSizes, OpenWorldPlaces places)
    {
        _plotSizes = plotSizes;
        _places = places;
    }

    internal PreparedVenue[] BuildPreparedVenues(IReadOnlyList<DirectoryVenue> venues)
    {
        var prepared = new PreparedVenue[venues.Count];
        for (var i = 0; i < venues.Count; i++)
        {
            var sourceVenue = venues[i];
            try
            {
                prepared[i] = CreatePreparedVenue(sourceVenue);
            }
            catch (Exception ex)
            {
                DalamudServices.PluginLog.Warning(ex, "Could not prepare venue {VenueId} ({VenueName}); showing a fallback.", sourceVenue.Id, sourceVenue.Name ?? string.Empty);

                prepared[i] = CreateFallbackPreparedVenue(sourceVenue, i);
            }
        }

        return prepared;
    }

    internal PreparedVenue[] BuildPreparedVenuesLightweight(IReadOnlyList<DirectoryVenue> venues)
    {
        var prepared = new PreparedVenue[venues.Count];
        for (var i = 0; i < venues.Count; i++)
        {
            var sourceVenue = venues[i];
            try
            {
                prepared[i] = CreatePreparedVenue(sourceVenue, resolvePlotSize: false);
            }
            catch (Exception ex)
            {
                DalamudServices.PluginLog.Warning(ex, "Could not prepare venue {VenueId} ({VenueName}) without house sizes; showing a fallback.", sourceVenue.Id, sourceVenue.Name ?? string.Empty);

                prepared[i] = CreateFallbackPreparedVenue(sourceVenue, i);
            }
        }

        return prepared;
    }

    internal PreparedVenue CreatePreparedVenue(DirectoryVenue venue, bool resolvePlotSize = true)
    {
        var id = GetPreparedVenueId(venue);
        var displayName = string.IsNullOrWhiteSpace(venue.Name) ? "Unnamed venue" : NormalizeDisplayText(venue.Name);
        var descriptionText = JoinNonEmptyLines(venue.Description);
        var normalizedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (venue.Tags != null)
        {
            foreach (var tag in venue.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    normalizedTags.Add(tag);
                }
            }
        }

        var searchText = BuildPreparedSearchText(displayName, descriptionText, normalizedTags);
        var region = ResolveRegion(venue.Location?.DataCenter);
        var plotSize = resolvePlotSize ? TryGetPlotSize(venue.Location) : null;
        var isApartment = IsApartmentLocation(venue.Location);
        var venueTypeLabel = GetPreparedVenueTypeLabel(isApartment, plotSize);
        var venueTypeSortKey = GetPreparedVenueTypeSortKey(isApartment, plotSize);
        var tableAddress = FormatAddressForTable(venue);
        var detailedAddress = FormatAddressDetailed(venue.Location);
        var warningText = GetVenueWarningText(IsOpenlyNsfwVenue(venue), HasAdultServicesTag(venue));
        return new PreparedVenue(
            venue,
            id,
            displayName,
            searchText,
            normalizedTags,
            region,
            venue.Location?.DataCenter,
            venue.Location?.World,
            VenueStatus.For(venue, DateTimeOffset.UtcNow),
            IsVenueNsfw(venue),
            plotSize,
            isApartment,
            venueTypeLabel,
            venueTypeSortKey,
            GetLocationKey(venue),
            tableAddress,
            detailedAddress,
            [],
            [],
            warningText);
    }

    internal HousingPlotSize? TryGetPlotSize(DirectoryLocation? location) =>
        _plotSizes.TryGetSize(location, out var size) ? size : null;

    internal PreparedVenueDetails BuildPreparedVenueDetails(PreparedVenue venue) =>
        new(
            BuildRouteOptions(venue.Source, _places).ToArray(),
            PrepareVenueDescription(venue.Source.Description),
            [],
            BuildScheduleAmendmentRows(venue.Source.ScheduleOverrides),
            BuildActiveNotices(venue.Source.Notices),
            HasScheduleEntries(venue.Source.Schedule));

    internal static PreparedVenue CreateFallbackPreparedVenue(DirectoryVenue venue, int index)
    {
        var displayName = string.IsNullOrWhiteSpace(venue.Name)
            ? $"Venue #{index + 1}"
            : venue.Name.Trim();
        var fallbackId = BuildFallbackPreparedVenueId(venue);
        var location = venue.Location;
        var region = location?.DataCenter;
        var tableAddress = BuildFallbackAddress(location);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new PreparedVenue(
            venue,
            fallbackId,
            displayName,
            NormalizeForSearch(displayName),
            tags,
            ResolveRegion(region),
            region,
            location?.World,
            VenueStatus.For(venue, DateTimeOffset.UtcNow),
            !venue.Sfw,
            null,
            location?.Apartment > 0,
            "?",
            int.MaxValue,
            $"{region ?? string.Empty}|{location?.World ?? string.Empty}|{displayName}",
            tableAddress,
            tableAddress,
            [],
            [],
            "This venue could not be fully prepared.");
    }

    internal static string BuildFallbackPreparedVenueId(DirectoryVenue venue)
    {
        if (!string.IsNullOrWhiteSpace(venue.Id))
        {
            return venue.Id;
        }

        var location = venue.Location;
        var fallbackKey = string.Join("|",
            venue.Name?.Trim() ?? string.Empty,
            location?.DataCenter?.Trim() ?? string.Empty,
            location?.World?.Trim() ?? string.Empty,
            location?.District?.Trim() ?? string.Empty,
            location?.Ward.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            location?.Plot.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            location?.Apartment.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            location?.Room.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            venue.Website?.ToString() ?? string.Empty,
            venue.Discord?.ToString() ?? string.Empty);

        return "generated:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fallbackKey)));
    }

    internal static string GetPreparedVenueId(DirectoryVenue venue)
    {
        if (!string.IsNullOrWhiteSpace(venue.Id))
        {
            return venue.Id;
        }

        var fallbackKey = string.Join("|",
            NormalizeForSearch(venue.Name),
            NormalizeForSearch(FormatAddressDetailed(venue.Location)),
            NormalizeForSearch(JoinNonEmptyLines(venue.Description)),
            venue.Website?.ToString() ?? string.Empty,
            venue.Discord?.ToString() ?? string.Empty);
        return "generated:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fallbackKey)));
    }

    internal static string GetPreparedVenueTypeLabel(bool isApartment, HousingPlotSize? size)
    {
        if (isApartment)
        {
            return "A";
        }

        return size switch
        {
            HousingPlotSize.Small => "S",
            HousingPlotSize.Medium => "M",
            HousingPlotSize.Large => "L",
            _ => "?"
        };
    }

    internal static int GetPreparedVenueTypeSortKey(bool isApartment, HousingPlotSize? size)
    {
        if (isApartment)
        {
            return 0;
        }

        return size switch
        {
            HousingPlotSize.Small => 1,
            HousingPlotSize.Medium => 2,
            HousingPlotSize.Large => 3,
            _ => 4
        };
    }

    internal static bool IsVenueNsfw(DirectoryVenue venue)
    {
        return IsOpenlyNsfwVenue(venue);
    }

    internal static bool IsOpenlyNsfwVenue(DirectoryVenue venue) => venue.Sfw == false;

    internal static bool HasAdultServicesTag(DirectoryVenue venue) =>
        venue.Tags?.Any(tag => !string.IsNullOrWhiteSpace(tag) &&
                               tag.Contains("courtesan", StringComparison.OrdinalIgnoreCase)) == true;

    internal static string? GetVenueWarningText(bool openlyNsfw, bool hasAdultServices)
    {
        if (hasAdultServices && openlyNsfw)
        {
            return "This venue has indicated they are openly NSFW and offer adult services. You must not visit this venue if you are under 18 years of age or the legal age of consent in your country, and by visiting you declare you are not. Be prepared to verify your age.";
        }

        if (hasAdultServices && !openlyNsfw)
        {
            return "This venue has indicated they offer adult services. You must not partake in these services if you are under 18 years of age or the legal age of consent in your country, and by partaking in these services you declare you are not. Be prepared to verify your age.";
        }

        if (!hasAdultServices && openlyNsfw)
        {
            return "This venue has indicated they are openly NSFW. You must not visit this venue if you are under 18 years of age or the legal age of consent in your country, and by visiting you declare you are not. Be prepared to verify your age.";
        }

        return null;
    }

    internal static PreparedVenueDetails CreateFallbackPreparedVenueDetails(PreparedVenue venue) =>
        new(
            GetImmediateRouteOptions(venue),
            RichDocument.Empty,
            [],
            [],
            [],
            false);

    // The API keeps past overrides, so only current and upcoming ones are shown, as on the website.
    internal static PreparedScheduleRow[] BuildScheduleAmendmentRows(IEnumerable<DirectoryScheduleOverride>? overrides)
    {
        if (overrides == null)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        return overrides
            .Where(o => o.Start != null && o.End != null && o.End > now)
            .OrderBy(o => o.Start)
            .Select(o => new PreparedScheduleRow(
                o.Open ? "Open" : "Closed",
                FormatAmendmentRange(o.Start!.Value, o.End!.Value, now),
                [new VenueOpening(o.Start!.Value, o.End!.Value)]))
            .ToArray();
    }

    internal static PreparedNotice[] BuildActiveNotices(IEnumerable<DirectoryNotice>? notices)
    {
        if (notices == null)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        return notices
            .Where(n => n.Start <= now && n.End > now && !string.IsNullOrWhiteSpace(n.Message))
            // Emoji become bullets in display text; a trailing bullet is removed from a single-line notice.
            .Select(n => new PreparedNotice(NormalizeDisplayText(n.Message!).TrimEnd(' ', '•'), n.IsWarning))
            .ToArray();
    }

    internal static PreparedScheduleRow[] BuildPreparedScheduleRows(IEnumerable<DirectorySchedule>? schedules)
    {
        if (schedules == null)
        {
            return [];
        }

        // Rows come from each schedule's resolution, the next concrete opening computed by the API (monthly and biweekly intervals, missing end times, the venue's time zone and DST), as on the website. The raw day and time alone are not enough for monthly schedules and day shifts.
        var now = DateTimeOffset.UtcNow;
        var entries = schedules
            .Where(schedule => schedule.Resolution != null)
            .Select(schedule =>
            {
                var resolution = schedule.Resolution!;
                var opening = VenueOpenings.NextOccurrence(schedule, now) ?? new VenueOpening(resolution.Start, resolution.End);
                var localStart = opening.Start.ToLocalTime();
                var row = new PreparedScheduleRow(
                    FormatScheduleLabel(schedule.Interval, localStart.DayOfWeek),
                    $"{FormatShortTime(opening.Start)} - {FormatShortTime(opening.End)}",
                    [opening]);
                var weekly = (schedule.Interval?.Type ?? DirectoryIntervalType.EveryXWeeks) == DirectoryIntervalType.EveryXWeeks &&
                             (schedule.Interval?.Argument ?? 1) <= 1;
                return (Row: row, Opening: opening, Day: localStart.DayOfWeek, Weekly: weekly,
                        SortKey: GetScheduleDaySortKey(localStart.DayOfWeek) * 1440 + localStart.Hour * 60 + localStart.Minute);
            })
            .OrderBy(entry => entry.SortKey)
            .ToList();

        // The same hours every day of the week are one row; a whole day each day is around the clock.
        if (entries.Count == 7 &&
            entries.All(entry => entry.Weekly) &&
            entries.Select(entry => entry.Day).Distinct().Count() == 7 &&
            entries.Select(entry => entry.Row.TimeRange).Distinct().Count() == 1)
        {
            var aroundTheClock = entries.All(entry => entry.Opening.End - entry.Opening.Start >= TimeSpan.FromHours(23.9));
            return
            [
                new PreparedScheduleRow(
                    "Every day",
                    aroundTheClock ? "24 hours" : entries[0].Row.TimeRange,
                    entries.SelectMany(entry => entry.Row.Occurrences).ToArray()),
            ];
        }

        return entries.Select(entry => entry.Row).ToArray();
    }

    internal static bool HasNonEmptyDescription(IEnumerable<string>? lines) =>
        lines?.Any(line => !string.IsNullOrWhiteSpace(line)) == true;

    internal static bool HasScheduleEntries(IEnumerable<DirectorySchedule>? schedules) =>
        schedules?.Any() == true;
}
