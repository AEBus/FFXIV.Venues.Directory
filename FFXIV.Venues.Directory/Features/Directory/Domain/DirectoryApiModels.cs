using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFXIV.Venues.Directory.Features.Directory.Domain;

internal sealed class DirectoryVenue
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public List<string>? Description { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("location")]
    public DirectoryLocation? Location { get; set; }

    [JsonPropertyName("resolution")]
    public DirectoryResolution? Resolution { get; set; }

    [JsonPropertyName("schedule")]
    public List<DirectorySchedule>? Schedule { get; set; }

    [JsonPropertyName("scheduleOverrides")]
    public List<DirectoryScheduleOverride>? ScheduleOverrides { get; set; }

    [JsonPropertyName("notices")]
    public List<DirectoryNotice>? Notices { get; set; }

    [JsonPropertyName("sfw")]
    public bool Sfw { get; set; }

    [JsonPropertyName("website")]
    public Uri? Website { get; set; }

    [JsonPropertyName("discord")]
    public Uri? Discord { get; set; }

    [JsonPropertyName("bannerUri")]
    public Uri? BannerUri { get; set; }

    [JsonPropertyName("banner")]
    public Uri? Banner
    {
        get => BannerUri;
        set => BannerUri = value;
    }

    // Openings known from elsewhere than the API: the events of a venue only listed on Partake.
    [JsonIgnore]
    public IReadOnlyList<VenueOpening>? KnownOpenings { get; set; }

    // When the Party Finder ad of a venue known only from its events and ads went up and when it expires; no opening, as an ad tells nothing of the hours.
    [JsonIgnore]
    public VenueOpening? Advertisement { get; set; }
}

internal sealed class DirectoryLocation
{
    [JsonPropertyName("dataCenter")]
    public string? DataCenter { get; set; }

    [JsonPropertyName("world")]
    public string? World { get; set; }

    [JsonPropertyName("district")]
    public string? District { get; set; }

    [JsonPropertyName("ward")]
    public int Ward { get; set; }

    [JsonPropertyName("plot")]
    public int Plot { get; set; }

    [JsonPropertyName("subdivision")]
    public bool Subdivision { get; set; }

    [JsonPropertyName("apartment")]
    public int Apartment { get; set; }

    [JsonPropertyName("room")]
    public int Room { get; set; }

    [JsonPropertyName("shard")]
    public string? Shard { get; set; }

    [JsonPropertyName("override")]
    public string? Override { get; set; }
}

internal sealed class DirectoryResolution
{
    [JsonPropertyName("isNow")]
    public bool IsNow { get; set; }

    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; set; }

    [JsonPropertyName("end")]
    public DateTimeOffset End { get; set; }
}

internal sealed class DirectorySchedule
{
    [JsonPropertyName("day")]
    public DirectoryDay Day { get; set; }

    [JsonPropertyName("start")]
    public DirectoryTime? Start { get; set; }

    [JsonPropertyName("end")]
    public DirectoryTime? End { get; set; }

    [JsonPropertyName("interval")]
    public DirectoryInterval? Interval { get; set; }

    [JsonPropertyName("resolution")]
    public DirectoryResolution? Resolution { get; set; }
}

// A one-off opening or closure that amends the regular schedule. Dates are nullable so one malformed entry cannot fail the whole venue list.
internal sealed class DirectoryScheduleOverride
{
    [JsonPropertyName("open")]
    public bool Open { get; set; }

    [JsonPropertyName("start")]
    public DateTimeOffset? Start { get; set; }

    [JsonPropertyName("end")]
    public DateTimeOffset? End { get; set; }
}

internal sealed class DirectoryNotice
{
    [JsonPropertyName("start")]
    public DateTimeOffset? Start { get; set; }

    [JsonPropertyName("end")]
    public DateTimeOffset? End { get; set; }

    // 0 = information, 1 = warning, 2 = critical.
    [JsonPropertyName("type")]
    public JsonElement Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonIgnore]
    public bool IsWarning => Type.ValueKind == JsonValueKind.Number
        ? Type.TryGetInt32(out var value) && value > 0
        : Type.ValueKind == JsonValueKind.String && !string.Equals(Type.GetString(), "Information", StringComparison.OrdinalIgnoreCase);
}

internal sealed class DirectoryInterval
{
    [JsonPropertyName("intervalType")]
    public JsonElement IntervalType { get; set; }

    [JsonPropertyName("intervalArgument")]
    public JsonElement IntervalArgument { get; set; }

    // The API sends numbers; names are accepted too so a serializer change cannot break the whole venue list.
    [JsonIgnore]
    public DirectoryIntervalType Type => IntervalType.ValueKind switch
    {
        JsonValueKind.Number when IntervalType.TryGetInt32(out var value) => (DirectoryIntervalType)value,
        JsonValueKind.String when Enum.TryParse<DirectoryIntervalType>(IntervalType.GetString(), out var value) => value,
        _ => DirectoryIntervalType.Unknown,
    };

    [JsonIgnore]
    public int Argument => IntervalArgument.ValueKind switch
    {
        JsonValueKind.Number when IntervalArgument.TryGetInt32(out var value) => value,
        JsonValueKind.String when int.TryParse(IntervalArgument.GetString(), out var value) => value,
        _ => 1,
    };
}

internal enum DirectoryIntervalType
{
    Unknown = -1,
    EveryXWeeks = 0,

    // The argument picks the weekday occurrence in the month; negative values count from the end (-1 = last).
    EveryXthDayOfTheMonth = 1,
}

internal sealed class DirectoryTime
{
    [JsonPropertyName("hour")]
    public int Hour { get; set; }

    [JsonPropertyName("minute")]
    public int Minute { get; set; }

    [JsonPropertyName("nextDay")]
    public bool NextDay { get; set; }

    [JsonPropertyName("timeZone")]
    public string TimeZone { get; set; } = "UTC";
}

internal enum DirectoryDay
{
    // The directory API serializes weekdays as Monday=0 through Sunday=6.
    Monday = 0,
    Tuesday = 1,
    Wednesday = 2,
    Thursday = 3,
    Friday = 4,
    Saturday = 5,
    Sunday = 6,
}
