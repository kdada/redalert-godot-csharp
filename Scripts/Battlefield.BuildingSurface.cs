using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    readonly List<(int Depth,int Order,Entity? Entity,int X,int Y)> buildingOrderItems=[];
    readonly List<(int Target,int Next)> buildingDepthEdges=[];
    readonly PriorityQueue<int,int> buildingDepthReady=new();
    int[] buildingDepthHeads=[],buildingDepthIncoming=[];
    bool[] buildingDepthEmitted=[];
    public int BuildingDepthConstraintCount {get;private set;}
    public int BuildingDepthFallbacks {get;private set;}

    // The original SHP layout offsets are relative to the center of the original
    // foundation. Roof pixels and shadow/bib extents are not collision geometry.
    public Vector2 BuildingGroundAnchor(Building building)=>ProjectGround(new Vector2(
        building.CellLeft+building.Definition.Width*.5f,building.CellTop+building.Definition.Height*.5f));

    static (long Left,long Top,long Right,long Bottom) GroundBody(Entity entity)
    {
        if(entity is Building building)return ((long)building.CellLeft*Rules.Scale,(long)building.CellTop*Rules.Scale,
            (long)(building.CellLeft+building.Definition.Width)*Rules.Scale,(long)(building.CellTop+building.Definition.Height)*Rules.Scale);
        var radius=entity is Unit unit?World.UnitCollisionRadius(unit):0;
        return ((long)entity.X-radius,(long)entity.Y-radius,(long)entity.X+radius,(long)entity.Y+radius);
    }

    static int BuildingRelation(Building building,Entity entity)
    {
        if(entity is Unit{Altitude:>0})return 1;
        // Docked planes sit on this airport's bib. Give that one relationship
        // an explicit order while keeping Altitude=0 and every other building's
        // ordinary footprint occlusion (including towers in front of the pad).
        if(entity is Unit{Altitude:0,Definition.AirportBound:true} aircraft && aircraft.Owner==building.Owner
            && aircraft.AircraftDockId==building.Id && building.Definition.AirCapacity>0
            && aircraft.X>=building.CellLeft*Rules.Scale && aircraft.X<(building.CellLeft+building.Definition.Width)*Rules.Scale
            && aircraft.Y>=building.CellTop*Rules.Scale && aircraft.Y<(building.CellTop+building.Definition.Height)*Rules.Scale)return 1;
        return SurfaceRelation(GroundBody(building),GroundBody(entity));
    }
    static int SurfaceRelation((long Left,long Top,long Right,long Bottom) a,(long Left,long Top,long Right,long Bottom) b)
    {
        var front=b.Left>=a.Right || b.Top>=a.Bottom;
        var behind=b.Right<=a.Left || b.Bottom<=a.Top;
        // Opposite diagonal quadrants have no shared ground occlusion order.
        return front==behind?0:front?1:-1;
    }

    public static bool EntityInFrontOfBuilding(Building building,Entity entity)=>BuildingRelation(building,entity)>0;

    public Rect2 BuildingSurfaceBounds(Building building)
    {
        var corners=Footprint(building.CellLeft,building.CellTop,building.Definition.Width,building.Definition.Height);
        var left=corners.Min(p=>p.X)-12*Zoom;var right=corners.Max(p=>p.X)+12*Zoom;
        var top=corners.Min(p=>p.Y)-240*Zoom;var bottom=corners.Max(p=>p.Y)+12*Zoom;
        return new Rect2(left,top,right-left,bottom-top);
    }

    Rect2 EntitySurfaceBounds(Entity entity)=>entity is Building building?BuildingSurfaceBounds(building):
        new Rect2(UnitActorPoint((Unit)entity)-Vector2.One*150,Vector2.One*300);

    bool EntitySurfaceOnscreen(Entity entity)=>EntitySurfaceBounds(entity).Intersects(new Rect2(Vector2.Zero,Size));

    void AddBuildingDepthEdge(int from,int to)
    {
        buildingDepthEdges.Add((to,buildingDepthHeads[from]));buildingDepthHeads[from]=buildingDepthEdges.Count-1;buildingDepthIncoming[to]++;
    }

    void SortBuildingSurfaces()
    {
        depthItems.Sort((a,b)=>a.Depth!=b.Depth?a.Depth.CompareTo(b.Depth):a.Order.CompareTo(b.Order));
        BuildingDepthConstraintCount=0;BuildingDepthFallbacks=0;
        var count=depthItems.Count;if(count<2)return;
        if(buildingDepthHeads.Length<count)
        {
            var capacity=Math.Max(count,buildingDepthHeads.Length*2);
            buildingDepthHeads=new int[capacity];buildingDepthIncoming=new int[capacity];buildingDepthEmitted=new bool[capacity];
        }
        Array.Fill(buildingDepthHeads,-1,0,count);Array.Clear(buildingDepthIncoming,0,count);Array.Clear(buildingDepthEmitted,0,count);
        buildingDepthEdges.Clear();buildingDepthReady.Clear();buildingOrderItems.Clear();buildingOrderItems.AddRange(depthItems);
        // Neutral original structures share the same foundation ordering as owned
        // buildings. A single far-corner depth cannot order both side edges.
        bool IsSurface(int index)=>buildingOrderItems[index].Entity is Building || buildingOrderItems[index].Y==OriginalObjectDepthSentinel && visibleOriginalObjects[buildingOrderItems[index].X].Object.Definition.Kind==ClassicMapObjectKind.Structure;
        Rect2 Bounds(int index)=>buildingOrderItems[index].Entity is { } entity?EntitySurfaceBounds(entity):visibleOriginalObjects[buildingOrderItems[index].X].Bounds;
        (long Left,long Top,long Right,long Bottom) Body(int index)
        {
            if(buildingOrderItems[index].Entity is { } entity)return GroundBody(entity);
            var item=visibleOriginalObjects[buildingOrderItems[index].X].Object;
            return ((long)item.X*Rules.Scale,(long)item.Y*Rules.Scale,(long)(item.X+item.Width)*Rules.Scale,(long)(item.Y+item.Height)*Rules.Scale);
        }
        for(var i=0;i<count;i++)
        {
            if(!IsSurface(i))continue;
            var bounds=Bounds(i);
            for(var j=0;j<count;j++)
            {
                if(i==j || buildingOrderItems[j].Entity is null && !IsSurface(j) || IsSurface(j) && j<i)continue;
                if(!bounds.Intersects(Bounds(j)))continue;
                var relation=buildingOrderItems[i].Entity is Building building && buildingOrderItems[j].Entity is {} actor
                    ?BuildingRelation(building,actor)
                    :buildingOrderItems[j].Entity is Unit{Altitude:>0}?1:SurfaceRelation(Body(i),Body(j));
                if(relation>0)AddBuildingDepthEdge(i,j);
                else if(relation<0)AddBuildingDepthEdge(j,i);
            }
        }
        BuildingDepthConstraintCount=buildingDepthEdges.Count;
        if(BuildingDepthConstraintCount==0)return;
        for(var i=0;i<count;i++)if(buildingDepthIncoming[i]==0)buildingDepthReady.Enqueue(i,i);
        depthItems.Clear();var fallback=0;
        while(depthItems.Count<count)
        {
            if(buildingDepthReady.Count==0)
            {
                while(buildingDepthEmitted[fallback])fallback++;
                buildingDepthReady.Enqueue(fallback,fallback);BuildingDepthFallbacks++;
            }
            var next=buildingDepthReady.Dequeue();if(buildingDepthEmitted[next])continue;
            buildingDepthEmitted[next]=true;depthItems.Add(buildingOrderItems[next]);
            for(var edge=buildingDepthHeads[next];edge>=0;edge=buildingDepthEdges[edge].Next)
            {
                var target=buildingDepthEdges[edge].Target;
                if(--buildingDepthIncoming[target]==0 && !buildingDepthEmitted[target])buildingDepthReady.Enqueue(target,target);
            }
        }
    }
}
