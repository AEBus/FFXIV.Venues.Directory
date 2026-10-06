using System;
using System.Collections.Generic;
using Dalamud.Game;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using FFXIV.Venues.Directory.Infrastructure;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace FFXIV.Venues.Directory.Features.Directory.Places;

// The housing address the character is at: a plot in a residential district, inside a house, or inside an apartment; null anywhere else, the district's streets included. Ward and plot come from the housing manager, the district from the territory (the house's original territory when inside, since interiors are shared between districts), with the territory's region as a fallback.
internal static class CurrentAddress
{
    // TerritoryIntendedUse of the residential districts' outdoor territories.
    private const uint ResidentialDistrictUse = 13;

    // The residential districts from the game sheets, by outdoor territory and by region, with the English names FFXIV Venues uses (address keys ignore a leading "The").
    private static readonly Lazy<(Dictionary<uint, string> ByTerritory, Dictionary<uint, string> ByRegion)> Districts =
        new(LoadDistricts);

    // Framework thread only (the housing manager is game memory).
    public static unsafe DirectoryLocation? Read()
    {
        if (!DalamudServices.ClientState.IsLoggedIn)
        {
            return null;
        }

        var manager = HousingManager.Instance();
        if (manager == null)
        {
            return null;
        }

        var ward = manager->GetCurrentWard();
        var world = DalamudServices.PlayerState.CurrentWorld.ValueNullable?.Name.ExtractText();
        if (ward < 0 || string.IsNullOrEmpty(world))
        {
            return null;
        }

        var inside = manager->IsInside();
        var territory = DalamudServices.ClientState.TerritoryType;
        var district = inside
            ? District(HousingManager.GetOriginalHouseTerritoryTypeId()) ?? District(territory)
            : District(territory);
        if (district == null)
        {
            return null;
        }

        var location = new DirectoryLocation { World = world, District = district, Ward = ward + 1 };
        var house = manager->GetCurrentHouseId();
        if (inside && house.IsApartment)
        {
            var room = manager->GetCurrentRoom();
            location.Apartment = room > 0 ? room : house.RoomNumber;
            location.Subdivision = house.ApartmentDivision == 1;
            return location.Apartment > 0 ? location : null;
        }

        var plot = manager->GetCurrentPlot();
        if (plot < 0)
        {
            return null;
        }

        location.Plot = plot + 1;
        return location;
    }

    private static string? District(uint territory)
    {
        if (territory == 0)
        {
            return null;
        }

        var (byTerritory, byRegion) = Districts.Value;
        if (byTerritory.TryGetValue(territory, out var district))
        {
            return district;
        }

        var row = DalamudServices.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory);
        return row != null && byRegion.TryGetValue(row.Value.PlaceNameRegion.RowId, out district) ? district : null;
    }

    private static (Dictionary<uint, string> ByTerritory, Dictionary<uint, string> ByRegion) LoadDistricts()
    {
        var byTerritory = new Dictionary<uint, string>();
        var byRegion = new Dictionary<uint, string>();
        var names = DalamudServices.DataManager.GetExcelSheet<PlaceName>(ClientLanguage.English);
        foreach (var territory in DalamudServices.DataManager.GetExcelSheet<TerritoryType>())
        {
            if (territory.TerritoryIntendedUse.RowId != ResidentialDistrictUse ||
                names.GetRowOrDefault(territory.PlaceName.RowId)?.Name.ExtractText() is not { Length: > 0 } name)
            {
                continue;
            }

            byTerritory[territory.RowId] = name;
            byRegion.TryAdd(territory.PlaceNameRegion.RowId, name);
        }

        return (byTerritory, byRegion);
    }
}
