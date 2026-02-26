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

internal sealed class DirectoryInterval
{
    [JsonPropertyName("intervalType")]
    public JsonElement IntervalType { get; set; }

    [JsonPropertyName("intervalArgument")]
    public JsonElement IntervalArgument { get; set; }
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
    Sunday,
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
}
