using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FFXIV.Venues.Directory.Features.Directory.Catalog;
using FFXIV.Venues.Directory.Features.Directory.Places;
using FFXIV.Venues.Directory.Features.Events;

namespace FFXIV.Venues.Directory.Features.PartyFinder;

// Party Finder ads as community events, live from when they were posted until they expire. An ad that links to a Partake event takes that event's venue and address; any other ad needs a housing address in its text to say where it is, and ads without one are left out. Ads for the same address make one event.
internal static partial class PartyFinderEvents
{
    public const string FallbackTitle = "Party Finder ad";

    private const int MaxTitleLength = 40;

    // How many words right before the district are looked at for the world.
    private const int WorldWordsBeforeDistrict = 2;

    // The game font's own characters, in the Unicode private use area.
    private const char Digit0 = '\uE060';
    private const char Digit9 = '\uE069';
    private const char TimeAm = '\uE06D';
    private const char TimePm = '\uE06E';
    private const char BoxedQuestionMark = '\uE070';
    private const char BoxedLetterA = '\uE071';
    private const char BoxedLetterZ = '\uE08A';
    private const char BoxedNumber0 = '\uE08F';
    private const char BoxedNumber31 = '\uE0AE';
    private const char BoxedPlus = '\uE0AF';
    private const char BoxedRoman1 = '\uE0C1';
    private const char BoxedRoman6 = '\uE0C6';
    private const char OutlinedDigit0 = '\uE0E0';
    private const char OutlinedDigit9 = '\uE0E9';

    private static readonly string[] RomanNumerals = ["I", "II", "III", "IV", "V", "VI"];

    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase) { "the", "and", "our", "for", "with", "your", "you", "all", "its", "from", "this", "that", "here", "one" };

    // Words a name does not end with: a part ending with one is the start of a sentence.
    private static readonly HashSet<string> TrailingWords = new(StringComparer.OrdinalIgnoreCase) { "a", "an", "the", "to", "and", "or", "of", "for", "with", "at", "in", "on", "by", "from", "your", "our", "my" };

    // worldDataCenters: every public world with its data centre, in English.
    public static List<CommunityEvent> FromAds(IReadOnlyList<PartyFinderAd> ads, IReadOnlyList<CommunityEvent> partakeEvents, IReadOnlyDictionary<string, string> worldDataCenters)
    {
        var partakeById = new Dictionary<int, CommunityEvent>();
        foreach (var communityEvent in partakeEvents)
        {
            if (communityEvent.Source == EventSource.Partake)
            {
                partakeById.TryAdd(communityEvent.Id, communityEvent);
            }
        }

        var events = new List<CommunityEvent>();
        foreach (var ad in ads)
        {
            var plain = ad with { Text = Plain(ad.Text) };
            var linked = LinkedPartakeEvent(plain.Text, partakeById);
            if (linked == null && RecruitmentRegex().IsMatch(plain.Text))
            {
                continue;
            }

            var world = linked?.World ?? WorldOf(plain, worldDataCenters);
            var dataCenter = linked?.DataCenter ?? (world != null ? worldDataCenters.GetValueOrDefault(world) : null);
            var address = linked?.Address ?? PartakeLocationParser.Parse(plain.Text, dataCenter, world);
            if (address == null && linked == null)
            {
                continue;
            }

            var name = linked?.TeamName ?? TitleOf(plain.Text, worldDataCenters) ?? linked?.Title;
            events.Add(new CommunityEvent(
                EventIdOf(ad.Id),
                name ?? FallbackTitle,
                ad.PostedAt,
                ad.ExpiresAt,
                linked?.Tags ?? [],
                linked?.AgeRating ?? string.Empty,
                0,
                false,
                linked?.LocationText ?? ad.Text,
                dataCenter,
                VenueAddresses.ResolveRegion(dataCenter),
                world,
                linked?.BannerUrl,
                name,
                null)
            {
                Address = address,
                Source = EventSource.PartyFinder,
                AdText = ad.Text,
            });
        }

        return events
            .GroupBy(e => e.Address != null && EventVenueMatches.AddressKey(e.Address) is { } key ? key : e.Id.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
            .Select(Merge)
            .ToList();
    }

    // The ads several of a venue's staff posted, as one event: the first one's id, from the first posted until the last expires, with every distinct text.
    private static CommunityEvent Merge(IEnumerable<CommunityEvent> ads)
    {
        var ordered = ads.OrderBy(e => e.StartsAt).ThenBy(e => e.Id).ToList();
        if (ordered.Count == 1)
        {
            return ordered[0];
        }

        var name = ordered.Select(e => e.TeamName).FirstOrDefault(n => n != null);
        return ordered[0] with
        {
            Title = name ?? FallbackTitle,
            TeamName = name,
            EndsAt = ordered.Max(e => e.EndsAt),
            Tags = ordered.Select(e => e.Tags).FirstOrDefault(t => t.Count > 0) ?? [],
            AgeRating = ordered.Select(e => e.AgeRating).FirstOrDefault(r => r.Length > 0) ?? string.Empty,
            BannerUrl = ordered.Select(e => e.BannerUrl).FirstOrDefault(u => u != null),
            AdText = string.Join("\n\n", ordered.Select(e => e.AdText).Distinct()),
        };
    }

    // Party Finder ads get event ids below zero, so they never collide with Partake's.
    public static int EventIdOf(long listingId) => -(int)(listingId % int.MaxValue) - 1;

    // The venue's world: a world named right before the district, in full or as the start of exactly one world's name (on the recruiter's data centre, else in their region, else anywhere); else a world named in full anywhere in the text; else the recruiter's home world, which is where most venues advertise from.
    internal static string? WorldOf(PartyFinderAd ad, IReadOnlyDictionary<string, string> worldDataCenters)
    {
        var districtIndex = PartakeLocationParser.DistrictIndex(ad.Text);
        if (districtIndex > 0)
        {
            var words = WordRegex().Matches(ad.Text[..districtIndex]).Select(m => m.Value).TakeLast(WorldWordsBeforeDistrict).Reverse();
            var homeDataCenter = worldDataCenters.GetValueOrDefault(ad.HomeWorld);
            var homeRegion = VenueAddresses.ResolveRegion(homeDataCenter);
            foreach (var word in words)
            {
                if (worldDataCenters.Keys.FirstOrDefault(w => w.Equals(word, StringComparison.OrdinalIgnoreCase)) is { } named)
                {
                    return named;
                }

                var starts = worldDataCenters.Where(w => w.Key.StartsWith(word, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var nearby in (Func<KeyValuePair<string, string>, bool>[])[w => w.Value == homeDataCenter, w => homeRegion != null && VenueAddresses.ResolveRegion(w.Value) == homeRegion, _ => true])
                {
                    var fitting = starts.Where(nearby).Take(2).ToList();
                    if (fitting.Count == 1)
                    {
                        return fitting[0].Key;
                    }
                }
            }
        }

        return OpenWorldPlaces.FindWholeWord(ad.Text, worldDataCenters.Keys.OrderByDescending(w => w.Length))
               ?? (string.IsNullOrEmpty(ad.HomeWorld) ? null : ad.HomeWorld);
    }

    // A name for the ad: what it puts in brackets up front, or else the first of its parts between separators, up to the one with the address, that still names something once links, the address, times, dates, age ratings and opening words are taken out; null when no part does.
    internal static string? TitleOf(string text, IReadOnlyDictionary<string, string> worldDataCenters)
    {
        var withoutLinks = LinkRegex().Replace(text, " ");
        var parts = SeparatorRegex().Split(withoutLinks).AsEnumerable();
        if (BracketedRegex().Match(withoutLinks) is { Success: true } bracketed)
        {
            parts = parts.Prepend(bracketed.Groups[1].Value);
        }

        foreach (var part in parts)
        {
            if (NameIn(part, worldDataCenters) is not { } name)
            {
                // What follows the address describes the place.
                if (PartakeLocationParser.AddressIndex(part) >= 0)
                {
                    break;
                }

                continue;
            }

            if (name.Length <= MaxTitleLength)
            {
                return name;
            }

            var cut = name.LastIndexOf(' ', MaxTitleLength - 1);
            return string.Concat(name.AsSpan(0, cut > 0 ? cut : MaxTitleLength - 1), "…");
        }

        return null;
    }

    // The name in one part of an ad: the text before any address or verb, without noise and without world or data centre names at its end; it needs a word that is no filler and a word that starts with a capital.
    private static string? NameIn(string part, IReadOnlyDictionary<string, string> worldDataCenters)
    {
        var addressIndex = PartakeLocationParser.AddressIndex(part);
        if (addressIndex >= 0)
        {
            part = part[..addressIndex];
        }

        if (VerbRegex().Match(part) is { Success: true } verb)
        {
            part = part[..verb.Index];
        }

        var words = Clean(NoiseRegex().Replace(part, " ")).Split(' ').Where(w => w.Any(char.IsLetterOrDigit)).ToList();
        while (words.Count > 0 && (words[^1] == "I" || IsPlaceName(words[^1], worldDataCenters)))
        {
            words.RemoveAt(words.Count - 1);
        }

        // A capital I standing alone at either end is a drawn separator.
        while (words.Count > 0 && words[0] == "I")
        {
            words.RemoveAt(0);
        }

        // A name starts with a capital: lowercase words before one are what the ad says around it.
        while (words.Count > 1 && char.IsLower(words[0][0]) && words.Skip(1).Any(w => char.IsUpper(w[0])))
        {
            words.RemoveAt(0);
        }

        var named = words.Count > 0
            && !TrailingWords.Contains(words[^1])
            && words.Any(w => w.Count(char.IsLetter) >= 3 && !FillerWords.Contains(w))
            && words.Any(w => char.IsLetter(w[0]) && !char.IsLower(w[0]));
        return named ? Clean(string.Join(' ', words)) : null;
    }

    // A world or data centre named in full, or a world by the start of its name.
    private static bool IsPlaceName(string word, IReadOnlyDictionary<string, string> worldDataCenters) =>
        worldDataCenters.Values.Contains(word, StringComparer.OrdinalIgnoreCase)
        || (word.Length >= 3 && worldDataCenters.Keys.Any(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase)));

    // The game's own boxed letters, digits and signs as plain text, so that what is written in them is read too.
    internal static string Plain(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case >= BoxedLetterA and <= BoxedLetterZ:
                    builder.Append((char)('A' + (c - BoxedLetterA)));
                    break;
                case >= Digit0 and <= Digit9:
                    builder.Append((char)('0' + (c - Digit0)));
                    break;
                case >= OutlinedDigit0 and <= OutlinedDigit9:
                    builder.Append((char)('0' + (c - OutlinedDigit0)));
                    break;
                case >= BoxedNumber0 and <= BoxedNumber31:
                    builder.Append(c - BoxedNumber0);
                    break;
                case >= BoxedRoman1 and <= BoxedRoman6:
                    builder.Append(RomanNumerals[c - BoxedRoman1]);
                    break;
                case BoxedPlus:
                    builder.Append('+');
                    break;
                case BoxedQuestionMark:
                    builder.Append('?');
                    break;
                case TimeAm:
                    builder.Append("am");
                    break;
                case TimePm:
                    builder.Append("pm");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    private static CommunityEvent? LinkedPartakeEvent(string text, Dictionary<int, CommunityEvent> partakeById)
    {
        foreach (Match match in PartakeLinkRegex().Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var id) && partakeById.TryGetValue(id, out var communityEvent))
            {
                return communityEvent;
            }
        }

        return null;
    }

    // Letters, digits and the punctuation names use; everything else, decorations included, becomes a space.
    private static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '\'' or '&' or '.' or '-' ? c : ' ');
        }

        return SpacesRegex().Replace(builder.ToString(), " ").Trim(' ', '.', '-', '\'', '&');
    }

    [GeneratedRegex(@"^\W*[【\[「『(]([^】\]」』)]{3,60})[】\]」』)]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedRegex();

    // Any run of symbols other than the punctuation of names, times and dates, a dash between spaces, or a double dash.
    [GeneratedRegex(@"[^\p{L}\p{Nd}\s'&.+/:@-]+|\s-+\s|-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"(?:https?://|www\.)\S+|\b[\w-]+\.(?:gg|com|net|org|io|co|me|ly|tv|xyz|link)\b(?:/\S*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    // Words that start what an ad says about the place rather than its name.
    [GeneratedRegex(@"\b(?:is|are|was|will|has|have|opens|presents|welcomes|invites|hosts)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VerbRegex();

    // Age ratings, opening words, days, months, times, time zones, dates and ranges of numbers.
    [GeneratedRegex(
        @"(?:\+\s*\d{2}|\b\d{2}\s*\+)(?!\w)"
        + @"|\b(?:heavy|light|walk-?in)\s*-?\s*e?rp\b"
        + @"|\b(?:n?sfw|afk|wfh|lgbt\w*|lgtb\w*|open|now|only|today|tonight|tomorrow|live|until|till|daily|nightly|weekly|join|come|us|welcome|e?rp|gpose|gamba|bingo|raffles?|giveaways?|djs?|wi-?fi)\b\+?"
        + @"|\b24\s*/\s*7\b|\b24\s*h(?:ours|rs)?\b"
        + @"|\b(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tues?|wed|thu(?:rs?)?|fri|sat)s?\b"
        + @"|\b(?:january|february|march|april|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sept?|oct|nov|dec)\b"
        + @"|\b\d{1,2}(?:[:.]\d{2})?\s*(?:am|pm)\b"
        + @"|\b\d{1,2}(?:st|nd|rd|th)\b"
        + @"|\b(?:gmt|utc)\s*[+-]\s*\d{1,2}\b"
        + @"|\b(?:st|est|edt|pst|pdt|cst|cdt|mst|mdt|gmt|bst|cet|cest|eet|aest|aedt|utc|et|pt|ct|jst)\b"
        + @"|\b\d{1,2}[./]\d{1,2}(?:[./]\d{2,4})?\b\.?"
        + @"|\b\d{1,2}(?::\d{2})?\s*-\s*\d{1,2}(?::\d{2})?\b"
        + @"|@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoiseRegex();

    // A free company looking for members rather than a venue looking for guests.
    [GeneratedRegex(@"\bfree\s+company\b|\bour\b[^.!|]{0,20}\bfc\b|\bfc\s+(?:is\s+)?(?:looking|recruiting)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RecruitmentRegex();

    [GeneratedRegex(@"(?:prtk\.gg|partake\.gg/events)/(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartakeLinkRegex();

    [GeneratedRegex(@"[A-Za-z']{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();
}
