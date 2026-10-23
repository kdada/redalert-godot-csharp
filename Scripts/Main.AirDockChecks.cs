using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    // Read-only runtime assertions for the ordinary production/return operator.
    // This neither advances the simulation nor spawns units or writes captures.
    public Godot.Collections.Dictionary GetAirDockAudit()
    {
        var errors=new Godot.Collections.Array<string>();
        var records=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var checks=0;var parked=0;var unrelatedOcclusionChecks=0;
        if(Battle is not {} world)return new(){{"passed",false},{"error","No active battle"}};
        void Require(bool condition,string message){checks++;if(!condition)errors.Add(message);}
        // Independent source-data expectation: original GAAIRC/AMRADR pads
        // occupy the right two columns of their 3x2 foundation, at cell centers.
        (int X,int Y)[] expectedCells=[(1,0),(1,1),(2,0),(2,1)];
        foreach(var airport in world.Buildings.Where(b=>b.Alive && b.Complete && b.Definition.AirCapacity>0))
        {
            Require(airport.Definition.Width==3 && airport.Definition.Height==2,"Unexpected original airport foundation "+airport.Id);
            Require(world.ClassicAircraftDockPosition(airport,-1) is null,"Negative airport slot accepted");
            Require(world.ClassicAircraftDockPosition(airport,airport.Definition.AirCapacity) is null,"Over-capacity airport slot accepted");
            var pads=new Godot.Collections.Array<Vector2>();
            for(var slot=0;slot<Math.Min(expectedCells.Length,airport.Definition.AirCapacity);slot++)
            {
                var pad=world.ClassicAircraftDockPosition(airport,slot);
                var expected=expectedCells[slot];
                var x=(airport.CellLeft+expected.X)*Rules.Scale+Rules.Scale/2;
                var y=(airport.CellTop+expected.Y)*Rules.Scale+Rules.Scale/2;
                Require(pad is {} p && p.X==x && p.Y==y,"Original airport pad mismatch "+airport.Id+" slot "+slot);
                if(pad is {} point)pads.Add(new Vector2(point.X/(float)Rules.Scale,point.Y/(float)Rules.Scale));
            }
            foreach(var plane in world.Units.Where(u=>u.Alive && u.Definition.AirportBound && u.Owner==airport.Owner && u.AircraftDockId==airport.Id))
            {
                var slot=world.ClassicAircraftDockSlot(plane,airport);var pad=world.ClassicAircraftDockPosition(airport,slot);
                Require(pad is not null,"Assigned aircraft has no airport slot "+plane.Id);
                var record=new Godot.Collections.Dictionary{{"airport",airport.Id},{"plane",plane.Id},{"slot",slot},
                    {"fixedPosition",new Vector2(plane.X,plane.Y)},{"altitude",plane.Altitude},{"ammo",plane.Ammo},
                    {"originCountry",plane.OriginCountry.ToString()},{"foundationCountry",airport.FoundationCountry.ToString()},
                    {"pads",pads},{"order",plane.Order.ToString()},{"returning",plane.Returning}};
                records.Add(record);
                if(plane.Altitude!=0 || plane.Order!=UnitOrder.Idle || plane.Returning || pad is not {} home)continue;
                parked++;Require(plane.X==home.X && plane.Y==home.Y,"Idle parked aircraft is not centered on its original pad "+plane.Id);
                Require(Battlefield.EntityInFrontOfBuilding(airport,plane),"Own airport bib can cover parked aircraft "+plane.Id);
                foreach(var other in world.Buildings.Where(b=>b.Alive && b.Id!=airport.Id))
                {
                    var radius=World.UnitCollisionRadius(plane);
                    var behind=plane.X+radius<=other.CellLeft*Rules.Scale || plane.Y+radius<=other.CellTop*Rules.Scale;
                    var inFront=plane.X-radius>=(other.CellLeft+other.Definition.Width)*Rules.Scale || plane.Y-radius>=(other.CellTop+other.Definition.Height)*Rules.Scale;
                    if(!behind || inFront)continue;
                    unrelatedOcclusionChecks++;
                    Require(!Battlefield.EntityInFrontOfBuilding(other,plane),"Parked aircraft incorrectly forced in front of unrelated building "+other.Id);
                }
            }
        }
        foreach(var building in world.Buildings.Where(b=>b.Alive && b.Definition.AirCapacity==0))
            Require(world.ClassicAircraftDockPosition(building,0) is null,"Non-airport gained an implicit zero slot "+building.Id);
        return new(){{"passed",errors.Count==0 && parked>0},{"checks",checks},{"parkedAircraft",parked},
            {"unrelatedOcclusionChecks",unrelatedOcclusionChecks},{"planes",records},{"errors",errors},
            {"tick",world.Tick},{"directEntitySpawns",0},{"simulationSteps",0}};
    }
}
