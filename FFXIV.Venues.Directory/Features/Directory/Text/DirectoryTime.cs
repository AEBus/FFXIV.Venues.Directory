using System;
using System.Globalization;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Domain;

namespace FFXIV.Venues.Directory.Features.Directory.Text;

// How times, days and durations are written everywhere in the directory, in the local time zone and the user's clock format.
internal static class DirectoryTime
{
    // The user's clock format; read by the formatters on preparation threads too.
    private static volatile bool s_use12HourClock;

    public static bool Use12HourClock
    {
        get => s_use12HourClock;
        set => s_use12HourClock = value;
    }

    internal const string PluginTimeFormat = "HH:mm";

    internal static string FormatShortTime(DateTimeOffset value) =>
        s_use12HourClock
            ? value.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture)
            : value.ToLocalTime().ToString(PluginTimeFormat, CultureInfo.CurrentCulture);

    // An opening a week or more away is written with its date instead of the weekday.
    internal static string FormatOpeningDay(DateTimeOffset value, string weekdayFormat)
    {
        var local = value.ToLocalTime();
        return (local.Date - DateTime.Today).TotalDays < 7
            ? local.ToString(weekdayFormat, CultureInfo.InvariantCulture)
            : local.ToString("MMM d", CultureInfo.InvariantCulture);
    }

    // A closing time 20 hours or more away includes its weekday, so a multi-day opening reads correctly.
    internal static string FormatClosingTime(DateTimeOffset end) =>
        end - DateTimeOffset.UtcNow >= TimeSpan.FromHours(20)
            ? $"{FormatOpeningDay(end, "ddd")} {FormatShortTime(end)}"
            : FormatShortTime(end);

    internal static string FormatStatusLine(VenueOpening? opening, bool isOpen) =>
        opening is not { } known ? "No opening set"
        : isOpen && known.IsAroundTheClock ? "Open 24/7"
        : isOpen ? $"Open until {FormatClosingTime(known.End)}"
        : $"Opens {FormatOpeningDay(known.Start, "ddd")} {FormatShortTime(known.Start)}";

    internal static string FormatRelativeTime(DateTimeOffset value)
    {
        var span = DateTimeOffset.UtcNow - value;
        return span switch
        {
            { TotalSeconds: < 45 } => "just now",
            { TotalMinutes: < 1.5 } => "a minute ago",
            { TotalHours: < 1 } => $"{Math.Round(span.TotalMinutes)} minutes ago",
            { TotalHours: < 1.5 } => "an hour ago",
            { TotalHours: < 24 } => $"{Math.Round(span.TotalHours)} hours ago",
            { TotalDays: < 2 } => "yesterday",
            _ => $"{Math.Round(span.TotalDays)} days ago",
        };
    }

    internal static string FormatRetryIn(DateTimeOffset retryAt)
    {
        var seconds = (int)Math.Ceiling((retryAt - DateTimeOffset.UtcNow).TotalSeconds);
        return seconds <= 1 ? "now" : seconds < 90 ? $"in {seconds} s" : $"in {(int)Math.Round(seconds / 60.0)} min";
    }

    internal static string FormatScheduleLabel(DirectoryInterval? interval, DayOfWeek day)
    {
        var argument = interval?.Argument ?? 1;
        return (interval?.Type ?? DirectoryIntervalType.EveryXWeeks) switch
        {
            DirectoryIntervalType.EveryXWeeks when argument <= 1 => $"Weekly on {day}s",
            DirectoryIntervalType.EveryXWeeks when argument == 2 => $"Biweekly on {day}s",
            DirectoryIntervalType.EveryXWeeks => $"Every {argument} weeks on {day}s",
            DirectoryIntervalType.EveryXthDayOfTheMonth when argument > 0 => $"{FormatOrdinal(argument)} {day} of the month",
            DirectoryIntervalType.EveryXthDayOfTheMonth when argument == -1 => $"Last {day} of the month",
            DirectoryIntervalType.EveryXthDayOfTheMonth when argument < -1 => $"{FormatOrdinal(-argument)} last {day} of the month",
            _ => $"On {day}s"
        };
    }

    internal static string FormatOrdinal(int value) =>
        (value % 100) is 11 or 12 or 13
            ? $"{value}th"
            : (value % 10) switch
            {
                1 => $"{value}st",
                2 => $"{value}nd",
                3 => $"{value}rd",
                _ => $"{value}th"
            };

    internal static int GetScheduleDaySortKey(DayOfWeek day) => ((int)day + 6) % 7;

    // The end of a range includes its day unless it is today.
    internal static string FormatAmendmentRange(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var endText = end.ToLocalTime().Date == DateTime.Today
            ? FormatShortTime(end)
            : $"{FormatOpeningDay(end, "ddd")} {FormatShortTime(end)}";
        if (start <= now)
        {
            return $"until {endText}";
        }

        var startText = $"{FormatOpeningDay(start, "ddd")} {FormatShortTime(start)}";
        return end - start < TimeSpan.FromDays(1)
            ? $"{startText} - {FormatShortTime(end)}"
            : $"{startText} - {endText}";
    }

    internal static string FormatOpensIn(TimeSpan wait)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));
        return minutes < 60
            ? $"Opens in {minutes} min"
            : minutes % 60 == 0
                ? $"Opens in {minutes / 60} h"
                : $"Opens in {minutes / 60} h {minutes % 60} min";
    }

    internal static string DescribeEventDay(DateTimeOffset startsAt)
    {
        var day = startsAt.ToLocalTime().Date;
        var today = DateTime.Now.Date;
        var label = day.ToString("ddd, MMM d", CultureInfo.InvariantCulture);
        return day == today ? $"Today · {label}"
            : day == today.AddDays(1) ? $"Tomorrow · {label}"
            : day.ToString("dddd, MMM d", CultureInfo.InvariantCulture);
    }

    internal static string FormatDuration(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        return minutes < 60 ? $"{minutes} min"
            : minutes < 48 * 60 ? (minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min")
            : $"{(int)Math.Round(span.TotalDays)} days";
    }

    // Short countdowns for the list: minutes, hours and minutes, or days.
    internal static string FormatShortDuration(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        return minutes < 60 ? $"{minutes}m"
            : minutes < 48 * 60 ? (minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m")
            : $"{(int)Math.Round(span.TotalDays)}d";
    }

    // A time tag in a description: date and time in the local time zone.
    internal static string FormatRichTime(DateTimeOffset time) =>
        $"{time.ToLocalTime().ToString("ddd, MMM d", CultureInfo.InvariantCulture)} · {FormatShortTime(time)}";

    // The tooltip of a time tag: how far away it is and the full date.
    internal static string DescribeRichTime(DateTimeOffset time)
    {
        var delta = time - DateTimeOffset.Now;
        var distance = Math.Abs(delta.TotalMinutes) < 60
            ? $"{Math.Max(1, (int)Math.Round(Math.Abs(delta.TotalMinutes)))} min"
            : Math.Abs(delta.TotalHours) < 48
                ? $"{(int)Math.Round(Math.Abs(delta.TotalHours))} h"
                : $"{(int)Math.Round(Math.Abs(delta.TotalDays))} days";
        var relative = delta >= TimeSpan.Zero ? $"in {distance}" : $"{distance} ago";
        return $"{relative} · {time.ToLocalTime().ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)} {FormatShortTime(time)}";
    }

    internal static string? BuildResolutionSummary(VenueStatus status) =>
        status.Opening is not { } opening || status.Advertised ? null
        : status.IsOpen && opening.IsAroundTheClock ? "Open now, around the clock!"
        : status.IsOpen ? $"Open now until {FormatClosingTime(opening.End)}!"
        : $"Next open {FormatOpeningDay(opening.Start, "dddd")} at {FormatShortTime(opening.Start)}";
}
