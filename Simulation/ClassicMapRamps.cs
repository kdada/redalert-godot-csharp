namespace RedAlert.Simulation;

/// <summary>Relative level offsets in logical cell-corner order: (0,0),(1,0),(1,1),(0,1).</summary>
public readonly record struct ClassicRampCorners(float NorthWest, float NorthEast, float SouthEast, float SouthWest);

/// <summary>
/// Data reconstructed independently from original slope01..20 TMP surface silhouettes,
/// their rotational families, and shared-edge heights in official map records.
/// One level is 15 native pixels. This is a data table, not original engine code.
/// Base Z diamonds are deliberately not treated as physical corner height maps.
/// Types 17/19 and 18/20 share corners; their internal saddle triangulation remains distinct
/// in the retained RampType and original artwork. Four corners alone do not identify it.
/// Type 13 faces edge-on in the native projection: its adjacent ones follow the rotational
/// family of the directly measurable types 14..16. See the reproducible geometry report.
/// </summary>
public static class ClassicMapRamps
{
    static readonly ClassicRampCorners[] Data =
    [
        new(0, 0, 0, 0),
        new(0, 1, 1, 0), new(0, 0, 1, 1), new(1, 0, 0, 1), new(1, 1, 0, 0),
        new(0, 0, 1, 0), new(0, 0, 0, 1), new(1, 0, 0, 0), new(0, 1, 0, 0),
        new(0, 1, 1, 1), new(1, 0, 1, 1), new(1, 1, 0, 1), new(1, 1, 1, 0),
        new(0, 1, 2, 1), new(1, 0, 1, 2), new(2, 1, 0, 1), new(1, 2, 1, 0),
        new(0, 1, 0, 1), new(1, 0, 1, 0), new(0, 1, 0, 1), new(1, 0, 1, 0)
    ];

    public static ClassicRampCorners Corners(int rampType) =>
        rampType >= 0 && rampType < Data.Length ? Data[rampType] :
            throw new ArgumentOutOfRangeException(nameof(rampType), rampType, "Unsupported original TMP ramp data value");

    public static bool TryGetCorners(int rampType, out ClassicRampCorners corners)
    {
        if (rampType >= 0 && rampType < Data.Length) { corners = Data[rampType]; return true; }
        corners = default;
        return false;
    }
}
