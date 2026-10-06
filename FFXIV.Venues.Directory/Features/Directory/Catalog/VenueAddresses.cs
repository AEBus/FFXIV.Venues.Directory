using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Features.Directory.Places;
using static FFXIV.Venues.Directory.Features.Directory.Text.VenueText;

namespace FFXIV.Venues.Directory.Features.Directory.Catalog;

// Venue locations: regions and data centers, address formats for the list, the details and the clipboard, and the routes to a venue (a housing address, several from a free-text override, or a spot in a zone) with the Lifestream command for each.
internal static class VenueAddresses
{
    internal static readonly string[] Regions = ["North America", "Europe", "Oceania", "Japan"];

    internal static readonly Regex OverrideRouteRegex = new(
        @"(?:(?<label>[A-Za-z0-9'&()\- ]{2,40}):\s*)?(?<address>[A-Za-z][A-Za-z0-9'’\- ]+(?:,\s*[A-Za-z][A-Za-z0-9'’\- ]+){1,2},\s*Ward\s*\d+(?:\s*(?:Sub|Subdivision))?\s*,\s*(?:Plot|Apartment|Apt)\s*\d+(?:,\s*(?:Sub|Subdivision))?(?:,\s*Room\s*\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string? ResolveRegion(string? dataCenter)
    {
        if (string.IsNullOrWhiteSpace(dataCenter))
        {
            return null;
        }

        return dataCenter.Trim() switch
        {
            "Aether" => "North America",
            "Crystal" => "North America",
            "Dynamis" => "North America",
            "Primal" => "North America",
            "Chaos" => "Europe",
            "Light" => "Europe",
            "Materia" => "Oceania",
            "Elemental" => "Japan",
            "Gaia" => "Japan",
            "Mana" => "Japan",
            "Meteor" => "Japan",
            _ => null
        };
    }

    internal static string FormatAddressForTable(DirectoryVenue venue)
    {
        var location = venue.Location;
        if (location == null)
        {
            return "Location unknown";
        }

        if (!string.IsNullOrWhiteSpace(location.Override))
        {
            var routes = ParseOverrideRouteOptions(location.Override)
                .Select(r => r.DisplayText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (routes.Count > 1)
            {
                return string.Join("\n", routes);
            }

            return location.Override;
        }

        return FormatCompactAddress(location);
    }

    // The one-line address for the list: world, district, ward and plot. The details and the tooltip show the full address.
    internal static string FormatCompactAddress(DirectoryLocation location)
    {
        var place = location.Apartment > 0
            ? $"{location.District} W{location.Ward}{(location.Subdivision ? " Sub" : string.Empty)} · Apt {location.Apartment}"
            : $"{location.District} W{location.Ward} P{location.Plot}";
        var room = location.Room > 0 ? $" · Room {location.Room}" : string.Empty;
        return string.IsNullOrWhiteSpace(location.World) ? place + room : $"{location.World} · {place}{room}";
    }

    internal static string FormatAddressDetailed(DirectoryLocation? location)
    {
        if (location == null)
        {
            return "Location unknown";
        }

        if (!string.IsNullOrWhiteSpace(location.Override))
        {
            return location.Override;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.DataCenter))
        {
            parts.Add(location.DataCenter);
        }

        if (!string.IsNullOrWhiteSpace(location.World))
        {
            parts.Add(location.World);
        }

        if (!string.IsNullOrWhiteSpace(location.District))
        {
            parts.Add(location.District);
        }

        parts.Add($"Ward {location.Ward}" + (location.Subdivision ? " (Subdivision)" : string.Empty));
        if (location.Apartment > 0)
        {
            parts.Add($"Apartment {location.Apartment}");
        }
        else
        {
            parts.Add($"Plot {location.Plot}");
        }

        if (location.Room > 0)
        {
            parts.Add($"Room {location.Room}");
        }

        if (!string.IsNullOrWhiteSpace(location.Shard))
        {
            parts.Add($"Shard {location.Shard}");
        }

        return string.Join(", ", parts);
    }

    internal static string BuildFallbackAddress(DirectoryLocation? location)
    {
        if (location == null)
        {
            return "Unknown location";
        }

        return string.Join(", ",
            new[]
            {
                location.World,
                location.District,
                location.Ward > 0 ? $"Ward {location.Ward}" : null,
                location.Apartment > 0 ? $"Apartment {location.Apartment}" : (location.Plot > 0 ? $"Plot {location.Plot}" : null),
                location.Room > 0 ? $"Room {location.Room}" : null
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    internal static VenueRouteOption[] GetImmediateRouteOptions(PreparedVenue venue)
    {
        var lifestreamArgs = FormatLifestreamArguments(venue.Source.Location);
        return
        [
            new VenueRouteOption(
                venue.DetailedAddress,
                venue.DetailedAddress,
                string.IsNullOrWhiteSpace(lifestreamArgs) ? null : lifestreamArgs)
        ];
    }

    internal static List<VenueRouteOption> BuildRouteOptions(DirectoryVenue venue, OpenWorldPlaces? places)
    {
        var options = new List<VenueRouteOption>();
        var location = venue.Location;
        if (location == null)
        {
            options.Add(new VenueRouteOption("Location unknown", "Location unknown", null));
            return options;
        }

        if (!string.IsNullOrWhiteSpace(location.Override))
        {
            options.AddRange(ParseOverrideRouteOptions(location.Override));
            if (options.Count > 0)
            {
                return options;
            }

            // Not a housing address: a spot in a zone or city, reached by teleporting to the nearest aetheryte.
            if (places?.Find(location.Override) is { } place)
            {
                var text = NormalizeDisplayText(location.Override).Replace('\n', ' ');
                var display = string.IsNullOrWhiteSpace(location.World) ? text : $"{location.World} · {text}";
                var world = string.IsNullOrWhiteSpace(location.World) ? places.FindWorld(location.Override) : location.World;
                options.Add(new VenueRouteOption(display, display, FormatPlaceLifestreamArguments(world, place), place));
                return options;
            }
        }

        var detailed = FormatAddressDetailed(location);
        var lifestreamArgs = FormatLifestreamArguments(location);
        options.Add(new VenueRouteOption(detailed, detailed, string.IsNullOrWhiteSpace(lifestreamArgs) ? null : lifestreamArgs));
        return options;
    }

    internal static IEnumerable<VenueRouteOption> ParseOverrideRouteOptions(string overrideText)
    {
        var text = Regex.Replace(overrideText, @"\s+", " ").Trim();
        if (text.Length == 0)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentLabel = null;
        foreach (Match match in OverrideRouteRegex.Matches(text))
        {
            if (!match.Success)
            {
                continue;
            }

            var label = match.Groups["label"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(label))
            {
                currentLabel = label;
            }

            var address = NormalizeRouteAddress(match.Groups["address"].Value);
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            var display = string.IsNullOrWhiteSpace(currentLabel)
                ? address
                : $"{currentLabel}: {address}";
            if (!seen.Add(display))
            {
                continue;
            }

            var lifestreamArgs = FormatLifestreamArguments(address);
            yield return new VenueRouteOption(display, address, string.IsNullOrWhiteSpace(lifestreamArgs) ? null : lifestreamArgs);
        }
    }

    internal static string NormalizeRouteAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ").Trim().TrimEnd('.');
    }

    // Lifestream travels to the world and then to the place by name. The name is passed without "tp": a name alone also reaches aethernet shards (Lifestream teleports to the city's aetheryte and takes the aethernet from there) and falls back on a plain teleport for field aetherytes, while "tp" only searches the teleport list.
    internal static string? FormatPlaceLifestreamArguments(string? world, OpenWorldPlace place) =>
        place.TeleportName == null ? null
        : string.IsNullOrWhiteSpace(world) ? place.TeleportName
        : $"{world}, {place.TeleportName}";

    internal static string FormatLifestreamArguments(DirectoryLocation? location)
    {
        if (location == null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.DataCenter))
        {
            parts.Add(location.DataCenter);
        }

        if (!string.IsNullOrWhiteSpace(location.World))
        {
            parts.Add(location.World);
        }

        if (!string.IsNullOrWhiteSpace(location.District))
        {
            var cleaned = location.District.Replace("(Subdivision)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                parts.Add(cleaned);
            }
        }

        parts.Add($"Ward {location.Ward}");
        if (location.Apartment > 0)
        {
            parts.Add($"Apartment {location.Apartment}");
            if (location.Subdivision)
            {
                parts.Add("Subdivision");
            }
        }
        else
        {
            parts.Add($"Plot {location.Plot}");

            if (location.Room > 0)
            {
                parts.Add($"Room {location.Room}");
            }
        }

        return string.Join(", ", parts);
    }

    private static string? FormatLifestreamArguments(string routeText)
    {
        if (string.IsNullOrWhiteSpace(routeText))
        {
            return null;
        }

        var tokens = routeText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim().TrimEnd('.'))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var wardToken = tokens.FirstOrDefault(t => t.StartsWith("Ward ", StringComparison.OrdinalIgnoreCase));
        if (wardToken == null)
        {
            return null;
        }

        var wardIndex = tokens.FindIndex(t => string.Equals(t, wardToken, StringComparison.OrdinalIgnoreCase));
        if (wardIndex < 2)
        {
            return null;
        }

        var plotToken = tokens.FirstOrDefault(t => t.StartsWith("Plot ", StringComparison.OrdinalIgnoreCase));
        var apartmentToken = tokens.FirstOrDefault(t =>
            t.StartsWith("Apartment ", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Apt ", StringComparison.OrdinalIgnoreCase));
        if (plotToken == null && apartmentToken == null)
        {
            return null;
        }

        var head = tokens.Take(wardIndex).ToList();
        var parts = new List<string>(head.Count + 4);
        parts.AddRange(head);
        parts.Add(NormalizeWardOrPlotToken(wardToken));
        if (apartmentToken != null)
        {
            parts.Add(NormalizeWardOrPlotToken(apartmentToken));
            if (wardToken.Contains("sub", StringComparison.OrdinalIgnoreCase) ||
                tokens.Any(t => t.Contains("subdivision", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t, "sub", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t, "s", StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add("Subdivision");
            }
        }
        else
        {
            parts.Add(NormalizeWardOrPlotToken(plotToken!));
        }

        var room = tokens.FirstOrDefault(t => t.StartsWith("Room ", StringComparison.OrdinalIgnoreCase));
        if (room != null)
        {
            parts.Add(room);
        }

        return string.Join(", ", parts);
    }

    internal static string NormalizeWardOrPlotToken(string token)
    {
        var cleaned = Regex.Replace(token, @"\s+", " ").Trim();
        if (cleaned.StartsWith("Ward", StringComparison.OrdinalIgnoreCase))
        {
            return "Ward " + cleaned.Substring(4).Trim();
        }

        if (cleaned.StartsWith("Apartment", StringComparison.OrdinalIgnoreCase))
        {
            return "Apartment " + cleaned.Substring("Apartment".Length).Trim();
        }

        if (cleaned.StartsWith("Apt", StringComparison.OrdinalIgnoreCase))
        {
            return "Apartment " + cleaned.Substring(3).Trim();
        }

        if (cleaned.StartsWith("Plot", StringComparison.OrdinalIgnoreCase))
        {
            return "Plot " + cleaned.Substring(4).Trim();
        }

        return cleaned;
    }

    internal static bool IsApartmentLocation(DirectoryLocation? location) =>
        location is { Apartment: > 0 };

    internal static string GetLocationKey(DirectoryVenue venue) =>
        $"{venue.Location?.DataCenter}-{venue.Location?.World}-{venue.Location?.District}-{venue.Location?.Ward}-{venue.Location?.Plot}";
}
