using System;
using System.Collections.Generic;

namespace FFXIV.Venues.Directory.Features.Directory.Domain;

internal readonly record struct VenueOpening(DateTimeOffset Start, DateTimeOffset End)
{
    // A week or more without a break: the venue is open around the clock.
    public bool IsAroundTheClock => End - Start >= TimeSpan.FromDays(7);

    public bool IsOpenAt(DateTimeOffset now) => Start <= now && now < End;
}

// Works out whether a venue is open at any moment from the openings the API resolved when the list was loaded. The API resolves the venue's current or next opening (its schedules, time zone, DST and overrides) and the next opening of each schedule. While the venue's opening has not ended it is the answer; after it ends, the earliest opening still ahead takes its place: a schedule's own, a weekly schedule's moved on by its interval, or an open override, skipping the ones a closure covers. The periodic refresh brings the API's own answer soon after.
//
// Openings separated by at most MaxGap are merged into one, so consecutive days form one stretch, and a venue whose daily opening ends just before the next one starts counts as open around the clock.
internal static class VenueOpenings
{
    // Well above what any refresh interval allows; bounds the loop for a stale list.
    private const int MaxIntervalsMovedOn = 60;

    // Lets an opening that ends a minute before the next day's opening chain with it.
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);

    // Chaining stops a little past a week: enough to tell a venue that never closes.
    private static readonly TimeSpan MaxChained = TimeSpan.FromDays(8);

    // Returns the opening in progress at now, or the next one; null when the venue has no opening.
    public static VenueOpening? Current(DirectoryVenue venue, DateTimeOffset now)
    {
        if (venue.Resolution is not { } resolution)
        {
            return null;
        }

        // Looking from a moment back lets an opening that has just ended join the one that follows it; one that ended for good gives way to the next.
        var since = now - MaxGap;
        var api = new VenueOpening(resolution.Start, resolution.End);
        var known = KnownOpenings(venue, since);
        if (First(api, known, since) is { } recent && Chain(recent, known) is var chained && chained.End > now)
        {
            return chained;
        }

        return First(api, known, now) is { } next ? Chain(next, known) : null;
    }

    // Returns the API's opening while it lasts, otherwise the earliest known one that ends after the given moment.
    private static VenueOpening? First(VenueOpening api, List<VenueOpening> known, DateTimeOffset after)
    {
        if (api.End > after)
        {
            return api;
        }

        VenueOpening? first = null;
        foreach (var opening in known)
        {
            if (opening.End > after)
            {
                first = Earlier(first, opening);
            }
        }

        return first;
    }

    // Returns the schedule's opening in progress at now, or the next one, from the API's resolution. A weekly schedule is moved on by its interval; a monthly one cannot be without the calendar rules, so it returns null once past.
    public static VenueOpening? NextOccurrence(DirectorySchedule schedule, DateTimeOffset now)
    {
        if (schedule.Resolution is not { } resolution)
        {
            return null;
        }

        var opening = new VenueOpening(resolution.Start, resolution.End);
        if (opening.End > now)
        {
            return opening;
        }

        if (WeeklyStep(schedule.Interval) is not { } step)
        {
            return null;
        }

        for (var i = 0; i < MaxIntervalsMovedOn && opening.End <= now; i++)
        {
            opening = new VenueOpening(opening.Start + step, opening.End + step);
        }

        return opening.End > now ? opening : null;
    }

    // Returns every known opening that ends after now: each schedule's next occurrence (and the one after for weekly schedules, so a week can be chained), open overrides and openings known from elsewhere, without the ones closures cover.
    private static List<VenueOpening> KnownOpenings(DirectoryVenue venue, DateTimeOffset now)
    {
        var openings = new List<VenueOpening>();
        if (venue.Schedule != null)
        {
            foreach (var schedule in venue.Schedule)
            {
                if (NextOccurrence(schedule, now) is not { } opening)
                {
                    continue;
                }

                AddUnlessClosed(venue, opening, openings);
                if (WeeklyStep(schedule.Interval) is { } step)
                {
                    AddUnlessClosed(venue, new VenueOpening(opening.Start + step, opening.End + step), openings);
                }
            }
        }

        if (venue.ScheduleOverrides != null)
        {
            foreach (var amendment in venue.ScheduleOverrides)
            {
                if (amendment is { Open: true, Start: { } start, End: { } end } && end > now)
                {
                    openings.Add(new VenueOpening(start, end));
                }
            }
        }

        if (venue.KnownOpenings != null)
        {
            foreach (var opening in venue.KnownOpenings)
            {
                if (opening.End > now)
                {
                    openings.Add(opening);
                }
            }
        }

        return openings;
    }

    // Extends the opening by every known one that starts before it ends or within MaxGap after.
    private static VenueOpening Chain(VenueOpening opening, List<VenueOpening> known)
    {
        var end = opening.End;
        var extended = true;
        while (extended && end - opening.Start < MaxChained)
        {
            extended = false;
            foreach (var next in known)
            {
                if (next.Start <= end + MaxGap && next.End > end)
                {
                    end = next.End;
                    extended = true;
                }
            }
        }

        return opening with { End = end };
    }

    private static void AddUnlessClosed(DirectoryVenue venue, VenueOpening opening, List<VenueOpening> openings)
    {
        if (!IsClosed(venue, opening))
        {
            openings.Add(opening);
        }
    }

    private static TimeSpan? WeeklyStep(DirectoryInterval? interval) =>
        (interval?.Type ?? DirectoryIntervalType.EveryXWeeks) == DirectoryIntervalType.EveryXWeeks
            ? TimeSpan.FromDays(7 * Math.Max(1, interval?.Argument ?? 1))
            : null;

    private static bool IsClosed(DirectoryVenue venue, VenueOpening opening)
    {
        if (venue.ScheduleOverrides == null)
        {
            return false;
        }

        foreach (var amendment in venue.ScheduleOverrides)
        {
            if (amendment is { Open: false, Start: { } start, End: { } end } && start < opening.End && end > opening.Start)
            {
                return true;
            }
        }

        return false;
    }

    private static VenueOpening Earlier(VenueOpening? current, VenueOpening candidate) =>
        current is { } known && (known.Start < candidate.Start || (known.Start == candidate.Start && known.End >= candidate.End))
            ? known
            : candidate;
}
