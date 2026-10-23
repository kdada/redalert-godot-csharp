namespace RedAlert.Simulation;

/// <summary>
/// Original, bounded practice AI. Every decision is derived from the current public
/// world snapshot, tick, actor IDs and known starts; there is no persistent AI state
/// or random generator to omit from a replay. It is not the original game's AI.
/// </summary>
public static class PracticeCommander
{
    public const int DecisionTicks = 64;
    public const int CommandLimit = 6;
    public const int ArmyLimit = 80;
    const int SiteCheckLimit = 1200;
    const int AdvanceNodeLimit = 2048;

    static readonly UnitType[] Infantry = [UnitType.Gi, UnitType.Conscript, UnitType.Gi, UnitType.Conscript,
        UnitType.Dog, UnitType.FlakTrooper, UnitType.TeslaTrooper, UnitType.Rocketeer, UnitType.Sniper,
        UnitType.Desolator, UnitType.Tanya, UnitType.ChronoLegionnaire, UnitType.Yuri, UnitType.Terrorist];
    static readonly UnitType[] Vehicles = [UnitType.Grizzly, UnitType.Rhino, UnitType.Grizzly, UnitType.Rhino,
        UnitType.Ifv, UnitType.FlakTrack, UnitType.TeslaTank, UnitType.TankDestroyer, UnitType.PrismTank,
        UnitType.MirageTank, UnitType.Apocalypse, UnitType.TerrorDrone, UnitType.V3Launcher, UnitType.Kirov,
        UnitType.DemolitionTruck];
    static readonly UnitType[] Aircraft = [UnitType.Harrier, UnitType.BlackEagle];
    static readonly UnitType[] Ships = [UnitType.Destroyer, UnitType.AttackSubmarine, UnitType.Destroyer,
        UnitType.AttackSubmarine, UnitType.SeaScorpion, UnitType.AegisCruiser, UnitType.Dolphin,
        UnitType.GiantSquid, UnitType.AircraftCarrier, UnitType.Dreadnought];

    public static IEnumerable<GameCommand> Orders(World world, int player)
    {
        if ((uint)player >= (uint)world.Players.Length || world.Finished || world.Players[player].Defeated)
            return [];
        // All MCVs get an immediate first decision; later decisions are staggered.
        if (world.Tick != 0 && (world.Tick + player * 11) % DecisionTicks != 0) return [];
        return new Decision(world, player).Run();
    }

    sealed class Decision
    {
        readonly World world;
        readonly BattleMap map;
        readonly PlayerState player;
        readonly Unit[] units;
        readonly Building[] buildings;
        readonly Building[] anchors;
        readonly Entity[] visibleEnemies;
        readonly Cell[] enemyStarts;
        readonly List<Cell> knownOre = [];
        readonly List<Cell> knownWater = [];
        readonly HashSet<int> assigned = [];
        readonly List<GameCommand> commands = [];
        readonly Cell home;
        int credits;
        int sequence;
        int queuedCombat;
        int queuedTotal;
        int trained;
        readonly Dictionary<int, int> plannedAtProducer = [];

        public Decision(World world, int id)
        {
            this.world = world; map = world.Map; player = world.Players[id]; credits = player.Credits;
            sequence = unchecked(world.Tick * 100);
            units = world.Units.Where(u => u.Alive && u.Owner == id).OrderBy(u => u.Id).ToArray();
            buildings = world.Buildings.Where(b => b.Alive && b.Owner == id).OrderBy(b => b.Id).ToArray();
            var yard = buildings.FirstOrDefault(b => b.Type == BuildingType.ConstructionYard);
            var mcv = units.FirstOrDefault(u => u.Type == UnitType.Mcv);
            home = yard is not null ? new(yard.CellX, yard.CellY) : mcv is not null ? new(mcv.CellX, mcv.CellY)
                : id < map.Spawns.Length ? map.Spawns[id] : new(map.Width / 2, map.Height / 2);
            anchors = world.Buildings.Where(b => b.Alive && b.Complete && ValidOwner(b.Owner)
                && (b.Owner == id || world.Settings.BuildNearAlly && world.Allied(id, b.Owner)))
                .OrderBy(b => b.Owner == id ? 0 : 1).ThenBy(b => b.Id).Take(20).ToArray();
            visibleEnemies = world.Units.Where(u => ValidOwner(u.Owner) && !world.Allied(id, u.Owner)
                    && world.ClassicUnitVisibleTo(id, u) && !world.Allied(id, world.ClassicApparentOwner(id, u)))
                .Cast<Entity>().Concat(world.Buildings.Where(b => b.Alive && ValidOwner(b.Owner)
                    && !world.Allied(id, b.Owner) && world.Visible(id, b.CellX, b.CellY)))
                .OrderBy(e => Distance(home, new(e.CellX, e.CellY))).ThenBy(e => e.Id).Take(96).ToArray();
            enemyStarts = world.Players.Where(p => !p.Defeated && !world.Allied(id, p.Id) && p.Id < map.Spawns.Length)
                .Select(p => map.Spawns[p.Id]).OrderBy(p => Distance(home, p)).ThenBy(p => p.Y).ThenBy(p => p.X).ToArray();
            foreach (var building in buildings)
            {
                queuedTotal += building.PendingClones.Count;
                queuedCombat += building.PendingClones.Count(c => IsCombatType(c.Type));
                foreach (var type in building.Queue)
                {
                    var copies = Rules.Unit(type, building.FoundationCountry).Infantry ? InfantryCopies() : 1;
                    queuedTotal += copies;
                    if (IsCombatType(type)) queuedCombat += copies;
                }
            }
            for (var i = 0; i < map.Ore.Length; i++)
            {
                var x = i % map.Width; var y = i / map.Width;
                if (!world.Explored(id, x, y)) continue;
                if (map.Ore[i] > 0 && map.Walkable(x, y)) knownOre.Add(new(x, y));
                if (map.Navigable(x, y, MovementDomain.Water)) knownWater.Add(new(x, y));
            }
        }

        public IEnumerable<GameCommand> Run()
        {
            DeployMcv();
            Construct();
            Repair();
            Produce();
            RecoverIdleMiner();
            Ferry();
            Fight(MovementDomain.Water);
            Fight(MovementDomain.Air);
            Fight(MovementDomain.Land);
            return commands;
        }

        bool ValidOwner(int owner) => (uint)owner < (uint)world.Players.Length;
        int Count(BuildingType type) => buildings.Count(b => b.Type == type);
        bool Has(BuildingType type) => Count(type) > 0;
        int Count(UnitType type) => units.Count(u => u.Type == type) + buildings.Sum(b => b.Queue.Count(t => t == type));
        bool IsCombatType(UnitType type) { var d = Rules.Unit(type, player.Setup.Country); return !d.Miner && type != UnitType.Mcv && d.Passengers == 0; }
        int InfantryCopies() => 1 + Count(BuildingType.CloningVats) + (player.BuildingProduction == BuildingType.CloningVats ? 1 : 0);
        bool Available(Unit unit) => unit.TransportId == 0 && unit.ParasiteHostId == 0 && unit.PendingTransportId == 0 && !assigned.Contains(unit.Id);
        static long Distance(Cell a, Cell b) { var dx = (long)a.X - b.X; var dy = (long)a.Y - b.Y; return dx * dx + dy * dy; }
        bool Add(CommandType type, int[]? actors = null, int x = 0, int y = 0, int target = 0,
            UnitType unit = default, BuildingType building = default, int cost = 0)
        {
            if (commands.Count >= CommandLimit || cost > credits) return false;
            commands.Add(new() { Tick = world.Tick, Player = player.Id, Sequence = sequence++, Type = type,
                Actors = actors ?? [], X = x, Y = y, Target = target, Unit = unit, Building = building });
            credits -= cost;
            if (actors is not null) foreach (var id in actors) assigned.Add(id);
            return true;
        }

        void DeployMcv()
        {
            if (Has(BuildingType.ConstructionYard)) return;
            var mcv = units.FirstOrDefault(u => u.Type == UnitType.Mcv && u.TransportId == 0);
            if (mcv is null) return;
            var definition = Rules.Building(BuildingType.ConstructionYard, mcv.OriginCountry);
            var start = new Cell(mcv.CellX, mcv.CellY);
            Cell? best = null; long score = long.MaxValue;
            var queue = new Queue<Cell>(); var seen = new HashSet<Cell>(); queue.Enqueue(start); seen.Add(start);
            while (queue.Count > 0 && seen.Count <= 512)
            {
                var cell = queue.Dequeue(); var x = cell.X - (definition.Width - 1) / 2; var y = cell.Y - (definition.Height - 1) / 2;
                if (world.CanUseBuildingFootprint(definition, x, y) && FootprintExplored(definition, x, y)
                    && !world.Units.Any(other => other.Id != mcv.Id && BodyOverlaps(other, x, y, definition.Width, definition.Height)))
                {
                    var value = Distance(start, cell) * 16 + Distance(home, cell);
                    if (value < score) { score = value; best = cell; }
                    if (value == 0) break;
                }
                foreach (var next in Neighbors(cell))
                    if (Distance(start, next) <= 144 && !seen.Contains(next)
                        && map.CanTraverseMovement(cell, next, MovementDomain.Land)) { seen.Add(next); queue.Enqueue(next); }
            }
            if (best is { } site)
            {
                if (site == start) Add(CommandType.Deploy, [mcv.Id]);
                else if (mcv.Order != UnitOrder.Move || mcv.GoalX != site.X || mcv.GoalY != site.Y)
                    Add(CommandType.Move, [mcv.Id], site.X, site.Y);
            }
            else
            {
                // Starting troops can obstruct an otherwise valid foundation; move them, never mutate the MCV to test it.
                ClearBlockers(definition, start.X - (definition.Width - 1) / 2, start.Y - (definition.Height - 1) / 2, mcv.Id);
            }
        }

        bool BodyOverlaps(Unit unit, int x, int y, int width, int height)
        {
            if (!unit.Alive || unit.TransportId != 0 || unit.ParasiteHostId != 0 || unit.Definition.Domain == MovementDomain.Air) return false;
            var radius = World.UnitCollisionRadius(unit);
            return (long)unit.X + radius >= (long)x * Rules.Scale && (long)unit.X - radius <= ((long)x + width) * Rules.Scale
                && (long)unit.Y + radius >= (long)y * Rules.Scale && (long)unit.Y - radius <= ((long)y + height) * Rules.Scale;
        }

        void Construct()
        {
            if (player.BuildingProduction is { } active)
            {
                if (!world.CanProduceBuilding(player.Id, active))
                { Add(CommandType.CancelBuilding, cost: -Rules.Building(active, player.Setup.Country).Cost); return; }
                if (!player.BuildingReady) return;
                if (FindSite(active, true, false) is { } site)
                { Add(CommandType.Build, x: site.X, y: site.Y, building: active); return; }
                if (FindSite(active, false, true) is { } blocked)
                    ClearBlockers(Rules.Building(active, player.Setup.Country), blocked.X, blocked.Y);
                else if ((world.Tick / DecisionTicks + player.Id) % 8 == 7)
                    Add(CommandType.CancelBuilding, cost: -Rules.Building(active, player.Setup.Country).Cost);
                return;
            }
            var desired = DesiredBuilding();
            if (desired is not { } type || !world.CanQueueBuilding(player.Id, type)) return;
            var definition = Rules.Building(type, player.Setup.Country);
            if (credits < definition.Cost || FindSite(type, false, false) is null) return;
            Add(CommandType.QueueBuilding, building: type, cost: definition.Cost);
        }

        BuildingType? DesiredBuilding()
        {
            if (!buildings.Any(b => b.Type == BuildingType.ConstructionYard && b.Complete)) return null;
            BuildingType[] corePlan = Has(BuildingType.NuclearReactor)
                ? [BuildingType.Refinery, BuildingType.Barracks, BuildingType.WarFactory, BuildingType.Radar, BuildingType.ServiceDepot]
                : [BuildingType.PowerPlant, BuildingType.Refinery, BuildingType.Barracks, BuildingType.WarFactory, BuildingType.Radar, BuildingType.ServiceDepot];
            var next = NextAllowedStep(corePlan);
            if (next is null && !Has(BuildingType.NavalYard) && CanConsider(BuildingType.NavalYard) && knownWater.Count > 0
                && FindSite(BuildingType.NavalYard, false, false) is not null) next = BuildingType.NavalYard;
            if (next is null) next = NextAllowedStep([BuildingType.BattleLab]);
            var generators = buildings.Where(b => b.Complete && b.Definition.Power > 0)
                .Sum(b => (int)((long)b.Definition.Power * b.Health / b.MaxHealth));
            var futureLoad = next is { } planned ? Math.Max(0, -Rules.Building(planned, player.Setup.Country).Power) : 35;
            if (generators < player.UsedPower + futureLoad + 15)
                return CanConsider(BuildingType.NuclearReactor) ? BuildingType.NuclearReactor
                    : CanConsider(BuildingType.PowerPlant) ? BuildingType.PowerPlant : null;
            if (next is { } required) return CanConsider(required) ? required : MissingRequirement(required);
            if (Count(BuildingType.Refinery) < 2 && knownOre.Count >= 12 && credits >= 3800 && CanConsider(BuildingType.Refinery)) return BuildingType.Refinery;
            var threat = visibleEnemies.Any(e => Distance(home, new(e.CellX, e.CellY)) < 24 * 24);
            var aa = Rules.Side(player.Setup.Country) == Faction.Allied ? BuildingType.PatriotMissile : BuildingType.FlakCannon;
            if (Count(aa) < 1 && CanConsider(aa) && (world.Tick >= 1600 || visibleEnemies.Any(e => e is Unit { Definition.Domain: MovementDomain.Air })))
                return aa;
            BuildingType[] defenses = Rules.Side(player.Setup.Country) == Faction.Allied
                ? [BuildingType.GrandCannon, BuildingType.PrismTower, BuildingType.Pillbox]
                : [BuildingType.TeslaCoil, BuildingType.SentryGun];
            var defense = defenses.FirstOrDefault(t => CanConsider(t) && Count(t) < (threat ? 2 : 1));
            if ((threat || credits >= 2400) && defenses.Contains(defense)) return defense;
            if (credits >= 6000)
                foreach (var type in new[] { BuildingType.OrePurifier, BuildingType.CloningVats, BuildingType.PsychicSensor, BuildingType.SpySatellite })
                    if (!Has(type) && CanConsider(type)) return type;
            return null;
        }

        bool CanConsider(BuildingType type) => world.CanProduceBuilding(player.Id, type)
            && map.AllowsRuleObject(Rules.Building(type, player.Setup.Country).RulesId);
        BuildingType? NextAllowedStep(IEnumerable<BuildingType> plan)
        {
            foreach (var type in plan)
            {
                var definition = Rules.Building(type, player.Setup.Country);
                if (Has(type) || !definition.Buildable || definition.TechLevel < 0 || !Rules.Allowed(definition, player.Setup.Country)
                    || !map.AllowsRuleObject(definition.RulesId)) continue;
                if (CanConsider(type)) return type;
                if (MissingRequirement(type) is { } prerequisite) return prerequisite;
            }
            return null;
        }
        BuildingType? MissingRequirement(BuildingType type)
        {
            foreach (var requirement in Rules.BuildingRequirements(type, player.Setup.Country))
                if (!Has(requirement.Building) && CanConsider(requirement.Building)) return requirement.Building;
            return null;
        }

        bool FootprintExplored(BuildingDef definition, int x, int y)
        {
            for (var dy = 0; dy < definition.Height; dy++) for (var dx = 0; dx < definition.Width; dx++)
                if (!world.Explored(player.Id, x + dx, y + dy)) return false;
            return true;
        }

        Cell? FindSite(BuildingType type, bool ready, bool ignoreUnits)
        {
            var definition = Rules.Building(type, player.Setup.Country); Cell? best = null; long bestScore = long.MaxValue;
            var used = new HashSet<Cell>(); var siteChecks = 0;
            var ordered = definition.WaterBound && knownWater.Count > 0
                ? anchors.OrderBy(b => knownWater.Min(w => Distance(new(b.CellX, b.CellY), w))).ThenBy(b => b.Id)
                : anchors.OrderBy(b => Distance(home, new(b.CellX, b.CellY))).ThenBy(b => b.Id);
            foreach (var anchor in ordered)
            for (var radius = 0; radius <= 10; radius++)
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) != radius) continue;
                var cell = new Cell(anchor.CellX + dx, anchor.CellY + dy);
                if (!used.Add(cell)) continue;
                if (++siteChecks > SiteCheckLimit) return best;
                if (!world.CanUseBuildingFootprint(definition, cell.X, cell.Y) || !FootprintExplored(definition, cell.X, cell.Y)) continue;
                if (ready ? !world.CanPlaceBuilding(player.Id, type, cell.X, cell.Y)
                    : !ignoreUnits && world.BuildingAreaHasUnits(cell.X, cell.Y, definition.Width, definition.Height)) continue;
                var score = PlacementScore(type, definition, cell);
                if (score < bestScore) { bestScore = score; best = cell; }
            }
            return best;
        }

        long PlacementScore(BuildingType type, BuildingDef definition, Cell corner)
        {
            var center = new Cell(corner.X + definition.Width / 2, corner.Y + definition.Height / 2);
            var score = Distance(home, center) * 6;
            if (type == BuildingType.Refinery && knownOre.Count > 0) score += knownOre.Min(ore => Distance(center, ore)) * 22;
            if (definition.IsDefense && enemyStarts.Length > 0) score += Distance(center, enemyStarts[0]) * 2;
            foreach (var factory in buildings.Where(b => b.Definition.Produces is not null || b.Type == BuildingType.Refinery))
            {
                var exitX = factory.CellLeft + factory.Definition.Width + 1; var exitY = factory.CellTop + factory.Definition.Height / 2;
                if (exitX >= corner.X - 1 && exitX <= corner.X + definition.Width && exitY >= corner.Y - 1 && exitY <= corner.Y + definition.Height)
                    score += 12000;
            }
            return score;
        }

        void ClearBlockers(BuildingDef definition, int x, int y, int except = 0)
        {
            var blockers = units.Where(u => u.Id != except && Available(u) && !u.Definition.Miner
                && BodyOverlaps(u, x, y, definition.Width, definition.Height)).Take(12).ToArray();
            if (blockers.Length == 0) return;
            var desired = new Cell(x + definition.Width + 5, y + definition.Height + 4);
            var goal = world.NearestFree(desired.X, desired.Y, true);
            if (goal is { } target) Add(CommandType.Move, blockers.Select(u => u.Id).ToArray(), target.X, target.Y);
        }

        void Repair()
        {
            if (credits < 600) return;
            var damaged = buildings.Where(b => b.Complete && !b.Repairing && b.Health < b.MaxHealth * 2 / 3)
                .OrderBy(b => b.Type == BuildingType.ConstructionYard ? 0 : b.Type == BuildingType.Refinery ? 1 : 2)
                .ThenBy(b => b.Health * 1000L / b.MaxHealth).ThenBy(b => b.Id).FirstOrDefault();
            if (damaged is not null) Add(CommandType.Repair, [damaged.Id]);
        }

        int Reserve()
        {
            if (!Has(BuildingType.ConstructionYard) && Count(UnitType.Mcv) == 0) return Rules.Unit(UnitType.Mcv, player.Setup.Country).Cost;
            // No costly site search here: reserve the next economic/tech step independently of troop production.
            var next = NextAllowedStep([BuildingType.Refinery, BuildingType.Barracks, BuildingType.WarFactory, BuildingType.Radar, BuildingType.BattleLab]);
            return next is { } type ? Math.Max(800, Rules.Building(type, player.Setup.Country).Cost) : 800;
        }

        bool Train(Building producer, UnitType type, bool essential = false)
        {
            if (trained >= 2 || producer.Queue.Count + plannedAtProducer.GetValueOrDefault(producer.Id) >= 2 || !world.CanTrainUnit(player.Id, type, producer)) return false;
            var definition = Rules.Unit(type, producer.FoundationCountry);
            if (definition.AirportBound && !world.HasClassicAircraftSlot(producer)) return false;
            if (units.Length + queuedTotal >= ArmyLimit + 16) return false;
            var combat = IsCombatType(type);
            var copies = definition.Infantry ? InfantryCopies() : 1;
            if (combat && units.Count(u => IsCombatType(u.Type)) + queuedCombat + copies > ArmyLimit) return false;
            if (credits - definition.Cost < (essential ? 0 : Reserve())) return false;
            if (!Add(CommandType.Train, [producer.Id], unit: type, cost: definition.Cost)) return false;
            trained++; plannedAtProducer[producer.Id] = plannedAtProducer.GetValueOrDefault(producer.Id) + 1;
            queuedTotal += copies; if (combat) queuedCombat += copies;
            return true;
        }

        void Produce()
        {
            var producers = buildings.Where(b => b.Complete && b.Definition.Produces is not null)
                .OrderBy(b => b.Queue.Count).ThenBy(b => (b.Id + world.Tick / DecisionTicks) % 13).ThenBy(b => b.Id).ToArray();
            if (!Has(BuildingType.ConstructionYard) && Count(UnitType.Mcv) == 0)
                foreach (var producer in producers) if (Train(producer, UnitType.Mcv, true)) break;
            var desiredMiners = Math.Min(6, Math.Max(1, Count(BuildingType.Refinery) * 2));
            if (Has(BuildingType.Refinery) && knownOre.Count > 0 && units.Count(u => u.Definition.Miner)
                + buildings.Sum(b => b.Queue.Count(t => Rules.Unit(t, b.FoundationCountry).Miner)) < desiredMiners)
                foreach (var producer in producers) if (Train(producer, Rules.Miner(producer.FoundationCountry), true)) break;
            if (!player.Powered || player.PowerBlackoutUntil > world.Tick) return;
            foreach (var producer in producers)
            {
                if (trained >= 2 || commands.Count >= CommandLimit - 2 || producer.Queue.Count >= 2) break;
                UnitType[] choices = producer.Definition.Produces switch
                { ProductionCategory.Infantry => Infantry, ProductionCategory.Vehicle => Vehicles,
                    ProductionCategory.Aircraft => Aircraft, ProductionCategory.Naval => Ships, _ => [] };
                if (units.Count(u => u.Definition.Infantry && !u.Definition.Miner) >= 10
                    && units.Count(u => u.Definition.Passengers > 0) + buildings.Sum(b => b.Queue.Count(t => Rules.Unit(t, b.FoundationCountry).Passengers > 0)) < 2)
                {
                    var transport = producer.Definition.Produces == ProductionCategory.Naval ? UnitType.AmphibiousTransport : UnitType.Nighthawk;
                    if (Train(producer, transport)) continue;
                }
                var start = choices.Length == 0 ? 0 : (world.Tick / DecisionTicks + producer.Id + player.Id * 3) % choices.Length;
                for (var i = 0; i < choices.Length; i++)
                    if (Train(producer, choices[(start + i) % choices.Length])) break;
            }
        }

        void RecoverIdleMiner()
        {
            var miner = units.FirstOrDefault(u => u.Definition.Miner && Available(u) && u.Order == UnitOrder.Idle);
            if (miner is null || knownOre.Count == 0) return;
            var ore = knownOre.OrderBy(p => Distance(new(miner.CellX, miner.CellY), p)).First();
            Add(CommandType.Move, [miner.Id], ore.X, ore.Y);
        }

        Cell EnemyStart() => enemyStarts.Length == 0 ? home : enemyStarts[(world.Tick / 1600) % enemyStarts.Length];

        void Ferry()
        {
            var transports = units.Where(u => u.Definition.Passengers > 0 && Available(u)).ToArray();
            if (transports.Length == 0 || enemyStarts.Length == 0 || commands.Count >= CommandLimit - 1) return;
            var transport = transports[(world.Tick / DecisionTicks + player.Id) % transports.Length];
            var here = new Cell(transport.CellX, transport.CellY); var destination = EnemyStart();
            if (transport.Passengers.Count > 0)
            {
                var pending = units.Any(u => u.PendingTransportId == transport.Id);
                if (transport.Passengers.Count < 4 && pending) return;
                var nearDestination = Distance(here, destination) <= 18 * 18;
                if (nearDestination && Neighbors(here).Any(p => map.Walkable(p.X, p.Y)))
                { Add(CommandType.Unload, [transport.Id]); return; }
                var goal = Advance(transport, destination);
                if (goal is { } drop) Add(CommandType.Move, [transport.Id], drop.X, drop.Y);
                return;
            }
            if (Distance(here, home) > 20 * 20)
            {
                if (Advance(transport, home) is { } back) Add(CommandType.Move, [transport.Id], back.X, back.Y);
                return;
            }
            var waiting = units.Count(u => u.PendingTransportId == transport.Id);
            if (waiting > 0) { if (transport.Order != UnitOrder.Idle) Add(CommandType.Stop, [transport.Id]); return; }
            var passengers = units.Where(u => u.Definition.Infantry && u.Definition.Domain == MovementDomain.Land && u.Type != UnitType.Engineer && Available(u)
                && Distance(here, new(u.CellX, u.CellY)) <= 14 * 14).OrderBy(u => Distance(here, new(u.CellX, u.CellY))).ThenBy(u => u.Id)
                .Take(Math.Min(8, transport.Definition.Passengers)).Select(u => u.Id).ToArray();
            if (passengers.Length == 0) return;
            if (transport.Order != UnitOrder.Idle && !Add(CommandType.Stop, [transport.Id])) return;
            Add(CommandType.EnterTransport, passengers, target: transport.Id);
        }

        void Fight(MovementDomain domain)
        {
            if (commands.Count >= CommandLimit || enemyStarts.Length == 0) return;
            var army = units.Where(u => Available(u) && u.Definition.Domain == domain && !u.Definition.Miner
                && u.Type != UnitType.Mcv && u.Type != UnitType.Engineer && u.Definition.Passengers == 0
                && (!u.Definition.AirportBound || !u.Returning && u.Ammo > 0)).ToArray();
            if (army.Length == 0) return;
            var threat = visibleEnemies.FirstOrDefault(e => Distance(home, new(e.CellX, e.CellY)) <= 24 * 24
                && army.Any(u => world.CanTarget(u, e)));
            var target = threat ?? visibleEnemies.FirstOrDefault(e => army.Any(u => world.CanTarget(u, e)));
            var group = army.Where(u => target is null ? u.Order == UnitOrder.Idle || u.BlockedTicks >= 80 : world.CanTarget(u, target))
                .OrderBy(u => target is null ? u.Id : Distance(new(u.CellX, u.CellY), new(target.CellX, target.CellY)))
                .ThenBy(u => u.Id).Take(20).ToArray();
            if (group.Length == 0) return;
            if (target is not null)
            {
                var attackers = group.Where(u => world.CanTarget(u, target)).Select(u => u.Id).ToArray();
                if (attackers.Length > 0)
                {
                    // A long-range ship must approach on connected water, never receive an inland infantry waypoint.
                    if (domain == MovementDomain.Water && target is not Unit { Definition.Domain: MovementDomain.Water }
                        && world.NearestFree(target.CellX, target.CellY, domain: MovementDomain.Water) is null)
                    {
                        if (Advance(group[0], new(target.CellX, target.CellY)) is { } coast)
                            Add(CommandType.AttackMove, group.Select(u => u.Id).ToArray(), coast.X, coast.Y);
                    }
                    else Add(CommandType.Attack, attackers, target: target.Id);
                    return;
                }
            }
            var moving = group.Where(u => u.Order == UnitOrder.Idle || u.BlockedTicks >= 80).ToArray();
            if (moving.Length == 0) return;
            if (domain == MovementDomain.Land && world.Tick < 400)
            {
                // Early troops guard the deployment area; one scout learns the known enemy start after eight seconds.
                if (world.Tick < 160) return;
                moving = moving.OrderByDescending(u => u.Definition.Speed).ThenBy(u => u.Id).Take(1).ToArray();
            }
            else if (domain == MovementDomain.Land && army.Length < 8 && world.Tick < 1200) moving = moving.Take(1).ToArray();
            var destination = EnemyStart();
            if (Advance(moving[0], destination) is { } advance)
                Add(CommandType.AttackMove, moving.Select(u => u.Id).ToArray(), advance.X, advance.Y);
        }

        Cell? Advance(Unit unit, Cell destination)
        {
            if (unit.Definition.Domain == MovementDomain.Air)
                return world.NearestFree(destination.X, destination.Y, domain: MovementDomain.Air);
            var start = new Cell(unit.CellX, unit.CellY);
            if (!map.Navigable(start.X, start.Y, unit.Definition.Domain, unit.Definition.Amphibious)) return null;
            var seen = new HashSet<Cell>(); var queue = new Queue<Cell>(); seen.Add(start); queue.Enqueue(start);
            var best = start; var score = Distance(start, destination);
            while (queue.Count > 0 && seen.Count <= AdvanceNodeLimit)
            {
                var cell = queue.Dequeue(); var value = Distance(cell, destination);
                if (value < score) { score = value; best = cell; if (score == 0) break; }
                foreach (var next in Neighbors(cell))
                    if (!seen.Contains(next) && map.CanTraverseMovement(cell, next, unit.Definition.Domain, unit.Definition.Amphibious))
                    { seen.Add(next); queue.Enqueue(next); }
            }
            return best == start && score > 0 ? null : best;
        }

        static IEnumerable<Cell> Neighbors(Cell cell)
        {
            yield return new(cell.X - 1, cell.Y); yield return new(cell.X, cell.Y - 1);
            yield return new(cell.X + 1, cell.Y); yield return new(cell.X, cell.Y + 1);
        }
    }
}
