using System.Globalization;

namespace RedAlert.Simulation;

public enum ClassicMapObjectKind : byte { Terrain, Structure, CivilianVehicle, Smudge }

/// <summary>Original object DATA, independent of the frontend/atlas and entity combat.</summary>
public sealed record ClassicMapObjectRule(string Name, ClassicMapObjectKind Kind, string Image, string SourceImage,
    string SourceFoundation, int Width, int Height, bool LandPassable, bool WaterPassable, bool Buildable,
    int TemperateOccupationBits, bool Immune, bool Crushable, bool SpawnsTiberium)
{
    public bool BlocksLandMovement => Kind != ClassicMapObjectKind.Smudge && !LandPassable;
    public bool BlocksWaterMovement => Kind != ClassicMapObjectKind.Smudge && !WaterPassable;
    public bool BlocksPlacement => Kind != ClassicMapObjectKind.Smudge && !Buildable;
}

/// <summary>X/Y are the original occupied cell/top-left foundation, translated by SourceOrigin only.</summary>
public readonly record struct ClassicMapObjectPlacement(ClassicMapObject Source, ClassicMapObjectRule Definition,
    int SourceX, int SourceY, int X, int Y)
{
    public int Width => Definition.Width;
    public int Height => Definition.Height;
    public string Name => Definition.Name;
}

public static class ClassicMapObjectRules
{
    public const string Revision = "original-static-map-objects-1";
    public const string SourceRulesSha256 = "fd1e95cea0306ea78049dc81c8cd816e18c28c496872a1ff02edd50bd082062f";
    public const string SourceArtSha256 = "b477f861063a9509e87b7836b78190204c73bea5ea5595a842c456f5920ea223";
    // Static policy: missing IsPassable/Buildable => occupied, except smudge => free.
    // Art.Foundation defines occupied rectangles; vehicle missing Foundation => one cell.
    // Smudge Width/Height only describes art. Missing Immune/Crushable/SpawnsTiberium => false.
    // OccupationBits is retained, not guessed into neighboring cells. This grid model has
    // no original infantry subcell/crushing/destruction system. Unknown names are reported
    // and left unblocked, preserving their raw records without inventing invisible walls.
    public static IReadOnlyList<ClassicMapObjectRule> Definitions { get; }
    static readonly Dictionary<string, ClassicMapObjectRule> lookup;
    static ClassicMapObjectRules()
    {
        Definitions = Array.AsReadOnly(data);
        lookup = data.ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);
    }
    public static bool TryGet(string name, out ClassicMapObjectRule definition) => lookup.TryGetValue(name, out definition!);

    public static bool TryDecode(ClassicMapDefinition map, ClassicMapObject source, out ClassicMapObjectPlacement placement)
    {
        placement = default;
        var section = source.Section.ToLowerInvariant(); var values = source.Value.Split(',');
        string name; int sx, sy; ClassicMapObjectKind kind;
        if (section == "terrain")
        {
            if (values.Length < 1 || !Number(source.Key, out var packed) || packed < 0) return false;
            name = values[0].Trim(); sx = packed % 1000; sy = packed / 1000; kind = ClassicMapObjectKind.Terrain;
        }
        else if (section == "smudge")
        {
            if (values.Length < 3 || !Number(values[1], out sx) || !Number(values[2], out sy)) return false;
            name = values[0].Trim(); kind = ClassicMapObjectKind.Smudge;
        }
        else if (section is "structures" or "units")
        {
            if (values.Length < 5 || !Number(values[3], out sx) || !Number(values[4], out sy)) return false;
            name = values[1].Trim(); kind = section == "structures" ? ClassicMapObjectKind.Structure : ClassicMapObjectKind.CivilianVehicle;
        }
        else return false;
        if (!TryGet(name, out var definition) || definition.Kind != kind) return false;
        var x = (long)sx - map.SourceOriginX; var y = (long)sy - map.SourceOriginY;
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue) return false;
        placement = new(source, definition, sx, sy, (int)x, (int)y); return true;
    }
    static bool Number(string value, out int result) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    public static void AddToFingerprint(ref StableHash hash)
    {
        hash.Add(Revision); hash.Add(SourceRulesSha256); hash.Add(SourceArtSha256); hash.Add(data.Length);
        foreach (var definition in data)
        {
            hash.Add(definition.Name); hash.Add((int)definition.Kind); hash.Add(definition.Image); hash.Add(definition.SourceImage);
            hash.Add(definition.SourceFoundation); hash.Add(definition.Width); hash.Add(definition.Height);
            hash.Add(definition.LandPassable); hash.Add(definition.WaterPassable); hash.Add(definition.Buildable);
            hash.Add(definition.TemperateOccupationBits); hash.Add(definition.Immune); hash.Add(definition.Crushable); hash.Add(definition.SpawnsTiberium);
        }
    }

    // Generated from frozen MapObjects/metadata.json (91 rules/art names, 1645 original records).
    // Data only: original Rules.Image / Art.Image, Art.Foundation, flags and smudge dimensions.
    static readonly ClassicMapObjectRule[] data =
    [
        new("BURNT02", ClassicMapObjectKind.Smudge, "BURNT02", "BURNT02", "", 1, 1, true, true, true, 0, false, false, false),
        new("BURNT04", ClassicMapObjectKind.Smudge, "BURNT04", "BURNT04", "", 1, 1, true, true, true, 0, false, false, false),
        new("BURNT05", ClassicMapObjectKind.Smudge, "BURNT05", "BURNT05", "", 1, 1, true, true, true, 0, false, false, false),
        new("BURNT06", ClassicMapObjectKind.Smudge, "BURNT06", "BURNT06", "", 1, 1, true, true, true, 0, false, false, false),
        new("BURNT07", ClassicMapObjectKind.Smudge, "BURNT07", "BURNT07", "", 2, 1, true, true, true, 0, false, false, false),
        new("BURNT08", ClassicMapObjectKind.Smudge, "BURNT08", "BURNT08", "", 2, 1, true, true, true, 0, false, false, false),
        new("BURNT09", ClassicMapObjectKind.Smudge, "BURNT09", "BURNT09", "", 1, 2, true, true, true, 0, false, false, false),
        new("BURNT10", ClassicMapObjectKind.Smudge, "BURNT10", "BURNT10", "", 1, 2, true, true, true, 0, false, false, false),
        new("BURNT11", ClassicMapObjectKind.Smudge, "BURNT11", "BURNT11", "", 2, 2, true, true, true, 0, false, false, false),
        new("BURNT12", ClassicMapObjectKind.Smudge, "BURNT12", "BURNT12", "", 2, 2, true, true, true, 0, false, false, false),
        new("CAAIRP", ClassicMapObjectKind.Structure, "CAAIRP", "CAAIRP", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CABHUT", ClassicMapObjectKind.Structure, "CABHUT", "CABHUT", "1x1", 1, 1, false, false, false, 0, true, false, false),
        new("CACHIG03", ClassicMapObjectKind.Structure, "CACHIG03", "CACHIG03", "3x2", 3, 2, false, false, false, 0, false, false, false),
        new("CAFARM06", ClassicMapObjectKind.Structure, "CAFARM06", "CAFARM06", "2x2", 2, 2, false, false, false, 0, false, false, false),
        new("CAGAS01", ClassicMapObjectKind.Structure, "CAGAS01", "CAGAS01", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CAMIAM01", ClassicMapObjectKind.Structure, "CAMIAM01", "CAMIAM01", "4x2", 4, 2, false, false, false, 0, false, false, false),
        new("CAMIAM02", ClassicMapObjectKind.Structure, "CAMIAM02", "CAMIAM02", "4x2", 4, 2, false, false, false, 0, false, false, false),
        new("CAMIAM04", ClassicMapObjectKind.Structure, "CAMIAM04", "CAMIAM04", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CAMSC01", ClassicMapObjectKind.Structure, "CAMSC01", "CAMSC01", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CAMSC02", ClassicMapObjectKind.Structure, "CAMSC02", "CAMSC02", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CAMSC03", ClassicMapObjectKind.Structure, "CAMSC03", "CAMSC03", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CAMSC04", ClassicMapObjectKind.Structure, "CAMSC04", "CAMSC04", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CAMSC05", ClassicMapObjectKind.Structure, "CAMSC05", "CAMSC05", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CAMSC06", ClassicMapObjectKind.Structure, "CAMSC06", "CAMSC06", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CAMSC07", ClassicMapObjectKind.Structure, "CAMSC07", "CAMSC07", "2x2", 2, 2, false, false, false, 0, false, false, false),
        new("CAMSC08", ClassicMapObjectKind.Structure, "CAMSC08", "CAMSC08", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CAMSC09", ClassicMapObjectKind.Structure, "CAMSC09", "CAMSC09", "1x1", 1, 1, false, false, false, 0, false, false, false),
        new("CANEWY06", ClassicMapObjectKind.Structure, "CANEWY06", "CANEWY06", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CANEWY07", ClassicMapObjectKind.Structure, "CANEWY07", "CANEWY07", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CANEWY08", ClassicMapObjectKind.Structure, "CANEWY08", "CANEWY08", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CANEWY14", ClassicMapObjectKind.Structure, "CANEWY14", "CANEWY14", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CANWY24", ClassicMapObjectKind.Structure, "CANWY24", "CANWY24", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CANWY26", ClassicMapObjectKind.Structure, "CANWY26", "CANWY26", "5x3", 5, 3, false, false, false, 0, false, false, false),
        new("CAOILD", ClassicMapObjectKind.Structure, "CAOILD", "CAOILD", "2x2", 2, 2, false, false, false, 0, false, false, false),
        new("CAPARS13", ClassicMapObjectKind.Structure, "CAPARS13", "CAPARS13", "3x4", 3, 4, false, false, false, 0, false, false, false),
        new("CASTL01", ClassicMapObjectKind.Structure, "CASTL01", "CASTL01", "4x4", 4, 4, false, false, false, 0, false, false, false),
        new("CASTL03", ClassicMapObjectKind.Structure, "CASTL03", "CASTL03", "4x4", 4, 4, false, false, false, 0, false, false, false),
        new("CATEXS06", ClassicMapObjectKind.Structure, "CATEXS06", "CATEXS06", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CATEXS07", ClassicMapObjectKind.Structure, "CATEXS07", "CATEXS07", "3x3", 3, 3, false, false, false, 0, false, false, false),
        new("CATEXS08", ClassicMapObjectKind.Structure, "CATEXS08", "CATEXS08", "2x3", 2, 3, false, false, false, 0, false, false, false),
        new("CAWASH18", ClassicMapObjectKind.Structure, "CAWASH18", "CAWASH18", "2x2", 2, 2, false, false, false, 0, false, false, false),
        new("CRATER01", ClassicMapObjectKind.Smudge, "CRATER01", "CRATER01", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER02", ClassicMapObjectKind.Smudge, "CRATER02", "CRATER02", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER03", ClassicMapObjectKind.Smudge, "CRATER03", "CRATER03", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER05", ClassicMapObjectKind.Smudge, "CRATER05", "CRATER05", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER06", ClassicMapObjectKind.Smudge, "CRATER06", "CRATER06", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER07", ClassicMapObjectKind.Smudge, "CRATER07", "CRATER07", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER08", ClassicMapObjectKind.Smudge, "CRATER08", "CRATER08", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER09", ClassicMapObjectKind.Smudge, "CRATER09", "CRATER09", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER10", ClassicMapObjectKind.Smudge, "CRATER10", "CRATER10", "", 1, 1, true, true, true, 0, false, false, false),
        new("CRATER11", ClassicMapObjectKind.Smudge, "CRATER11", "CRATER11", "", 2, 2, true, true, true, 0, false, false, false),
        new("CRATER12", ClassicMapObjectKind.Smudge, "CRATER12", "CRATER12", "", 2, 2, true, true, true, 0, false, false, false),
        new("LT_SGN02", ClassicMapObjectKind.Terrain, "LT_SGN02", "LT_SGN02", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("LT_SGN03", ClassicMapObjectKind.Terrain, "LT_SGN03", "LT_SGN03", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("LT_SGN04", ClassicMapObjectKind.Terrain, "LT_SGN04", "LT_SGN04", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("PTRUCK", ClassicMapObjectKind.CivilianVehicle, "PTRUCK", "PTRUCK", "", 1, 1, false, false, false, 0, false, false, false),
        new("STANG", ClassicMapObjectKind.CivilianVehicle, "STANG", "STANG", "", 1, 1, false, false, false, 0, false, false, false),
        new("SUVB", ClassicMapObjectKind.CivilianVehicle, "SUVB", "SUVB", "", 1, 1, false, false, false, 0, false, false, false),
        new("SUVW", ClassicMapObjectKind.CivilianVehicle, "SUVW", "SUVW", "", 1, 1, false, false, false, 0, false, false, false),
        new("TAXI", ClassicMapObjectKind.CivilianVehicle, "TAXI", "TAXI", "", 1, 1, false, false, false, 0, false, false, false),
        new("TIBTRE01", ClassicMapObjectKind.Terrain, "TIBTRE01", "TIBTRE01", "1x1", 1, 1, false, false, false, 0, true, false, true),
        new("TIBTRE02", ClassicMapObjectKind.Terrain, "TIBTRE02", "TIBTRE02", "1x1", 1, 1, false, false, false, 0, true, false, true),
        new("TIBTRE03", ClassicMapObjectKind.Terrain, "TIBTRE03", "TIBTRE03", "1x1", 1, 1, false, false, false, 0, true, false, true),
        new("TREE01", ClassicMapObjectKind.Terrain, "TREE01", "TREE01", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE02", ClassicMapObjectKind.Terrain, "TREE02", "TREE02", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE03", ClassicMapObjectKind.Terrain, "TREE03", "TREE03", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE04", ClassicMapObjectKind.Terrain, "TREE04", "TREE04", "1x1", 1, 1, false, false, false, 7, false, false, false),
        new("TREE05", ClassicMapObjectKind.Terrain, "TREE05", "TREE05", "1x1", 1, 1, false, false, false, 5, false, false, false),
        new("TREE06", ClassicMapObjectKind.Terrain, "TREE06", "TREE06", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE07", ClassicMapObjectKind.Terrain, "TREE07", "TREE07", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE08", ClassicMapObjectKind.Terrain, "TREE08", "TREE08", "1x1", 1, 1, false, false, false, 7, false, false, false),
        new("TREE09", ClassicMapObjectKind.Terrain, "TREE09", "TREE09", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE10", ClassicMapObjectKind.Terrain, "TREE10", "TREE10", "1x1", 1, 1, false, false, false, 6, false, false, false),
        new("TREE11", ClassicMapObjectKind.Terrain, "TREE11", "TREE11", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE12", ClassicMapObjectKind.Terrain, "TREE12", "TREE12", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE13", ClassicMapObjectKind.Terrain, "TREE13", "TREE13", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE14", ClassicMapObjectKind.Terrain, "TREE14", "TREE14", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE15", ClassicMapObjectKind.Terrain, "TREE15", "TREE15", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE16", ClassicMapObjectKind.Terrain, "TREE16", "TREE16", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE17", ClassicMapObjectKind.Terrain, "TREE17", "TREE17", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE18", ClassicMapObjectKind.Terrain, "TREE18", "TREE18", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE19", ClassicMapObjectKind.Terrain, "TREE19", "TREE19", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE20", ClassicMapObjectKind.Terrain, "TREE20", "TREE20", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE21", ClassicMapObjectKind.Terrain, "TREE21", "TREE21", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE22", ClassicMapObjectKind.Terrain, "TREE22", "TREE22", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE23", ClassicMapObjectKind.Terrain, "TREE23", "TREE23", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TREE24", ClassicMapObjectKind.Terrain, "TREE24", "TREE24", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TRFF01", ClassicMapObjectKind.Terrain, "TRFF01", "TRFF01", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TRFF02", ClassicMapObjectKind.Terrain, "TRFF02", "TRFF02", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TRFF03", ClassicMapObjectKind.Terrain, "TRFF03", "TRFF03", "1x1", 1, 1, false, false, false, 4, false, false, false),
        new("TRFF04", ClassicMapObjectKind.Terrain, "TRFF04", "TRFF04", "1x1", 1, 1, false, false, false, 4, false, false, false)
    ];
}
