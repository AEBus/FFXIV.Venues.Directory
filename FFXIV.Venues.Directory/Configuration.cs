using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace FFXIV.Venues.Directory;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; }
    public List<string> FavoriteVenueIds { get; set; } = new();
    public List<string> VisitedVenueIds { get; set; } = new();

    public void Save(IDalamudPluginInterface pluginInterface) =>
        pluginInterface.SavePluginConfig(this);
}

