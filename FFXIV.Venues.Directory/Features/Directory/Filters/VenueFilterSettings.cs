using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FFXIV.Venues.Directory.Features.Directory.Filters;

public enum VenueTimeFilter
{
    OpenNow,
    Soon,
    AnyTime,
}

public enum VenueContentFilter
{
    All,
    SfwOnly,
    NsfwOnly,
}

public enum VenueVisitedFilter
{
    Any,
    Visited,
    NotVisited,
}

// The state of the venue filter cards, saved with the plugin configuration.
[Serializable]
public sealed class VenueFilterSettings
{
    public VenueTimeFilter Time { get; set; } = VenueTimeFilter.OpenNow;
    public VenueContentFilter Content { get; set; }
    public VenueVisitedFilter Visited { get; set; }
    public bool FavoritesOnly { get; set; }
    public bool HiddenOnly { get; set; }

    // Venues known only from their Partake events or Party Finder ads (not listed on FFXIV Venues). Saved under its earlier name.
    [JsonProperty("IncludePartakeVenues")]
    public bool IncludeUnlistedVenues { get; set; } = true;
    public bool SizeApartment { get; set; } = true;
    public bool SizeSmall { get; set; } = true;
    public bool SizeMedium { get; set; } = true;
    public bool SizeLarge { get; set; } = true;

    // Places that are not a house: outside the housing districts, or an address whose house size is not known.
    public bool SizeElsewhere { get; set; } = true;
    public string? Region { get; set; }
    public string? DataCenter { get; set; }
    public string? World { get; set; }

    // When set, the data center follows the character's current one, including during data center travel.
    public bool MyDataCenter { get; set; }
    public List<string> IncludedTags { get; set; } = [];
    public List<string> ExcludedTags { get; set; } = [];

    internal bool ShowsEveryPlace() => SizeApartment && SizeSmall && SizeMedium && SizeLarge && SizeElsewhere;

    internal bool ShowsPlace(bool isApartment, HousingPlotSize? plotSize) =>
        isApartment
            ? SizeApartment
            : plotSize switch
            {
                HousingPlotSize.Small => SizeSmall,
                HousingPlotSize.Medium => SizeMedium,
                HousingPlotSize.Large => SizeLarge,
                _ => SizeElsewhere,
            };

    internal void ShowEveryPlace() => SizeApartment = SizeSmall = SizeMedium = SizeLarge = SizeElsewhere = true;
}

// The location part of the filters, kept per character.
[Serializable]
public sealed class VenueLocationFilter
{
    public string? Region { get; set; }
    public string? DataCenter { get; set; }
    public string? World { get; set; }
    public bool MyDataCenter { get; set; }
}
