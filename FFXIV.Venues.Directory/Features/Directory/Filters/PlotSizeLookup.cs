using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Plugin.Services;
using FFXIV.Venues.Directory.Features.Directory.Domain;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;

namespace FFXIV.Venues.Directory.Features.Directory.Filters;

internal sealed class PlotSizeLookup
{
    private const int DistrictPlotCount = 60;

    private static readonly Dictionary<(byte A, byte B, byte C), string> DistrictSignatures = new()
    {
        [(1, 2, 0)] = "mist",
        [(1, 0, 2)] = "lavender beds",
        [(0, 0, 0)] = "goblet",
        [(1, 0, 0)] = "shirogane",
        [(0, 1, 0)] = "empyreum",
    };

    private readonly IDataManager _dataManager;
    private readonly object _syncRoot = new();
    private Dictionary<string, HousingPlotSize[]>? _cache;
    private bool _loaded;

    public PlotSizeLookup(IDataManager dataManager)
    {
        _dataManager = dataManager;
    }

    public bool TryGetSize(DirectoryLocation? location, out HousingPlotSize size)
    {
        size = default;

        if (location == null || location.Plot <= 0)
        {
            return false;
        }

        EnsureLoaded();
        if (_cache == null)
        {
            return false;
        }

        var districtKey = NormalizeDistrict(location.District);
        if (districtKey == null || !_cache.TryGetValue(districtKey, out var districtPlots))
        {
            return false;
        }

        var index = location.Plot - 1;
        if (index < 0 || index >= districtPlots.Length)
        {
            return false;
        }

        size = districtPlots[index];
        return true;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_loaded)
            {
                return;
            }

            _cache = BuildCache();
            _loaded = true;
        }
    }

    private Dictionary<string, HousingPlotSize[]> BuildCache()
    {
        var rowsByDistrict = new Dictionary<string, RawRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _dataManager.GetExcelSheet<RawRow>(name: "HousingLandSet"))
        {
            if (!TryReadSignature(row, out var signature))
            {
                continue;
            }

            if (!DistrictSignatures.TryGetValue(signature, out var district))
            {
                continue;
            }

            rowsByDistrict.TryAdd(district, row);
        }

        var result = new Dictionary<string, HousingPlotSize[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (district, row) in rowsByDistrict)
        {
            if (TryReadDistrictPlotSizes(row, out var sizes))
            {
                result[district] = sizes;
            }
        }

        return result;
    }

    private static bool TryReadSignature(RawRow row, out (byte A, byte B, byte C) signature)
    {
        signature = default;

        if (!TryReadUInt8(row, 0, out var a) ||
            !TryReadUInt8(row, 1, out var b) ||
            !TryReadUInt8(row, 2, out var c))
        {
            return false;
        }

        signature = (a, b, c);
        return true;
    }

    private static bool TryReadDistrictPlotSizes(RawRow row, out HousingPlotSize[] sizes)
    {
        sizes = Array.Empty<HousingPlotSize>();

        if (row.Columns.Count < DistrictPlotCount)
        {
            return false;
        }

        var output = new HousingPlotSize[DistrictPlotCount];
        for (var i = 0; i < DistrictPlotCount; i++)
        {
            if (!TryReadUInt8(row, i, out var value))
            {
                return false;
            }

            output[i] = value switch
            {
                0 => HousingPlotSize.Small,
                1 => HousingPlotSize.Medium,
                2 => HousingPlotSize.Large,
                _ => throw new InvalidOperationException("Unexpected HousingLandSet plot size value."),
            };
        }

        sizes = output;
        return true;
    }

    private static bool TryReadUInt8(RawRow row, int columnIndex, out byte value)
    {
        value = default;
        if (columnIndex < 0 || columnIndex >= row.Columns.Count)
        {
            return false;
        }

        var column = row.Columns[columnIndex];
        if (column.Type != ExcelColumnDataType.UInt8)
        {
            return false;
        }

        value = row.ReadUInt8(column.Offset);
        return true;
    }

    private static string? NormalizeDistrict(string? district)
    {
        if (string.IsNullOrWhiteSpace(district))
        {
            return null;
        }

        var normalized = district.Trim();
        if (normalized.StartsWith("the ", true, CultureInfo.InvariantCulture))
        {
            normalized = normalized[4..];
        }

        return normalized.ToLowerInvariant();
    }
}

internal enum HousingPlotSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}
