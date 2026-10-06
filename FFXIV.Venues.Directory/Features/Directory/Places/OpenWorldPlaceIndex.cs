using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace FFXIV.Venues.Directory.Features.Directory.Places;

internal enum PlaceKind
{
    Zone,
    Aetheryte,
    Aethernet,
}

// A place outside housing as the game names it: a zone, an aetheryte or an aethernet shard, with its location. TeleportName is the name in the client's language, which Lifestream looks up; MapPosition is the aetheryte's position in map coordinates when the game data has it.
internal sealed record PlaceEntry(
    string Name,
    PlaceKind Kind,
    uint TerritoryId,
    uint MapId,
    string ZoneName,
    string? TeleportName,
    Vector2? MapPosition);

// A place outside housing read from free text: the zone, the closest aetheryte or shard to teleport to, and the map coordinates when the text gave them.
internal sealed record OpenWorldPlace(
    uint TerritoryId,
    uint MapId,
    string ZoneName,
    string? TeleportName,
    Vector2? Coordinates)
{
    // The zone name with the map coordinates, when known.
    public string Describe() =>
        Coordinates is { } at
            ? string.Create(CultureInfo.InvariantCulture, $"{ZoneName} ({at.X:0.0}, {at.Y:0.0})")
            : ZoneName;
}

// Finds a zone, aetheryte or aethernet name in a free-text location: the longest name that appears as whole words wins, and the teleport is the aetheryte of that zone closest to the coordinates, when the text has any.
internal sealed partial class OpenWorldPlaceIndex
{
    // Common names of the cities, mapped to the zones the game names.
    private static readonly (string Alias, string Zone)[] CityAliases =
    [
        ("limsa", "Limsa Lominsa Lower Decks"),
        ("limsa lominsa", "Limsa Lominsa Lower Decks"),
        ("uldah", "Ul'dah - Steps of Nald"),
        ("ul dah", "Ul'dah - Steps of Nald"),
        ("gridania", "New Gridania"),
        ("ishgard", "Foundation"),
        ("sharlayan", "Old Sharlayan"),
        ("crystarium", "The Crystarium"),
        ("gold saucer", "The Gold Saucer"),
    ];

    private readonly List<(string Key, PlaceEntry Entry)> _names = [];
    private readonly Dictionary<uint, List<PlaceEntry>> _teleportsByTerritory = [];

    public OpenWorldPlaceIndex(IEnumerable<PlaceEntry> entries)
    {
        var zonesByName = new Dictionary<string, PlaceEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = Normalize(entry.Name);
            if (key.Length < 4)
            {
                continue;
            }

            _names.Add((key, entry));
            if (entry.Kind == PlaceKind.Zone)
            {
                zonesByName.TryAdd(key, entry);
            }
            else if (entry.TeleportName != null)
            {
                if (!_teleportsByTerritory.TryGetValue(entry.TerritoryId, out var list))
                {
                    _teleportsByTerritory[entry.TerritoryId] = list = [];
                }

                list.Add(entry);
            }
        }

        foreach (var (alias, zone) in CityAliases)
        {
            if (zonesByName.TryGetValue(Normalize(zone), out var entry))
            {
                _names.Add((Normalize(alias), entry));
            }
        }

        // Longest first, so a full name wins over a shorter name it contains.
        _names.Sort((left, right) => right.Key.Length.CompareTo(left.Key.Length));
    }

    public OpenWorldPlace? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var padded = $" {Normalize(text)} ";
        PlaceEntry? match = null;
        foreach (var (key, entry) in _names)
        {
            if (padded.Contains($" {key} ", StringComparison.Ordinal))
            {
                match = entry;
                break;
            }
        }

        if (match == null)
        {
            return null;
        }

        var coordinates = ReadCoordinates(text);
        var teleport = ChooseTeleport(match, coordinates);
        return new OpenWorldPlace(match.TerritoryId, match.MapId, match.ZoneName, teleport?.TeleportName, coordinates);
    }

    // Reads map coordinates written with X and Y labels or as a pair in parentheses; map coordinates run from 1 to about 42.
    public static Vector2? ReadCoordinates(string text)
    {
        var match = XyRegex().Match(text);
        if (!match.Success)
        {
            match = PairRegex().Match(text);
        }

        if (!match.Success ||
            !TryReadCoordinate(match.Groups["x"].Value, out var x) ||
            !TryReadCoordinate(match.Groups["y"].Value, out var y))
        {
            return null;
        }

        return new Vector2(x, y);
    }

    private static bool TryReadCoordinate(string text, out float value) =>
        float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        value is >= 1f and <= 43f;

    // An aetheryte named in the text is the one to go to, unless coordinates point somewhere else in the zone; then (and for a zone named alone) the closest one, aetherytes before aethernet shards.
    private PlaceEntry? ChooseTeleport(PlaceEntry match, Vector2? coordinates)
    {
        _teleportsByTerritory.TryGetValue(match.TerritoryId, out var teleports);
        if (coordinates is { } target && teleports != null)
        {
            var closest = teleports
                .Where(entry => entry.MapPosition != null)
                .OrderBy(entry => entry.Kind == PlaceKind.Aetheryte ? 0 : 1)
                .ThenBy(entry => Vector2.Distance(entry.MapPosition!.Value, target))
                .FirstOrDefault();
            if (closest != null)
            {
                return closest;
            }
        }

        if (match.Kind != PlaceKind.Zone && match.TeleportName != null)
        {
            return match;
        }

        return teleports?
            .OrderBy(entry => entry.Kind == PlaceKind.Aetheryte ? 0 : 1)
            .FirstOrDefault();
    }

    // Lower-case words; apostrophes are dropped and other punctuation becomes a space.
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\'' or '’')
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return SpacesRegex().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\bX\s*[:=]?\s*(?<x>\d{1,2}(?:[.,]\d{1,2})?)\s*[,;/|\-]?\s*Y\s*[:=]?\s*(?<y>\d{1,2}(?:[.,]\d{1,2})?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XyRegex();

    [GeneratedRegex(@"\(\s*(?<x>\d{1,2}[.,]\d)\s*[,;/]\s*(?<y>\d{1,2}[.,]\d)\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex PairRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
