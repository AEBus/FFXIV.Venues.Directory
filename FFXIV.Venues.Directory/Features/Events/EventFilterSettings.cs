using System;
using System.Collections.Generic;

namespace FFXIV.Venues.Directory.Features.Events;

public enum EventTimeFilter
{
    LiveNow,
    Next24Hours,
    Next7Days,
    All,
}

// Which events the "Saved" card keeps: all, the ones the user is going to, or the ones at favorite venues.
public enum EventShortlist
{
    All,
    Going,
    FavoriteVenues,
}

// An event the user marked as going to (Partake's "Attend", kept in the plugin only), until a day after it ends.
[Serializable]
public sealed class GoingEvent
{
    public int Id { get; set; }
    public DateTimeOffset EndsAt { get; set; }
}

// What the Events filter cards are set to; saved with the plugin configuration. Region, data center and world are shared with the venue filters.
[Serializable]
public sealed class EventFilterSettings
{
    public EventTimeFilter Time { get; set; } = EventTimeFilter.All;
    public bool ShowEveryone { get; set; } = true;
    public bool ShowTeen { get; set; } = true;
    public bool ShowMature { get; set; } = true;
    public bool ShowAdult { get; set; } = true;

    public EventShortlist Shortlist { get; set; }
    public List<string> IncludedTags { get; set; } = [];
    public List<string> ExcludedTags { get; set; } = [];
}
