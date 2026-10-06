using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FFXIV.Venues.Directory.Features.Directory.Domain;

namespace FFXIV.Venues.Directory.Features.Events;

// Where a community event comes from.
internal enum EventSource
{
    Partake,
    PartyFinder,
}

// An FFXIV community event: one listed on Partake, as the list query returns it (no description; that is fetched per event), or a venue ad from the Party Finder.
internal sealed record CommunityEvent(
    int Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<string> Tags,
    string AgeRating,
    int AttendeeCount,
    bool IsRecurring,
    string LocationText,
    string? DataCenter,
    string? Region,
    string? World,
    string? BannerUrl,
    string? TeamName,
    string? TeamIconUrl)
{
    public EventSource Source { get; init; } = EventSource.Partake;

    // A Party Finder ad's text, which comes with the listing.
    public string? AdText { get; init; }

    // The event's page on Partake; null for a Party Finder ad, which has none.
    public string? Url => Source == EventSource.Partake ? PartakeClient.WebsiteEventUrl + Id : null;

    // The housing address read from the free-text location, with the data center and world Partake gives separately; null when the text names no residential district, ward and plot or apartment.
    public DirectoryLocation? Address { get; init; }

    public bool IsLive(DateTimeOffset now) => StartsAt <= now && now < EndsAt;
}

// Partake's location is free text typed by the host: districts, wards and plots come in many abbreviations, separators and decorations.
internal static partial class PartakeLocationParser
{
    private static readonly (string Name, Regex Regex)[] Districts =
    [
        ("Mist", MistRegex()),
        ("Lavender Beds", LavenderBedsRegex()),
        ("Goblet", GobletRegex()),
        ("Shirogane", ShiroganeRegex()),
        ("Empyreum", EmpyreumRegex()),
    ];

    public static DirectoryLocation? Parse(string? text, string? dataCenter, string? world)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (FindDistrict(text) is not (var district, var districtIndex))
        {
            return null;
        }

        var ward = MatchNumber(WardRegex(), text);
        if (ward == 0)
        {
            ward = MatchNumber(WardAfterNumberRegex(), text);
        }

        var plot = MatchNumber(PlotRegex(), text);
        if (plot == 0)
        {
            plot = MatchNumber(PlotAfterNumberRegex(), text);
        }
        var apartment = MatchNumber(ApartmentRegex(), text);
        if (ward == 0 && plot == 0 && PairRegex().Match(text, districtIndex) is { Success: true } pair)
        {
            // A number pair after the district: ward, then plot.
            ward = int.Parse(pair.Groups[1].Value);
            plot = int.Parse(pair.Groups[2].Value);
        }

        if (ward is < 1 or > 30 || (apartment == 0 && plot is < 1 or > 60))
        {
            return null;
        }

        return new DirectoryLocation
        {
            DataCenter = dataCenter,
            World = world,
            District = district,
            Ward = ward,
            Plot = apartment > 0 ? 0 : plot,
            Apartment = apartment,
            Subdivision = apartment > 0 && SubdivisionRegex().IsMatch(text),
        };
    }

    // Where the text names the residential district of its address (see FindDistrict), or -1 when it names none.
    public static int DistrictIndex(string text) => FindDistrict(text)?.Index ?? -1;

    // Where the text first names part of a housing address (a district, ward, plot or apartment), or -1 when it names none.
    public static int AddressIndex(string text)
    {
        var index = DistrictIndex(text);
        foreach (var regex in (Regex[])[WardRegex(), PlotRegex(), ApartmentRegex()])
        {
            if (regex.Match(text) is { Success: true } match && (index < 0 || match.Index < index))
            {
                index = match.Index;
            }
        }

        return index;
    }

    // Returns the residential district of the address and where the text names it; null when it names none. A district word can also be part of a name, so of several the one with a number closest after it wins (addresses give the ward and plot after the district), then the first named.
    private static (string Name, int Index)? FindDistrict(string text)
    {
        (string Name, int Index)? best = null;
        var bestDistance = int.MaxValue;
        foreach (var (name, regex) in Districts)
        {
            foreach (Match match in regex.Matches(text))
            {
                var digit = text.AsSpan(match.Index + match.Length).IndexOfAnyInRange('0', '9');
                var distance = digit < 0 ? int.MaxValue : digit;
                if (best == null || distance < bestDistance || (distance == bestDistance && match.Index < best.Value.Index))
                {
                    best = (name, match.Index);
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    private static int MatchNumber(Regex regex, string text) =>
        regex.Match(text) is { Success: true } match ? int.Parse(match.Groups[1].Value) : 0;

    [GeneratedRegex(@"\bmist\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MistRegex();

    [GeneratedRegex(@"\blav(?:end|ed)?(?:er)?\.?\s*beds?\b|\blavenderbeds\b|\blavederbeds\b|\blb\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LavenderBedsRegex();

    [GeneratedRegex(@"\bgoblet\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GobletRegex();

    // Short forms, and misspellings that keep the start of the name.
    [GeneratedRegex(@"\bshiro(?:gane)?\b|\bshir[ao]g[a-z]*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShiroganeRegex();

    // Short forms, and misspellings that keep the start of the name.
    [GeneratedRegex(@"\bemp(?:y|yr[a-z]*)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmpyreumRegex();

    [GeneratedRegex(@"\b(?:w|ward)\s*[.:#]?\s*(\d{1,2})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WardRegex();

    [GeneratedRegex(@"(?:\b|(?<=\d))(?:p|plot)\s*[.:#]?\s*(\d{1,2})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlotRegex();

    // A number written before the word "plot".
    [GeneratedRegex(@"(?<![\d.])(\d{1,2})\s*(?:plot)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlotAfterNumberRegex();

    [GeneratedRegex(@"(?<![\d.])(\d{1,2})\s*(?:ward)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WardAfterNumberRegex();

    [GeneratedRegex(@"\b(?:apt|apartment)\s*[.:#]?\s*(\d{1,3})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApartmentRegex();

    [GeneratedRegex(@"(?<![\d:.])(\d{1,2})\s*[/-]\s*(\d{1,2})(?![\d:.])", RegexOptions.CultureInvariant)]
    private static partial Regex PairRegex();

    [GeneratedRegex(@"\bsub(?:division)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SubdivisionRegex();
}
