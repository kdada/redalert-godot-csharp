using System.Diagnostics;
using RedAlert.Simulation;

namespace RedAlert.Tests;

public static class TerrainSurfaceChecks
{
    static readonly Cell[] Neighbors = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static int Run(string[] args)
    {
        var failed = 0; var tests = 0; var watch = Stopwatch.StartNew();
        void Test(string name, Action action)
        {
            tests++;
            try { action(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
        }

        Test("generated ramps meet both flat landings and reach seeded plateaus", () =>
        {
            foreach (var seed in new uint[] { 0, 7, 20261018, uint.MaxValue })
            {
                var map = new BattleMap(MatchSettings.Practice(8, seed));
                var same = new BattleMap(MatchSettings.Practice(8, seed));
                Require(map.Elevation.SequenceEqual(same.Elevation) && map.Ramps.SequenceEqual(same.Ramps)
                    && map.BridgeIds.SequenceEqual(same.BridgeIds) && map.BridgeSpans.SequenceEqual(same.BridgeSpans), "surface generation was not deterministic");
                var reached = Reachable(map, map.Spawns[0]); var ramps = 0; var plateau = 0;
                for (var y = 0; y < map.Height; y++)
                for (var x = 0; x < map.Width; x++)
                {
                    var cell = new Cell(x, y); var i = map.Index(x, y);
                    if (map.Elevation[i] == 2 && map.Walkable(x, y))
                    {
                        plateau++; Require(map.SurfaceCornersAt(cell).IsFlat && reached[i], "raised plateau was uneven or inaccessible");
                    }
                    if (!map.IsRamp(cell)) continue;
                    ramps++;
                    var slope = map.SurfaceSlopeFixedAt(cell);
                    var direction = new Cell(Math.Sign(slope.X), Math.Sign(slope.Y));
                    var low = new Cell(x - direction.X, y - direction.Y);
                    var high = new Cell(x + direction.X, y + direction.Y);
                    Require(map.CanTraverseTerrain(low, cell) && map.CanTraverseTerrain(cell, high)
                        && map.CanTraverseTerrain(high, cell) && map.CanTraverseTerrain(cell, low), "ramp does not meet its landings continuously in both directions");
                    var center = map.SurfaceHeightFixed(x * Rules.Scale + Rules.Scale / 2, y * Rules.Scale + Rules.Scale / 2);
                    Require(center == map.Elevation[i] * Rules.Scale + Rules.Scale / 2 && reached[i], "ramp midpoint height or reachability is wrong");
                    Require(!map.SurfaceCornersAt(cell).IsFlat && map.Ground[i] != Terrain.Water && !map.HasBridge(cell), "ramp was flat, wet or bridged");
                }
                Require(ramps == 16 && plateau >= 60, $"seed {seed}: missing complete ramps or flat plateaus");
                Console.WriteLine($"  seed={seed} ramps={ramps} plateauCells={plateau} bridgeSpans={map.BridgeSpans.Length}");
            }
        });

        Test("fixed-point ramp heights, own corners and normals agree in four directions", () =>
        {
            var map = new BattleMap(MatchSettings.Practice()); var x = 46; var y = 44; var i = map.Index(x, y);
            map.Ground[i] = Terrain.Grass; map.Bridges[i] = false; map.Elevation[i] = 1;
            foreach (var direction in new[] { RampDirection.RiseEast, RampDirection.RiseSouth, RampDirection.RiseWest, RampDirection.RiseNorth })
            {
                map.Ramps[i] = direction;
                var corners = map.SurfaceCornersAt(x, y);
                var slope = map.SurfaceSlopeFixedAt(x, y); var normal = map.SurfaceNormalFixedAt(x, y);
                Require(normal == new SurfaceNormal(-slope.X, -slope.Y, Rules.Scale), "normal does not describe the ramp plane");
                Require(Math.Abs(slope.X) + Math.Abs(slope.Y) == Rules.Scale && !corners.IsFlat, "ramp gradient/corners are flat or diagonal");
                for (var sample = 0; sample < Rules.Scale; sample += Rules.Scale / 4)
                {
                    var localX = sample; var localY = Rules.Scale / 4;
                    var rise = direction switch
                    {
                        RampDirection.RiseEast => localX,
                        RampDirection.RiseSouth => localY,
                        RampDirection.RiseWest => Rules.Scale - localX,
                        _ => Rules.Scale - localY
                    };
                    var height = map.SurfaceHeightFixed(x * Rules.Scale + localX, y * Rules.Scale + localY);
                    Require(height == Rules.Scale + rise, "continuous integer sample disagrees with the ramp plane");
                    Require(map.SurfaceHeightAt(x + localX / (double)Rules.Scale, y + localY / (double)Rules.Scale) == height / (double)Rules.Scale,
                        "render convenience sampler disagrees with authoritative height");
                }
                // At the origin, own-corner height must match the half-open cell
                // sampler even when a cliff neighbor has a different elevation.
                Require(map.SurfaceHeightFixed(x * Rules.Scale, y * Rules.Scale) == corners.NorthWest, "own NW corner disagrees with sampler");
            }
            map.Ramps[i] = RampDirection.None;
            Require(map.SurfaceNormalFixedAt(x, y) == new SurfaceNormal(0, 0, Rules.Scale), "flat normal was tilted");
            Require(map.SurfaceHeightAt(double.NaN, y) == 0 && map.SurfaceHeightFixed(-1, 0) == 0, "invalid sampler input was not bounded");
        });

        Test("unmarked cliffs and ramp sides are blocked while complete ramp entries work", () =>
        {
            var map = new BattleMap(MatchSettings.Practice());
            var low = new Cell(46, 44); var ramp = new Cell(47, 44); var high = new Cell(48, 44);
            foreach (var cell in new[] { low, ramp, high, new Cell(47, 43), new Cell(48, 43) })
            {
                var i = map.Index(cell.X, cell.Y); map.Ground[i] = Terrain.Grass; map.Ore[i] = 0;
                map.Bridges[i] = false; map.Ramps[i] = RampDirection.None; map.Elevation[i] = 1;
            }
            map.Elevation[map.Index(ramp.X, ramp.Y)] = 2;
            Require(!map.CanTraverseTerrain(low, ramp), "one-level vertical cliff was treated as a ramp");
            map.Elevation[map.Index(ramp.X, ramp.Y)] = 1;
            map.Ramps[map.Index(ramp.X, ramp.Y)] = RampDirection.RiseEast;
            map.Elevation[map.Index(high.X, high.Y)] = 2;
            Require(map.CanTraverseTerrain(low, ramp) && map.CanTraverseTerrain(ramp, high), "explicit ramp did not join its low/high landing");
            Require(!map.CanTraverseTerrain(ramp, new(47, 43)) && !map.CanTraverseTerrain(low, new(47, 43)), "unit cut across the side of a sloped tile");
            Require(!map.FootprintFree(ramp.X, ramp.Y, 1, 1), "building was permitted on a ramp");
            var path = new GridPathfinder(map).Find(low.X, low.Y, high.X, high.Y);
            Require(path.Count > 0 && path.Contains(map.Index(ramp.X, ramp.Y)), "pathfinder did not use the explicit ramp");
        });

        Test("ordinary shores and low bridge decks share the original water plane", () =>
        {
            var map=new BattleMap(MatchSettings.Practice(8));var shores=0;
            for(var y=1;y<map.Height-1;y++)for(var x=1;x<map.Width-1;x++)
            {
                var i=map.Index(x,y);
                if(map.Ground[i]!=Terrain.Water)continue;
                Require(map.SurfaceCornersAt(x,y)==new SurfaceCorners(0,0,0,0),"water/low bridge acquired an artificial elevation");
                foreach(var cell in new[]{new Cell(x-1,y),new Cell(x+1,y),new Cell(x,y-1),new Cell(x,y+1)})
                {
                    if(map.Ground[map.Index(cell.X,cell.Y)]==Terrain.Water || map.ElevationAt(cell)!=0 || map.IsRamp(cell))continue;
                    Require(map.SurfaceCornersAt(cell)==map.SurfaceCornersAt(x,y),"ordinary shore has a vertical wall instead of a shared water plane");shores++;
                }
            }
            Require(shores>100,"shoreline coverage missing");
            Require(map.BridgeSpans.All(span=>span.DeckHeightFixed==0),"low bridge floats above ordinary banks");
        });

        Test("visible mineral deposits stay inland without removing starting patches", () =>
        {
            foreach(var seed in new uint[]{0,7,20261018,uint.MaxValue})
            {
                var map=new BattleMap(MatchSettings.Practice(8,seed));var deposits=0;
                for(var y=0;y<map.Height;y++)for(var x=0;x<map.Width;x++)
                {
                    if(map.Ore[map.Index(x,y)]<=0)continue;deposits++;
                    for(var dy=-2;dy<=2;dy++)for(var dx=-2;dx<=2;dx++)
                        Require(map.Inside(x+dx,y+dy) && map.Ground[map.Index(x+dx,y+dy)]!=Terrain.Water,"mineral ground clusters can overpaint a wet shoreline");
                }
                Require(deposits>=8*25,"inland setback removed a starting resource patch");
                foreach(var spawn in map.Spawns)for(var dy=2;dy<=6;dy++)for(var dx=6;dx<=10;dx++)
                    Require(map.Ore[map.Index(spawn.X+dx,spawn.Y+dy)]>0,"starting mineral patch changed");
            }
        });

        Test("typed bridges form complete rectangular decks with dry level banks", () =>
        {
            var map = new BattleMap(MatchSettings.Practice(8));
            Require(map.BridgeSpans.Length == 3, "unexpected number of river crossings");
            var deckCount = 0;
            foreach (var span in map.BridgeSpans)
            {
                Require(span.Axis == BridgeAxis.X && span.Width == 3 && span.Length is >= 5 and <= 7, "incoherent bridge dimensions/axis");
                Require(span.BankA == new Cell(span.Origin.X - 1, span.Origin.Y + span.Width / 2)
                    && span.BankB == new Cell(span.Origin.X + span.Length, span.Origin.Y + span.Width / 2), "bank coordinates do not match span ends");
                for (var y = span.Origin.Y; y < span.Origin.Y + span.SizeY; y++)
                for (var x = span.Origin.X; x < span.Origin.X + span.SizeX; x++)
                {
                    var i = map.Index(x, y); deckCount++;
                    Require(map.BridgeAt(x, y) == span && map.BridgeIds[i] == span.Id && map.HasBridge(x, y), "bridge metadata left a missing or mixed lane");
                    Require(map.Ground[i] == Terrain.Water && !map.IsRamp(x, y) && map.SurfaceCornersAt(x, y).IsFlat
                        && map.SurfaceHeightFixed(x * Rules.Scale + Rules.Scale / 2, y * Rules.Scale + Rules.Scale / 2) == span.DeckHeightFixed, "deck is wet on top, sloped or at the wrong height");
                }
                for (var y = span.Origin.Y; y < span.Origin.Y + span.Width; y++)
                {
                    var west = span.Origin.X - 1; var east = span.Origin.X + span.Length;
                    Require(map.Ground[map.Index(west, y)] != Terrain.Water && map.Ground[map.Index(east, y)] != Terrain.Water, "bridge ended over water");
                    for (var x = west; x < east; x++)
                        Require(map.CanTraverseTerrain(new(x, y), new(x + 1, y)), "bridge or its bank has an impassable edge");
                }
            }
            Require(map.Bridges.Count(value => value) == deckCount && map.BridgeIds.Count(value => value > 0) == deckCount, "arbitrary deck tiles exist outside typed spans");
            var world = new World(MatchSettings.Practice(8));
            var miners = world.Units.Where(unit => unit.Definition.Miner).ToArray();
            var reached = Reachable(world.Map, new(miners[0].CellX, miners[0].CellY));
            Require(miners.All(unit => reached[world.Map.Index(unit.CellX, unit.CellY)]), "occupied bases no longer share a component");
            foreach (var spawn in world.Map.Spawns)
            for (var y = spawn.Y + 2; y <= spawn.Y + 6; y++)
            for (var x = spawn.X + 6; x <= spawn.X + 10; x++)
                if (world.Map.IsPlayable(x, y)) Require(world.Map.Ore[world.Map.Index(x, y)] > 0 && reached[world.Map.Index(x, y)]
                    && world.Map.SurfaceCornersAt(x, y) == new SurfaceCorners(0, 0, 0, 0), "starting ore was not flat and reachable");
        });

        Test("ramp directions and typed bridge metadata enter the authoritative hash", () =>
        {
            var world = new World(MatchSettings.Practice()); var map = world.Map;
            var original = world.StateHash(); var i = map.Index(47, 47);
            map.Ramps[i] = RampDirection.RiseEast;
            Require(world.StateHash() != original, "ramp direction omitted from hash");
            map.Ramps[i] = RampDirection.None;
            Require(world.StateHash() == original, "restoring ramp did not restore hash");
            var span = map.BridgeSpans[0]; var deck = map.Index(span.Origin.X, span.Origin.Y);
            map.BridgeIds[deck]++;
            Require(world.StateHash() != original, "bridge IDs omitted from hash");
            map.BridgeIds[deck]--;
            map.BridgeSpans[0] = span with { DeckHeightFixed = span.DeckHeightFixed + 1 };
            Require(world.StateHash() != original, "typed deck height omitted from hash");
            map.BridgeSpans[0] = span;
            Require(world.StateHash() == original, "restoring bridge metadata did not restore hash");
        });

        Console.WriteLine($"Terrain surface: {tests - failed}/{tests} passed ({watch.ElapsedMilliseconds} ms; no world steps).");
        return failed == 0 ? 0 : 1;
    }

    static bool[] Reachable(BattleMap map, Cell start)
    {
        var seen = new bool[map.Ground.Length]; var queue = new Queue<Cell>();
        seen[map.Index(start.X, start.Y)] = true; queue.Enqueue(start);
        while (queue.TryDequeue(out var cell))
        foreach (var offset in Neighbors)
        {
            var next = new Cell(cell.X + offset.X, cell.Y + offset.Y);
            if (!map.Walkable(next.X, next.Y) || !map.CanTraverseTerrain(cell, next)) continue;
            var i = map.Index(next.X, next.Y);
            if (seen[i]) continue;
            seen[i] = true; queue.Enqueue(next);
        }
        return seen;
    }
}
