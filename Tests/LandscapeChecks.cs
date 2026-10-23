using System.Diagnostics;
using RedAlert.Simulation;

namespace RedAlert.Tests;

public static class LandscapeChecks
{
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

        Test("seeded elevation, contiguous seas, river water and bridge decks", () =>
        {
            foreach (var seed in new uint[] { 0, 7, 20261018, uint.MaxValue })
            {
                var map = new BattleMap(MatchSettings.Practice(8, seed));
                var same = new BattleMap(MatchSettings.Practice(8, seed));
                Require(map.Ground.SequenceEqual(same.Ground) && map.Elevation.SequenceEqual(same.Elevation)
                    && map.Bridges.SequenceEqual(same.Bridges), $"seed {seed}: generation changed between runs");
                var water = 0; var decks = 0; var elevated = 0;
                for (var y = 0; y < map.Height; y++)
                for (var x = 0; x < map.Width; x++)
                {
                    if (!map.IsPlayable(x, y)) continue;
                    var i = map.Index(x, y);
                    if (map.Ground[i] == Terrain.Water)
                    {
                        water++;
                        Require(map.Elevation[i] == 0, "water/deck elevation mismatch");
                        Require(map.Walkable(x, y) == map.Bridges[i], "water/deck land passability mismatch");
                        Require(map.Ore[i] == 0, "ore placed underwater or on a deck");
                    }
                    if (map.Bridges[i]) { decks++; Require(map.Ground[i] == Terrain.Water, "deck lacks water beneath it"); }
                    if (map.Elevation[i] >= 2) elevated++;
                }
                Require(water > 250 && LargestWaterRegion(map) > 100, $"seed {seed}: sea too fragmented or small");
                Require(decks >= 15 && elevated > 10, $"seed {seed}: missing bridge decks or raised ground");
                Console.WriteLine($"  seed={seed} water={water} largestSea={LargestWaterRegion(map)} decks={decks} raised={elevated}");
            }
            var a = new BattleMap(MatchSettings.Practice(8, 7));
            var b = new BattleMap(MatchSettings.Practice(8, 8));
            Require(!a.Elevation.SequenceEqual(b.Elevation), "different seeds did not change landscape heights");
        });

        Test("all eight clear bases and ore patches share one walkable component", () =>
        {
            foreach (var seed in new uint[] { 0, 7, 20261018, uint.MaxValue })
            {
                var map = new BattleMap(MatchSettings.Practice(8, seed));
                var reached = Reachable(map, map.Spawns[0]);
                foreach (var spawn in map.Spawns)
                {
                    Require(reached[map.Index(spawn.X, spawn.Y)], $"seed {seed}: isolated start {spawn}");
                    for (var y = spawn.Y - 4; y <= spawn.Y + 10; y++)
                    for (var x = spawn.X - 5; x <= spawn.X + 12; x++)
                    {
                        if (!map.IsPlayable(x, y)) continue;
                        Require(map.Walkable(x, y) && map.ElevationAt(x, y) == 0 && !map.HasBridge(x, y),
                            $"seed {seed}: obstructed/uneven base or approach at {x},{y}");
                    }
                    for (var y = spawn.Y + 2; y <= spawn.Y + 6; y++)
                    for (var x = spawn.X + 6; x <= spawn.X + 10; x++)
                    {
                        if (!map.IsPlayable(x, y)) continue;
                        var i = map.Index(x, y);
                        Require(map.Ore[i] > 0 && reached[i], $"seed {seed}: missing/unreachable starting ore {x},{y}");
                    }
                }
            }
            // Keep the guarantee after actual starting structures occupy their
            // footprints, rather than checking only an empty generated map.
            var world = new World(MatchSettings.Practice(8));
            var miners = world.Units.Where(unit => unit.Definition.Miner).ToArray();
            var occupiedReach = Reachable(world.Map, new(miners[0].CellX, miners[0].CellY));
            Require(miners.All(unit => occupiedReach[world.Map.Index(unit.CellX, unit.CellY)]),
                "starting structures separated the eight mining/base approaches");
        });

        Test("every bridge spans water between level walkable land approaches", () =>
        {
            var map = new BattleMap(MatchSettings.Practice(8));
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                if (!map.HasBridge(x, y)) continue;
                var west = x; var east = x;
                while (west > 0 && map.HasBridge(west, y)) west--;
                while (east < map.Width - 1 && map.HasBridge(east, y)) east++;
                Require(east - west is >= 3 and <= 12, "bridge does not form a bounded river crossing");
                Require(map.Ground[map.Index(west, y)] != Terrain.Water && map.Walkable(west, y)
                    && map.ElevationAt(west, y) == 0, "western approach is not dry, level land");
                Require(map.Ground[map.Index(east, y)] != Terrain.Water && map.Walkable(east, y)
                    && map.ElevationAt(east, y) == 0, "eastern approach is not dry, level land");
                for (var bx = west; bx < east; bx++)
                    Require(map.CanTraverseTerrain(new(bx, y), new(bx + 1, y)), "bridge/approach has an impassable grade");
            }
            var path = new GridPathfinder(map).Find(17, 47, 77, 47);
            Require(path.Count > 0 && path.Any(i => map.Bridges[i]), "land route failed to use the river crossing");
            var previous = new Cell(17, 47);
            foreach (var i in path)
            {
                var next = new Cell(i % map.Width, i / map.Width);
                Require(map.Walkable(next.X, next.Y) && map.CanTraverseTerrain(previous, next), "path crossed water or a cliff");
                previous = next;
            }
        });

        Test("terrain guards reject cliffs, water and diagonal cliff/river cuts", () =>
        {
            var map = new BattleMap(MatchSettings.Practice());
            var from = new Cell(47, 47); var to = new Cell(48, 47);
            map.Ground[map.Index(from.X, from.Y)] = Terrain.Grass;
            map.Ground[map.Index(to.X, to.Y)] = Terrain.Grass;
            map.Elevation[map.Index(from.X, from.Y)] = 1;
            map.Elevation[map.Index(to.X, to.Y)] = 4;
            Require(!map.CanTraverseTerrain(from, to) && !map.CanTraverseTerrain(to, from), "cliff jump accepted");
            map.Elevation[map.Index(to.X, to.Y)] = 2;
            Require(!map.CanTraverseTerrain(from, to), "unmarked one-level vertical cliff accepted");
            map.Ground[map.Index(to.X, to.Y)] = Terrain.Water;
            Require(!map.CanTraverseTerrain(from, to), "land unit entered open water");
            map.Bridges[map.Index(to.X, to.Y)] = true;
            map.Elevation[map.Index(to.X, to.Y)] = 1;
            Require(map.CanTraverseTerrain(from, to), "level deck rejected");
            var diagonal = new Cell(48, 48); var corner = map.Index(47, 48);
            map.Ground[map.Index(48, 48)] = Terrain.Grass; map.Elevation[map.Index(48, 48)] = 1;
            map.Ground[corner] = Terrain.Water; map.Bridges[corner] = false;
            Require(!map.CanTraverseTerrain(from, diagonal), "diagonal cut around water accepted");
            map.Ground[corner] = Terrain.Grass; map.Elevation[corner] = 4;
            Require(!map.CanTraverseTerrain(from, diagonal), "diagonal cut around cliff accepted");
            Require(!map.CanTraverseTerrain(from, new(50, 47)), "nonadjacent terrain jump accepted");
        });

        Test("elevation and bridge changes affect the authoritative state hash", () =>
        {
            var world = new World(MatchSettings.Practice(2));
            var original = world.StateHash(); var i = world.Map.Index(47, 47);
            world.Map.Elevation[i]++;
            Require(world.StateHash() != original, "elevation omitted from state hash");
            world.Map.Elevation[i]--;
            Require(world.StateHash() == original, "restoring elevation changed unrelated state");
            world.Map.Bridges[i] = !world.Map.Bridges[i];
            Require(world.StateHash() != original, "bridge layer omitted from state hash");
        });

        Console.WriteLine($"Landscape: {tests - failed}/{tests} passed ({watch.ElapsedMilliseconds} ms; no world steps).");
        return failed == 0 ? 0 : 1;
    }

    static bool[] Reachable(BattleMap map, Cell start)
    {
        var seen = new bool[map.Ground.Length]; var queue = new Queue<Cell>();
        queue.Enqueue(start); seen[map.Index(start.X, start.Y)] = true;
        while (queue.TryDequeue(out var cell))
        foreach (var offset in new Cell[] { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) })
        {
            var next = new Cell(cell.X + offset.X, cell.Y + offset.Y);
            if (!map.Walkable(next.X, next.Y) || !map.CanTraverseTerrain(cell, next)) continue;
            var i = map.Index(next.X, next.Y);
            if (seen[i]) continue;
            seen[i] = true; queue.Enqueue(next);
        }
        return seen;
    }

    static int LargestWaterRegion(BattleMap map)
    {
        var seen = new bool[map.Ground.Length]; var largest = 0;
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            if (!map.IsPlayable(x, y) || map.Ground[map.Index(x, y)] != Terrain.Water || seen[map.Index(x, y)]) continue;
            var queue = new Queue<Cell>(); queue.Enqueue(new(x, y)); seen[map.Index(x, y)] = true; var count = 0;
            while (queue.TryDequeue(out var cell))
            {
                count++;
                foreach (var offset in new Cell[] { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) })
                {
                    var nx = cell.X + offset.X; var ny = cell.Y + offset.Y;
                    if (!map.IsPlayable(nx, ny)) continue;
                    var i = map.Index(nx, ny);
                    if (seen[i] || map.Ground[i] != Terrain.Water) continue;
                    seen[i] = true; queue.Enqueue(new(nx, ny));
                }
            }
            largest = Math.Max(largest, count);
        }
        return largest;
    }
}
