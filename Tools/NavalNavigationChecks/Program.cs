using System.Diagnostics;
using System.Text.Json;
using RedAlert.Simulation;

// Focused static-map regression, not a native gameplay or production check.
// Coordinates/footprints come from full-playthrough-20261022-151859-naval/report.json.
var expectFixed = args.Contains("--expect-fixed");
var projectRoot = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".");
ClassicMapRepository.Loader = id => ClassicMapDefinition.Parse(File.ReadAllText(Path.Combine(projectRoot, "Assets/Generated/Maps", id + ".json")));
var settings = new MatchSettings(20261018, [new("Soviet", Country.Russia, 0), new("Allied", Country.America, 1)], MapId: "mp01t4", StartingUnits: 0);
var map = new BattleMap(settings);
var capturedBuildings = new (BuildingType Type, Country Country, int X, int Y)[]
{
    (BuildingType.ConstructionYard, Country.Russia, 60, 81), (BuildingType.ConstructionYard, Country.America, 110, 138),
    (BuildingType.PowerPlant, Country.Russia, 64, 83), (BuildingType.PowerPlant, Country.America, 112, 142),
    (BuildingType.Refinery, Country.Russia, 65, 85), (BuildingType.Refinery, Country.America, 111, 144),
    (BuildingType.Barracks, Country.Russia, 62, 85), (BuildingType.Barracks, Country.America, 115, 145),
    (BuildingType.WarFactory, Country.Russia, 63, 88), (BuildingType.WarFactory, Country.America, 105, 140),
    (BuildingType.NavalYard, Country.Russia, 61, 76), (BuildingType.NavalYard, Country.America, 116, 137)
};
foreach (var b in capturedBuildings)
{
    var d = Rules.Building(b.Type, b.Country);
    for (var y = b.Y; y < b.Y + d.Height; y++) for (var x = b.X; x < b.X + d.Width; x++) map.Buildings[map.Index(x, y)] = 1;
}
map.NavigationVersion++;
var start = new Cell(120, 140); var goal = new Cell(92, 100);
var reachable = Flood(map, start, MovementDomain.Water);
Require(reachable.Contains(goal), "Captured naval endpoints are disconnected");
var legacy = Reference(map, start, goal, MovementDomain.Water, null, 5000);
var complete = Reference(map, start, goal, MovementDomain.Water, null, int.MaxValue);
Require(legacy.Path.Count == 0 && legacy.Pops == 5000 && complete.Path.Count > 0, "Captured fixed-budget failure did not reproduce");
var finder = new GridPathfinder(map); var watch = Stopwatch.StartNew();
var actual = finder.Find(start.X, start.Y, goal.X, goal.Y, domain: MovementDomain.Water); watch.Stop();
Require(expectFixed ? actual.Count > 0 : actual.Count == 0, "Production pathfinder did not match the selected before/after expectation");
var results = new Dictionary<string, object?>
{
    ["mode"] = expectFixed ? "fixed" : "before-fix",
    ["map"] = "mp01t4", ["start"] = start, ["goal"] = goal, ["connectedCells"] = reachable.Count,
    ["legacy5000"] = new { legacy.Pops, legacy.Stale, legacy.Expanded, pathCells = legacy.Path.Count },
    ["completeReference"] = new { complete.Pops, complete.Stale, complete.Expanded, pathCells = complete.Path.Count, cost = Cost(map, complete.Path, start, null) },
    ["actualPathCells"] = actual.Count, ["actualMilliseconds"] = watch.Elapsed.TotalMilliseconds
};
if (expectFixed)
{
    Validate(map, start, goal, actual, MovementDomain.Water);
    Require(actual.SequenceEqual(complete.Path), "Tie breaks or optimal route changed beyond removal of the search cutoff");
    for (var i = 0; i < 3; i++) Require(finder.Find(start.X, start.Y, goal.X, goal.Y, domain: MovementDomain.Water).SequenceEqual(actual), "Repeated query changed route");
    var reverse = finder.Find(goal.X, goal.Y, start.X, start.Y, domain: MovementDomain.Water);
    Validate(map, goal, start, reverse, MovementDomain.Water);
    var occupied = new int[map.Width * map.Height];
    foreach (var index in actual.Skip(actual.Count / 3).Take(12)) occupied[index] = 1;
    var penalized = finder.Find(start.X, start.Y, goal.X, goal.Y, occupied, MovementDomain.Water);
    var penalizedReference = Reference(map, start, goal, MovementDomain.Water, occupied, int.MaxValue);
    Validate(map, start, goal, penalized, MovementDomain.Water);
    Require(penalized.SequenceEqual(penalizedReference.Path), "Occupancy penalty route differs from complete reference");
    var disconnected = Enumerable.Range(0, map.Width * map.Height).Select(i => new Cell(i % map.Width, i / map.Width))
        .First(c => map.Navigable(c.X, c.Y, MovementDomain.Water) && !reachable.Contains(c));
    watch.Restart(); var absent = finder.Find(start.X, start.Y, disconnected.X, disconnected.Y, domain: MovementDomain.Water); watch.Stop();
    Require(absent.Count == 0, "Disconnected water query returned a path");
    var failedReference = Reference(map, start, disconnected, MovementDomain.Water, null, int.MaxValue);
    Require(failedReference.Expanded <= reachable.Count, "Unreachable reference expanded a cell twice");
    results["disconnected"] = new { goal = disconnected, milliseconds = watch.Elapsed.TotalMilliseconds, failedReference.Expanded, failedReference.Pops };
    var landStart = map.Spawns[0]; var landGoal = map.Spawns[1];
    var land = finder.Find(landStart.X, landStart.Y, landGoal.X, landGoal.Y);
    Validate(map, landStart, landGoal, land, MovementDomain.Land);
    Require(land.SequenceEqual(Reference(map, landStart, landGoal, MovementDomain.Land, null, int.MaxValue).Path), "Land route/tie-break regression");
    results["landPathCells"] = land.Count; results["occupancyPathCells"] = penalized.Count;
    results["reversePathCells"] = reverse.Count; results["repeatQueries"] = 3;
}
Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(expectFixed ? "PASS: long naval route, reverse, determinism, occupancy, disconnected and land checks" : "PASS: native failure reproduced by the production pathfinder and independent connectivity check");

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static HashSet<Cell> Flood(BattleMap map, Cell start, MovementDomain domain)
{
    var found = new HashSet<Cell> { start }; var queue = new Queue<Cell>(); queue.Enqueue(start);
    while (queue.TryDequeue(out var c)) foreach (var n in new[] { new Cell(c.X - 1, c.Y), new Cell(c.X + 1, c.Y), new Cell(c.X, c.Y - 1), new Cell(c.X, c.Y + 1) })
        if (!found.Contains(n) && map.CanTraverseMovement(c, n, domain)) { found.Add(n); queue.Enqueue(n); }
    return found;
}
static void Validate(BattleMap map, Cell start, Cell goal, List<int> path, MovementDomain domain)
{
    Require(path.Count > 0 && path[^1] == map.Index(goal.X, goal.Y), "Path does not reach requested destination");
    var previous = start;
    foreach (var index in path)
    {
        var next = new Cell(index % map.Width, index / map.Width);
        Require(map.CanTraverseMovement(previous, next, domain), "Path crossed an illegal terrain edge"); previous = next;
    }
}
static int Cost(BattleMap map, List<int> path, Cell start, int[]? occupied)
{
    var cost = 0; var previous = start;
    for (var i = 0; i < path.Count; i++)
    {
        var next = new Cell(path[i] % map.Width, path[i] / map.Width);
        cost += next.X != previous.X && next.Y != previous.Y ? 14 : 10;
        if (occupied is not null && i != path.Count - 1 && occupied[path[i]] > 0) cost += 45;
        previous = next;
    }
    return cost;
}
// Frozen pre-fix algorithm, with counters and a selectable pop limit for causal comparison.
static Search Reference(BattleMap map, Cell from, Cell to, MovementDomain domain, int[]? occupied, int popLimit)
{
    var neighbors = new (int X, int Y, int Cost)[] { (0,-1,10), (1,0,10), (0,1,10), (-1,0,10), (1,-1,14), (1,1,14), (-1,1,14), (-1,-1,14) };
    var costs = new int[map.Width * map.Height]; Array.Fill(costs, int.MaxValue);
    var parents = new int[costs.Length]; Array.Fill(parents, -1);
    var open = new PriorityQueue<int, (int Score, int Cell)>();
    var start = map.Index(from.X, from.Y); var goal = map.Index(to.X, to.Y);
    static int H(int x, int y, Cell t) { var dx = Math.Abs(x-t.X); var dy = Math.Abs(y-t.Y); return 10*Math.Max(dx,dy)+4*Math.Min(dx,dy); }
    costs[start] = 0; open.Enqueue(start, (H(from.X, from.Y, to), start));
    var result = new Search();
    while (result.Pops < popLimit && open.TryDequeue(out var current, out var priority))
    {
        result.Pops++; var x = current % map.Width; var y = current / map.Width;
        if (priority.Score != costs[current] + H(x,y,to)) { result.Stale++; continue; }
        result.Expanded++;
        if (current == goal)
        {
            for (var c = goal; c != start; c = parents[c]) result.Path.Add(c);
            result.Path.Reverse(); return result;
        }
        foreach (var n in neighbors)
        {
            var nx = x+n.X; var ny = y+n.Y;
            if (!map.Navigable(nx,ny,domain) || !map.CanTraverseMovement(new Cell(x,y),new Cell(nx,ny),domain)) continue;
            if (n.X != 0 && n.Y != 0 && (!map.Navigable(x+n.X,y,domain) || !map.Navigable(x,y+n.Y,domain))) continue;
            var ni = map.Index(nx,ny); var penalty = occupied is not null && ni != goal && occupied[ni] > 0 ? 45 : 0;
            var cost = costs[current]+n.Cost+penalty;
            if (cost >= costs[ni]) continue;
            costs[ni] = cost; parents[ni] = current; open.Enqueue(ni,(cost+H(nx,ny,to),ni));
        }
    }
    return result;
}
sealed class Search
{
    public int Pops, Stale, Expanded;
    public List<int> Path = [];
}
