namespace RedAlert.Simulation;

public sealed partial class BattleMap
{
    const byte OriginalLandBlocked = 1, OriginalWaterBlocked = 2, OriginalPlacementBlocked = 4;
    byte[] originalObjectCollision = [];
    public IReadOnlyList<ClassicMapObjectPlacement> OriginalObjects { get; private set; } = Array.Empty<ClassicMapObjectPlacement>();
    public int UnmappedOriginalObjects { get; private set; }
    public IReadOnlyList<byte> OriginalObjectCollision { get; private set; } = Array.Empty<byte>();

    public bool OriginalObjectBlocksMovement(int x, int y, MovementDomain domain) => Inside(x, y)
        && originalObjectCollision.Length != 0 && domain != MovementDomain.Air
        && (originalObjectCollision[Index(x, y)] & (domain == MovementDomain.Water ? OriginalWaterBlocked : OriginalLandBlocked)) != 0;
    public bool OriginalObjectBlocksPlacement(int x, int y) => Inside(x, y) && originalObjectCollision.Length != 0
        && (originalObjectCollision[Index(x, y)] & OriginalPlacementBlocked) != 0;

    void ApplyOriginalObjects(ClassicMapDefinition definition)
    {
        originalObjectCollision = new byte[Width * Height];
        var placements = new List<ClassicMapObjectPlacement>(); UnmappedOriginalObjects = 0;
        foreach (var source in definition.Objects)
        {
            if (!ClassicMapObjectRules.TryDecode(definition, source, out var placement)) { UnmappedOriginalObjects++; continue; }
            placements.Add(placement);
            AddOriginalObjectCollision(placement);
        }
        OriginalObjects = placements.AsReadOnly();
        OriginalObjectCollision = Array.AsReadOnly(originalObjectCollision);
    }

    void RebuildOriginalObjectCollision()
    {
        Array.Clear(originalObjectCollision);
        for (var i = 0; i < OriginalObjects.Count; i++)
            if (!IsOriginalObjectDestroyed(i)) AddOriginalObjectCollision(OriginalObjects[i]);
    }

    void AddOriginalObjectCollision(ClassicMapObjectPlacement placement)
    {
        var rule = placement.Definition;
        var flags = (byte)((rule.BlocksLandMovement ? OriginalLandBlocked : 0)
            | (rule.BlocksWaterMovement ? OriginalWaterBlocked : 0) | (rule.BlocksPlacement ? OriginalPlacementBlocked : 0));
        if (flags == 0) return;
        for (var dy = 0; dy < placement.Height; dy++) for (var dx = 0; dx < placement.Width; dx++)
        {
            var x = (long)placement.X + dx; var y = (long)placement.Y + dy;
            if (x >= 0 && y >= 0 && x < Width && y < Height) originalObjectCollision[Index((int)x, (int)y)] |= flags;
        }
    }
}
