namespace RedAlert.Simulation;

public enum TerrainTargetKind { Tree, Bridge }

/// <summary>A map-owned target. Negative IDs never allocate or impersonate world entities.</summary>
public sealed class TerrainTarget
{
    public int Id { get; init; }
    public TerrainTargetKind Kind { get; init; }
    public int ObjectIndex { get; init; } = -1;
    public int BridgeId { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Health { get; internal set; }
    public int MaxHealth { get; init; }
    public string Armor { get; init; } = "";
    public bool Immune { get; init; }
    public bool Alive => Health > 0;
}

public sealed partial class BattleMap
{
    bool terrainTargetsInitialized;
    IReadOnlyList<TerrainTarget> terrainTargets = Array.Empty<TerrainTarget>();
    readonly Dictionary<int, TerrainTarget> terrainTargetsById = new();
    readonly Dictionary<int, TerrainTarget> terrainBridgeTargets = new();
    bool[] destroyedOriginalObjects = [];
    // Imported LandPassable/Buildable include original [Terrain] occupancy.
    // Keep that occupancy in the surviving-object mask, separately from TMP support.
    bool[] originalTreeCells = [];
    // Captured once, before any collapse. BridgeIds/immutable source data remain intact.
    bool[] terrainBridgeCells = [];
    byte[] terrainBridgeUnderlyingLevels = [];

    public IReadOnlyList<TerrainTarget> TerrainTargets
    {
        get { InitializeTerrainTargets(); return terrainTargets; }
    }

    // Original maps initialize at the end of ApplyOriginalMap. Procedural maps
    // initialize lazily, after their constructor has finished painting bridges.
    void InitializeTerrainTargets()
    {
        if (terrainTargetsInitialized) return;
        var targets = new List<TerrainTarget>();
        destroyedOriginalObjects = new bool[OriginalObjects.Count];
        originalTreeCells = new bool[Ground.Length];
        for (var i = 0; i < OriginalObjects.Count; i++)
        {
            var placement = OriginalObjects[i];
            if (!ClassicTerrainCombatRules.IsTree(placement.Definition)) continue;
            for(var dy=0;dy<placement.Height;dy++)for(var dx=0;dx<placement.Width;dx++)
                if(Inside(placement.X+dx,placement.Y+dy))originalTreeCells[Index(placement.X+dx,placement.Y+dy)]=true;
            var target = new TerrainTarget
            {
                Id = -1 - i, Kind = TerrainTargetKind.Tree, ObjectIndex = i,
                CellX = placement.X, CellY = placement.Y, Width = placement.Width, Height = placement.Height,
                Health = ClassicTerrainCombatRules.TreeStrength, MaxHealth = ClassicTerrainCombatRules.TreeStrength,
                Armor = ClassicTerrainCombatRules.TreeArmor, Immune = placement.Definition.Immune
            };
            targets.Add(target); terrainTargetsById.Add(target.Id, target);
        }
        foreach (var bridge in BridgeSpans.OrderBy(span => span.Id))
        {
            var target = new TerrainTarget
            {
                Id = -1 - OriginalObjects.Count - bridge.Id, Kind = TerrainTargetKind.Bridge, BridgeId = bridge.Id,
                CellX = bridge.Origin.X, CellY = bridge.Origin.Y, Width = bridge.SizeX, Height = bridge.SizeY,
                Health = ClassicTerrainCombatRules.BridgeStrength, MaxHealth = ClassicTerrainCombatRules.BridgeStrength,
                Armor = ClassicTerrainCombatRules.BridgeArmor
            };
            targets.Add(target); terrainTargetsById.Add(target.Id, target); terrainBridgeTargets.Add(bridge.Id, target);
        }
        terrainBridgeCells = new bool[Ground.Length];
        terrainBridgeUnderlyingLevels = new byte[Ground.Length];
        for (var i = 0; i < Ground.Length; i++)
        {
            if (!Bridges[i] || !terrainBridgeTargets.ContainsKey(BridgeIds[i])) continue;
            terrainBridgeCells[i] = true;
            var original = OriginalCellAt(i % Width, i / Width);
            // High overlays preserve the ground Level. Procedural bridges are
            // constructed on the authoritative level-zero river surface.
            var nativeHeight=original is {HighBridge:true,TileSetId:19 or 80}?original.TemplateHeight:0;
            terrainBridgeUnderlyingLevels[i] = (byte)Math.Clamp((original?.Level ?? 0)-nativeHeight, 0, 255);
        }
        terrainTargets = targets.AsReadOnly();
        terrainTargetsInitialized = true;
    }

    bool OriginalTreeHasUnderlyingLand(ClassicMapCell cell)
    {
        InitializeTerrainTargets();
        // Reproduce only the importer's unoccupied TMP policy. Unknown overlays
        // retain their original block; live/overlapping objects remain in the mask.
        return originalTreeCells[Index(cell.X,cell.Y)] && !cell.Bridge
            && cell.UnderlyingGround is Terrain.Grass or Terrain.Dirt
            && cell.TerrainType is 8 or 10 or 11 or 12 or 13 or 14
            && (cell.OverlayName.Length==0 || cell.ResourceKind>0);
    }
    bool OriginalTerrainBuildable(int x,int y)=>OriginalCellAt(x,y) is not { } cell || cell.Buildable
        || OriginalTreeHasUnderlyingLand(cell) && cell.TerrainType is 11 or 12 or 13 or 14;

    public TerrainTarget? TerrainTargetById(int id)
    {
        InitializeTerrainTargets();
        return terrainTargetsById.GetValueOrDefault(id);
    }

    /// <summary>Living tree footprints precede a bridge deck at the same cell; ties follow original object order.</summary>
    public TerrainTarget? TerrainTargetAt(int x, int y)
    {
        if (!Inside(x, y)) return null;
        InitializeTerrainTargets();
        foreach (var target in terrainTargets)
            if (target.Kind == TerrainTargetKind.Tree && target.Alive && x >= target.CellX && y >= target.CellY
                && (long)x < (long)target.CellX + target.Width && (long)y < (long)target.CellY + target.Height) return target;
        return IsBridgeCellIntact(x, y) ? terrainBridgeTargets.GetValueOrDefault(BridgeIds[Index(x, y)]) : null;
    }

    public bool IsOriginalObjectDestroyed(int objectIndex)
    {
        InitializeTerrainTargets();
        return objectIndex >= 0 && objectIndex < destroyedOriginalObjects.Length && destroyedOriginalObjects[objectIndex];
    }

    public bool IsBridgeDestroyed(int bridgeId)
    {
        InitializeTerrainTargets();
        return terrainBridgeTargets.TryGetValue(bridgeId, out var target) && !target.Alive;
    }

    public bool IsBridgeCellIntact(int x, int y) => Inside(x, y) && Bridges[Index(x, y)]
        && !IsBridgeDestroyed(BridgeIds[Index(x, y)]);

    public bool IsCollapsedBridgeCell(int x, int y)
    {
        if (!Inside(x, y)) return false;
        InitializeTerrainTargets();
        var i = Index(x, y);
        return terrainBridgeCells[i] && IsBridgeDestroyed(BridgeIds[i]);
    }

    /// <summary>Underlying surface level, available before collapse for deck/underpass classification.</summary>
    public int CollapsedBridgeSurfaceLevel(int x, int y)
    {
        if (!Inside(x, y)) return 0;
        InitializeTerrainTargets();
        var i = Index(x, y);
        return terrainBridgeCells[i] ? terrainBridgeUnderlyingLevels[i] : OriginalCellAt(x, y)?.Level ?? ElevationAt(x, y);
    }

    /// <summary>Damage is already resolved against armor by the caller; true means a new destruction only.</summary>
    public bool DamageTerrain(int targetId, int damage)
    {
        var target = TerrainTargetById(targetId);
        if (target is null || !target.Alive || target.Immune || damage <= 0) return false;
        target.Health = (int)Math.Max(0L, (long)target.Health - damage);
        if (target.Alive) return false;
        if (target.Kind == TerrainTargetKind.Tree)
        {
            destroyedOriginalObjects[target.ObjectIndex] = true;
            // Rebuild from surviving placements; clearing a footprint would erase
            // an overlapping structure's collision bits.
            RebuildOriginalObjectCollision();
        }
        else
        {
            for (var i = 0; i < terrainBridgeCells.Length; i++)
            {
                if (!terrainBridgeCells[i] || BridgeIds[i] != target.BridgeId) continue;
                Bridges[i] = false; Elevation[i] = terrainBridgeUnderlyingLevels[i];
                Ramps[i] = RampDirection.None;
            }
        }
        NavigationVersion++;
        return true;
    }

    /// <summary>Hashes HP and map-owned destruction state in stable object/span/cell order.</summary>
    public void HashTerrainState(ref StableHash h)
    {
        InitializeTerrainTargets();
        h.Add(ClassicTerrainCombatRules.Revision); h.Add(terrainTargets.Count);
        foreach (var target in terrainTargets)
        {
            h.Add(target.Id); h.Add((int)target.Kind); h.Add(target.ObjectIndex); h.Add(target.BridgeId);
            h.Add(target.CellX); h.Add(target.CellY); h.Add(target.Width); h.Add(target.Height);
            h.Add(target.Health); h.Add(target.MaxHealth); h.Add(target.Armor); h.Add(target.Immune);
        }
        h.Add(destroyedOriginalObjects.Length); foreach (var destroyed in destroyedOriginalObjects) h.Add(destroyed);
        h.Add(originalTreeCells.Length);foreach(var treeCell in originalTreeCells)h.Add(treeCell);
        h.Add(terrainBridgeCells.Length);
        for (var i = 0; i < terrainBridgeCells.Length; i++)
        {
            h.Add(terrainBridgeCells[i]);
            if (!terrainBridgeCells[i]) continue;
            h.Add(BridgeIds[i]); h.Add((int)terrainBridgeUnderlyingLevels[i]); h.Add(Bridges[i]); h.Add((int)Elevation[i]); h.Add((int)Ramps[i]);
        }
        h.Add(originalObjectCollision.Length); foreach (var collision in originalObjectCollision) h.Add((int)collision);
    }
}
