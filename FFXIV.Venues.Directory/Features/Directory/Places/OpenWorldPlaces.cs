using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIV.Venues.Directory.Infrastructure;
using Lumina.Excel.Sheets;

namespace FFXIV.Venues.Directory.Features.Directory.Places;

// The game's zones, aetherytes and aethernet shards, for reading locations outside housing. Names are matched in English (what hosts write on Partake and FFXIV Venues); the teleport names are the client's, which is what Lifestream looks them up by. Built once in the background; until then Find finds nothing.
internal sealed class OpenWorldPlaces
{
    private readonly IDataManager _dataManager;
    private OpenWorldPlaceIndex? _index;
    private string[] _worlds = [];
    private IReadOnlyDictionary<string, string> _worldDataCenters = new Dictionary<string, string>();
    private int _loading;

    public OpenWorldPlaces(IDataManager dataManager)
    {
        _dataManager = dataManager;
    }

    public bool IsReady => Volatile.Read(ref _index) != null;

    // Every public world with its data centre, in English; empty until the game data is read.
    public IReadOnlyDictionary<string, string> WorldDataCenters => Volatile.Read(ref _worldDataCenters);

    public void WarmUp()
    {
        if (Interlocked.Exchange(ref _loading, 1) == 0)
        {
            Task.Run(Load);
        }
    }

    public OpenWorldPlace? Find(string? text)
    {
        WarmUp();
        return Volatile.Read(ref _index)?.Find(text);
    }

    // Finds a world named in the text as a whole word, for locations that do not give the world separately.
    public string? FindWorld(string? text) => FindWholeWord(text, Volatile.Read(ref _worlds));

    // Returns the first of the names, in the order given, that the text contains as a whole word (not inside a longer word), ignoring case; null when it contains none. Names that contain one another go longest first.
    public static string? FindWholeWord(string? text, IEnumerable<string> names)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var name in names)
        {
            var index = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var end = index + name.Length;
                if ((index == 0 || !char.IsLetter(text[index - 1])) && (end == text.Length || !char.IsLetter(text[end])))
                {
                    return name;
                }

                index = text.IndexOf(name, end, StringComparison.OrdinalIgnoreCase);
            }
        }

        return null;
    }

    private void Load()
    {
        try
        {
            var (worlds, dataCenters) = ReadWorlds();
            Volatile.Write(ref _worlds, worlds);
            Volatile.Write(ref _worldDataCenters, dataCenters);
            Volatile.Write(ref _index, new OpenWorldPlaceIndex(ReadEntries()));
        }
        catch (Exception ex)
        {
            DalamudServices.PluginLog.Warning(ex, "Could not read zones and aetherytes from the game data.");
        }
    }

    // Returns the worlds players can be on, longest name first, and the data centre of each.
    private (string[] Worlds, Dictionary<string, string> DataCenters) ReadWorlds()
    {
        var worlds = new List<string>();
        var dataCenters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dataCenterNames = _dataManager.GetExcelSheet<WorldDCGroupType>(ClientLanguage.English);
        foreach (var world in _dataManager.GetExcelSheet<World>(ClientLanguage.English))
        {
            var name = world.Name.ExtractText();
            if (world.IsPublic && name.Length >= 3)
            {
                worlds.Add(name);
                if (dataCenterNames.GetRowOrDefault(world.DataCenter.RowId)?.Name.ExtractText() is { Length: > 0 } dataCenter)
                {
                    dataCenters[name] = dataCenter;
                }
            }
        }

        worlds.Sort((left, right) => right.Length.CompareTo(left.Length));
        return (worlds.ToArray(), dataCenters);
    }

    private IEnumerable<PlaceEntry> ReadEntries()
    {
        var english = _dataManager.GetExcelSheet<Aetheryte>(ClientLanguage.English);
        var client = _dataManager.GetExcelSheet<Aetheryte>();
        var zones = new Dictionary<uint, PlaceEntry>();
        var entries = new List<PlaceEntry>();
        foreach (var aetheryte in english)
        {
            if (aetheryte.Invisible || aetheryte.Territory.ValueNullable is not { } territory || territory.RowId == 0)
            {
                continue;
            }

            var zoneName = territory.PlaceName.ValueNullable?.Name.ExtractText();
            var map = territory.Map.ValueNullable;
            if (string.IsNullOrWhiteSpace(zoneName) || map == null)
            {
                continue;
            }

            zones.TryAdd(territory.RowId, new PlaceEntry(zoneName, PlaceKind.Zone, territory.RowId, map.Value.RowId, zoneName, null, null));

            var name = aetheryte.IsAetheryte
                ? aetheryte.PlaceName.ValueNullable?.Name.ExtractText()
                : aetheryte.AethernetName.ValueNullable?.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var clientRow = client.GetRowOrDefault(aetheryte.RowId);
            var teleportName = clientRow == null
                ? name
                : (aetheryte.IsAetheryte
                    ? clientRow.Value.PlaceName.ValueNullable?.Name.ExtractText()
                    : clientRow.Value.AethernetName.ValueNullable?.Name.ExtractText()) ?? name;

            Vector2? position = null;
            if (aetheryte.Level.Count > 0 && aetheryte.Level[0].ValueNullable is { } level)
            {
                position = MapUtil.WorldToMap(new Vector2(level.X, level.Z), map.Value);
            }

            entries.Add(new PlaceEntry(
                name,
                aetheryte.IsAetheryte ? PlaceKind.Aetheryte : PlaceKind.Aethernet,
                territory.RowId,
                map.Value.RowId,
                zoneName,
                teleportName,
                position));
        }

        entries.AddRange(zones.Values);
        return entries;
    }
}
