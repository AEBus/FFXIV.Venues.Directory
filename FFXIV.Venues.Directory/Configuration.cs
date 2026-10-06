using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;
using FFXIV.Venues.Directory.Features.Directory.Filters;
using FFXIV.Venues.Directory.Features.Directory.Text;
using FFXIV.Venues.Directory.Features.Events;

namespace FFXIV.Venues.Directory;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; }
    public List<string> FavoriteVenueIds { get; set; } = [];
    public List<string> VisitedVenueIds { get; set; } = [];
    public List<string> HiddenVenueIds { get; set; } = [];
    public bool FilterSidebarHidden { get; set; }
    // The clock format for times. Null in configurations saved before this setting existed; Use12HourClock then decides: 12-hour stays 12-hour, anything else follows Windows.
    public ClockFormat? Clock { get; set; }

    public bool Use12HourClock { get; set; }

    // Whether the windows use the Dalamud style instead of the plugin's own theme.
    public bool UseDalamudTheme { get; set; }

    // The plugin's interface size on top of Dalamud's UI scale; 1 is the size Dalamud draws at.
    public float InterfaceScale { get; set; } = 1f;

    // Whether images in venue and event descriptions are loaded from their hosts. When off, each image is shown as a link. Banners from FFXIV Venues and Partake always load.
    public bool LoadDescriptionImages { get; set; } = true;

    // Whether hovering a banner or a picture shows it whole in a tooltip; off, the tooltip only says where a click goes.
    public bool PreviewImagesOnHover { get; set; }

    // The last state of the venue filters. Region, data center and world are also stored per character in CharacterLocations, keyed by content ID, and follow the logged-in character.
    public VenueFilterSettings? Filters { get; set; }
    public Dictionary<ulong, VenueLocationFilter> CharacterLocations { get; set; } = [];

    // Whether Partake events are fetched at all (the Events tab and event marks on venues), and the event filters.
    public bool ShowPartakeEvents { get; set; } = true;

    // Whether venue ads from the Party Finder are fetched from xivpf.com (the Events tab and the venues they are for).
    public bool ShowPartyFinderAds { get; set; } = true;
    public EventFilterSettings? EventFilters { get; set; }
    public bool LastModeWasEvents { get; set; }

    // Events the user marked as going to; each one is dropped a day after it ends.
    public List<GoingEvent> GoingEvents { get; set; } = [];

    // Opt-in notifications: a favorite venue opens, an event at a favorite venue starts, an event the user is going to starts soon.
    public bool NotifyFavoriteOpens { get; set; }
    public bool NotifyFavoriteEvents { get; set; }
    public bool NotifyGoingEvents { get; set; }

    public void Save(IDalamudPluginInterface pluginInterface) =>
        pluginInterface.SavePluginConfig(this);
}

