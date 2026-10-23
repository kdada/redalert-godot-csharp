using System.Diagnostics;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert.Tests;

// Pure simulation checks. Lead owns Program registration and build/run.
public static class ClassicMobilityChecks
{
    const int StepCap=200;
    static int steps;
    static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}

    static World Fixture(bool repacks=true)
    {
        var world=new World(MatchSettings.LegacyFixture(2) with{Crates=false,MCVRepacks=repacks});
        // Keep both starting bases alive, with their units stationary and passive.
        foreach(var unit in world.Units){unit.DisabledUntil=int.MaxValue;unit.Order=UnitOrder.Idle;unit.TargetId=0;}
        for(var y=30;y<=60;y++)for(var x=30;x<=60;x++)
        {
            var index=world.Map.Index(x,y);
            Require(world.Map.Buildings[index]==0,"mobility fixture would overwrite a starting base");
            world.Map.Ground[index]=Terrain.Grass;world.Map.Ore[index]=0;world.Map.Elevation[index]=0;
            world.Map.Bridges[index]=false;world.Map.BridgeIds[index]=0;world.Map.Ramps[index]=RampDirection.None;
        }
        world.Map.NavigationVersion++;
        foreach(var player in world.Players)Array.Fill(player.Visibility,(byte)3);
        return world;
    }
    static Unit Put(World world,int owner,UnitType type,int x,int y,Country? origin=null,bool passive=false)
    {
        var unit=world.SpawnUnit(owner,type,x,y,origin);
        Require(unit.CellX==x && unit.CellY==y,$"fixture could not place {type} at {x},{y}");
        unit.DisabledUntil=passive?int.MaxValue:0;
        // SpawnUnit is a fixture API; it does not reveal sight until a visibility update.
        foreach(var player in world.Players)Array.Fill(player.Visibility,(byte)3);
        return unit;
    }
    static GameCommand Command(World world,int player,CommandType type,int[] actors,int x=0,int y=0,int target=0)=>new()
    {Tick=world.Tick,Player=player,Sequence=world.Tick,Type=type,Actors=actors,X=x,Y=y,Target=target};
    static void Step(World world,params GameCommand[] commands)
    {
        Require(steps<StepCap,"classic mobility exceeded its hard 200-step budget");
        steps++;world.Step(commands);
        Require(!world.Finished,"mobility fixture ended the match unexpectedly");
    }
    static void Issue(World world,GameCommand command,bool accepted=true)
    {
        var beforeAccepted=world.AcceptedCommands;var beforeRejected=world.RejectedCommands;
        Step(world,command);
        Require(world.AcceptedCommands==beforeAccepted+(accepted?1:0) && world.RejectedCommands==beforeRejected+(accepted?0:1),
            $"{command.Type}: expected {(accepted?"accepted":"rejected")} command");
    }
    static void Until(World world,Func<bool> reached,int limit,string failure)
    {
        for(var tick=0;tick<limit && !reached();tick++)Step(world);
        Require(reached(),failure);
    }

    public static int Run(string[] args)
    {
        steps=0;var failed=0;var results=new List<object>();var watch=Stopwatch.StartNew();
        void Test(string name,Action check)
        {
            try{check();results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}
            catch(Exception error){failed++;results.Add(new{name,passed=false,error=error.Message});Console.Error.WriteLine("FAIL "+name+": "+error.Message);}
        }

        // Worst-case Step allowances: 49 + 42 + 3 + 33 + 2 = 129 across all five checks.
        Test("amphibious transport Move actually crosses from water onto land",()=>
        {
            var world=Fixture();
            for(var y=36;y<=44;y++)for(var x=35;x<=39;x++)world.Map.Ground[world.Map.Index(x,y)]=Terrain.Water;
            world.Map.NavigationVersion++;
            var transport=Put(world,0,UnitType.AmphibiousTransport,38,40);
            Require(transport.Definition.Domain==MovementDomain.Water && transport.Definition.Amphibious,"fixture is not a water-domain amphibious transport");
            Require(world.Map.Ground[world.Map.Index(transport.CellX,transport.CellY)]==Terrain.Water
                && world.Map.Walkable(43,40) && !world.Map.Navigable(43,40,MovementDomain.Water),"fixture does not separate water and land");
            var startX=transport.X;
            Issue(world,Command(world,0,CommandType.Move,[transport.Id],43,40));
            Require(transport.GoalX==43 && transport.GoalY==40,"water-domain admission redirected the land Move goal");
            Until(world,()=>transport.CellX==43 && transport.CellY==40 && transport.Order==UnitOrder.Idle,48,
                $"amphibious Move failed to land; last position {transport.CellX},{transport.CellY}");
            Require(transport.Alive && transport.X>startX && world.Map.Walkable(transport.CellX,transport.CellY),"transport did not physically reach dry land");
        });

        Test("Nighthawk EnterTransport and Unload restore a mobile ground infantry body",()=>
        {
            var world=Fixture();var passenger=Put(world,0,UnitType.Gi,39,40);var transport=Put(world,0,UnitType.Nighthawk,40,40);
            Issue(world,Command(world,0,CommandType.EnterTransport,[passenger.Id],target:transport.Id));
            Require(transport.Altitude>0 && passenger.TransportId==transport.Id && passenger.Altitude==transport.Altitude
                && transport.Passengers.SequenceEqual(new[]{passenger.Id}),"EnterTransport did not load an airborne passenger");
            Issue(world,Command(world,0,CommandType.Unload,[transport.Id]));
            Require(passenger.Alive && passenger.Definition.Domain==MovementDomain.Land && passenger.TransportId==0
                && passenger.PendingTransportId==0 && passenger.Altitude==0 && transport.Passengers.Count==0,
                "Unload left the Land infantry attached to the carrier or at aircraft altitude");
            Require(world.Map.Walkable(passenger.CellX,passenger.CellY) && world.CanOccupyUnitPosition(passenger,passenger.X,passenger.Y),"unloaded infantry has no legal ground body");
            var startX=passenger.X;var startY=passenger.Y;var destination=new Cell(passenger.CellX+3,passenger.CellY);
            Issue(world,Command(world,0,CommandType.Move,[passenger.Id],destination.X,destination.Y));
            Until(world,()=>passenger.CellX==destination.X && passenger.CellY==destination.Y,39,"unloaded infantry accepted Move but could not traverse the ground");
            Require((passenger.X!=startX || passenger.Y!=startY) && passenger.TransportId==0 && passenger.Altitude==0
                && world.Map.Walkable(passenger.CellX,passenger.CellY),"unloaded infantry did not remain a moving ground unit");
        });

        Test("shooting down a transport removes older passengers and releases their mind control in the same tick",()=>
        {
            var world=Fixture();var controller=Put(world,0,UnitType.Yuri,40,40,Country.Russia);
            var passenger=Put(world,0,UnitType.Gi,40,41);var victim=Put(world,1,UnitType.Grizzly,42,40,Country.America,passive:true);
            var transport=Put(world,0,UnitType.Nighthawk,41,40);transport.Reload=int.MaxValue;
            Require(controller.Id<transport.Id && passenger.Id<transport.Id,"cargo must precede the transport in entity order");
            Issue(world,Command(world,0,CommandType.Attack,[controller.Id],target:victim.Id));
            Require(victim.Owner==0 && victim.OriginalOwner==1 && victim.ControllerId==controller.Id && controller.ControlledTargetId==victim.Id,
                "attack command failed to establish the passenger controller's ownership relation");
            Issue(world,Command(world,0,CommandType.EnterTransport,[controller.Id,passenger.Id],target:transport.Id));
            Require(controller.TransportId==transport.Id && passenger.TransportId==transport.Id
                && transport.Passengers.Order().SequenceEqual(new[]{controller.Id,passenger.Id}.Order()),"older passengers were not loaded by command");
            var gunner=Put(world,1,UnitType.FlakTrooper,44,40);transport.Health=1;
            var deathTick=world.Tick;
            Issue(world,Command(world,1,CommandType.Attack,[gunner.Id],target:transport.Id));
            Require(world.Tick==deathTick+1 && world.Events.Any(hit=>hit.Tick==deathTick && hit.Source==gunner.Id && hit.Target==transport.Id && hit.Damage>0 && hit.Explosion),
                "FlakTrooper command did not kill the transport in the tested tick");
            Require(!transport.Alive && !controller.Alive && !passenger.Alive && transport.Passengers.Count==0
                && world.Find(transport.Id) is null && world.Find(controller.Id) is null && world.Find(passenger.Id) is null
                && !world.Units.Any(unit=>unit.Id==transport.Id || unit.Id==controller.Id || unit.Id==passenger.Id)
                && !world.Units.Any(unit=>unit.Alive && unit.TransportId==transport.Id),"transport cleanup left older passenger entities in the death tick");
            Require(victim.Alive && ReferenceEquals(world.Find(victim.Id),victim) && victim.Owner==1 && victim.ControllerId==0
                && victim.OriginalOwner==-1 && controller.ControlledTargetId==0,"passenger controller death did not restore its surviving victim in the same tick");
        });

        Test("Harrier Move flies straight across a wall rock and water to its commanded destination",()=>
        {
            var world=Fixture();var wall=world.SpawnBuilding(0,BuildingType.Wall,43,40);
            world.Map.Ground[world.Map.Index(44,40)]=Terrain.Rock;world.Map.Ground[world.Map.Index(45,40)]=Terrain.Water;
            world.Map.NavigationVersion++;
            Require(!world.Map.Walkable(43,40) && !world.Map.Walkable(44,40) && !world.Map.Walkable(45,40),"aircraft fixture lacks its surface blockers");
            var plane=Put(world,0,UnitType.Harrier,39,40);var startY=plane.Y;var overWall=false;var overRock=false;var overWater=false;
            Require(plane.Definition.Domain==MovementDomain.Air && plane.Ammo>0 && plane.AircraftDockId==0,"fixture is not a free-moving armed Harrier");
            void Observe()
            {
                Require(plane.Alive && plane.Y==startY,"Harrier deviated from its direct air route");
                if(plane.CellX is >=43 and <=45)Require(plane.Altitude>0,"Harrier crossed a surface obstacle without taking off");
                overWall|=plane.CellX==wall.CellLeft;overRock|=plane.CellX==44;overWater|=plane.CellX==45;
            }
            Issue(world,Command(world,0,CommandType.Move,[plane.Id],47,40));Observe();
            for(var tick=0;tick<32 && plane.Order!=UnitOrder.Idle;tick++){Step(world);Observe();}
            Require(plane.X==47*Rules.Scale+Rules.Scale/2 && plane.Y==40*Rules.Scale+Rules.Scale/2 && plane.Order==UnitOrder.Idle
                && overWall && overRock && overWater,"Harrier did not fly through all three ground blockers and reach the exact Move destination");
        });

        Test("MCVRepacks false rejects Deploy while the enabled control repacks its yard",()=>
        {
            foreach(var enabled in new[]{false,true})
            {
                var world=Fixture(enabled);var yard=world.Buildings.Single(building=>building.Owner==0 && building.Type==BuildingType.ConstructionYard);
                var health=yard.Health;var mcvs=world.Units.Count(unit=>unit.Owner==0 && unit.Type==UnitType.Mcv);
                Issue(world,Command(world,0,CommandType.Deploy,[yard.Id]),accepted:enabled);
                if(enabled)
                    Require(world.Find(yard.Id) is null && !world.Buildings.Contains(yard)
                        && world.Units.Count(unit=>unit.Owner==0 && unit.Type==UnitType.Mcv)==mcvs+1,"enabled repack did not replace its yard with an MCV");
                else
                    Require(ReferenceEquals(world.Find(yard.Id),yard) && yard.Health==health && yard.Complete && world.Buildings.Contains(yard)
                        && world.Map.Buildings[world.Map.Index(yard.CellLeft,yard.CellTop)]==yard.Id
                        && world.Units.Count(unit=>unit.Owner==0 && unit.Type==UnitType.Mcv)==mcvs,"disabled repack mutated its yard or spawned an MCV");
            }
        });

        var report=new{passed=failed==0,totalTests=results.Count,failed,worldSteps=steps,stepCap=StepCap,milliseconds=watch.Elapsed.TotalMilliseconds,tests=results};
        var reportArg=args.FirstOrDefault(arg=>arg.StartsWith("--report=",StringComparison.Ordinal));
        if(reportArg is not null)
        {
            var path=Path.GetFullPath(reportArg[9..]);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
        Console.WriteLine($"Classic mobility: {results.Count-failed}/{results.Count} passed; {steps}/{StepCap} capped world steps.");
        return failed==0?0:1;
    }
}
