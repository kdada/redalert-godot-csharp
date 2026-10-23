using System.Text.Json;

namespace RedAlert.Simulation;

/// <summary>Original map DATA contract; independent of Godot, world creation, and map generation.</summary>
public sealed class ClassicMapCatalog
{
    public const string ResourcePath = "res://Assets/Generated/Maps/catalog.json";
    public int Schema { get; init; }
    public ClassicMapInfo[] Maps { get; init; } = [];
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ClassicMapCatalog Parse(string json)
    {
        var catalog = JsonSerializer.Deserialize<ClassicMapCatalog>(json, JsonOptions)
            ?? throw new InvalidDataException("原版地图目录为空");
        if (catalog.Schema != 1 || catalog.Maps.Length < 3 ||
            catalog.Maps.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Maps.Length)
            throw new InvalidDataException("原版地图目录版本、数量或唯一标识无效");
        foreach (var map in catalog.Maps)
            if (string.IsNullOrWhiteSpace(map.Id) || map.MinPlayers < 2 || map.MaxPlayers < map.MinPlayers ||
                map.MaxPlayers > 8 || map.Width <= 0 || map.Height <= 0 ||
                !map.DataPath.StartsWith("res://Assets/Generated/Maps/", StringComparison.Ordinal))
                throw new InvalidDataException($"原版地图元数据无效: {map.Id}");
        return catalog;
    }

    public ClassicMapInfo? Find(string id) => Array.Find(Maps, map => map.Id == id);
}

public sealed class ClassicMapInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Theater { get; init; } = "";
    public string DataPath { get; init; } = "";
    public string PreviewPath { get; init; } = "";
    public int MinPlayers { get; init; }
    public int MaxPlayers { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public bool NavalSuitable { get; init; }
    public double WaterFraction { get; init; }
    public string SourceSha256 { get; init; } = "";
}

public sealed class ClassicMapDefinition
{
    public int Schema { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Theater { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string SourceSha256 { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>SourceX = runtime X + SourceOriginX; translation only, never rescaling.</summary>
    public int SourceOriginX { get; init; }
    public int SourceOriginY { get; init; }
    public ClassicMapRectangle SourceSize { get; init; } = new();
    public ClassicMapRectangle LocalSize { get; init; } = new();
    public ClassicMapUvBounds PlayableBounds { get; init; } = new();
    public ClassicMapCell[] RuntimeCells { get; init; } = [];
    public ClassicMapSpawn[] Spawns { get; init; } = [];
    public ClassicMapBridge[] Bridges { get; init; } = [];
    public ClassicMapObject[] Objects { get; init; } = [];
    public Dictionary<string, Dictionary<string, string>> MapRules { get; init; } = [];
    public bool ShipyardsAllowed { get; init; }
    int[] cellLookup = [];
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ClassicMapDefinition Parse(string json)
    {
        var map = JsonSerializer.Deserialize<ClassicMapDefinition>(json, JsonOptions)
            ?? throw new InvalidDataException("原版地图数据为空");
        if (map.Schema != 1 || map.Width <= 0 || map.Height <= 0 ||
            (long)map.Width * map.Height > 1024 * 1024 || map.Spawns.Length is < 2 or > 8)
            throw new InvalidDataException("原版地图版本、矩阵尺寸或起点数量无效");
        map.cellLookup = new int[map.Width * map.Height];
        Array.Fill(map.cellLookup, -1);
        for (var i = 0; i < map.RuntimeCells.Length; i++)
        {
            var cell = map.RuntimeCells[i];
            if (!map.Inside(cell.X, cell.Y) || cell.SourceX != cell.X + map.SourceOriginX ||
                cell.SourceY != cell.Y + map.SourceOriginY || cell.Level < 0 || cell.Level > 31)
                throw new InvalidDataException($"原版地图单元或坐标平移无效: {cell.SourceX},{cell.SourceY}");
            var index = cell.Y * map.Width + cell.X;
            if (map.cellLookup[index] >= 0) throw new InvalidDataException("原版地图存在重复坐标");
            map.cellLookup[index] = i;
        }
        foreach (var spawn in map.Spawns)
            if (map.CellAt(spawn.X, spawn.Y) is not { Playable: true, LandPassable: true })
                throw new InvalidDataException($"原版地图起点无可行走地面: {spawn.Index}");
        return map;
    }

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    /// <summary>Null means array void; it must stay unbuildable/unwalkable, not become random grass.</summary>
    public ClassicMapCell? CellAt(int x, int y)
    {
        if (!Inside(x, y) || cellLookup.Length == 0) return null;
        var index = cellLookup[y * Width + x];
        return index < 0 ? null : RuntimeCells[index];
    }
    public bool IsPlayable(int x, int y) => CellAt(x, y)?.Playable == true;
}

public sealed class ClassicMapRectangle
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>Continuous tile-corner bounds after translating source cells into the runtime array.</summary>
public sealed class ClassicMapUvBounds
{
    public int MinU { get; init; }
    public int MaxU { get; init; }
    public int MinV { get; init; }
    public int MaxV { get; init; }
}

public sealed class ClassicMapCell
{
    public int X { get; init; }
    public int Y { get; init; }
    public int SourceX { get; init; }
    public int SourceY { get; init; }
    public bool Playable { get; init; }
    public int TileIndex { get; init; }
    public int SubTile { get; init; }
    public int Level { get; init; } // Exact original IsoMapPack5 ground/deck value; retained for byte round trips.
    public int SurfaceLevel { get; init; } // Land actor surface: high-bridge overlays rise above their preserved ground Level.
    public int IceGrowth { get; init; }
    public int TileSetId { get; init; }
    public string Template { get; init; } = "";
    public int TemplateHeight { get; init; }
    public int TerrainType { get; init; }
    public int RampType { get; init; }
    public Terrain Ground { get; init; }
    public Terrain UnderlyingGround { get; init; }
    public bool LandPassable { get; init; }
    public bool WaterPassable { get; init; }
    public bool Buildable { get; init; }
    public bool Bridge { get; init; }
    public bool HighBridge { get; init; }
    public int OverlayIndex { get; init; } = 255;
    public int OverlayData { get; init; }
    public string OverlayName { get; init; } = "";
    public int ResourceKind { get; init; } // 0 none, 1 ore, 2 gems.
    public int ResourceDensity { get; init; } // Original OverlayData, without inventing a new mineral layout.
    public int ResourceValue { get; init; } // Original Rules Tiberium Value; density-to-capacity policy belongs to simulation.
}

public sealed class ClassicMapSpawn
{
    public int Index { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int SourceX { get; init; }
    public int SourceY { get; init; }
}

public sealed class ClassicMapBridge
{
    public int Id { get; init; }
    public string Axis { get; init; } = "";
    public bool High { get; init; }
    public int MinX { get; init; }
    public int MaxX { get; init; }
    public int MinY { get; init; }
    public int MaxY { get; init; }
    public int DeckLevel { get; init; }
    public Cell[] Cells { get; init; } = [];
}

/// <summary>Preserved map object records. World decides which original object types it currently supports.</summary>
public sealed class ClassicMapObject
{
    public string Section { get; init; } = "";
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
}
