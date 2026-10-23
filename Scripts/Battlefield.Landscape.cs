using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    // Native shoreline TMPs and low-bridge SHPs retain their original layers,
    // crop and anchors. Only genuine terrain height breaks need cliff geometry.
    float LandscapeLevelRise=>Controller.Battle?.Map.OriginalDefinition is not null?15:8;
    const int LandscapePickRadius=3;
    readonly Vector2[] landscapeQuad=new Vector2[4],landscapeTriangle=new Vector2[3];
    readonly Color[] landscapeColors=new Color[4];
    readonly float[] landscapeDepths=new float[4],landscapeHeights=new float[4];
    readonly List<LandscapeTile> landscapeTiles=[];
    readonly List<LandscapeFace> landscapeFaces=[];
    readonly List<LandscapeBridgeArt> landscapeBridgeArt=[];
    public int OriginalBridgePiecesDrawn {get;private set;}
    public int OriginalBridgeForegroundDraws {get;private set;}
    struct LandscapeBridgeArt
    {
        public required string Template;
        public int Frame;
        public Vector2 Point,EdgeA,EdgeB;
        public Rect2 Bounds;
        public float Level;
        public Color Light;
    }
    int landscapeForegroundBudget;
    public int LandscapeCliffFacesDrawn {get;private set;}
    public int LandscapeRampTilesDrawn {get;private set;}
    public int LandscapeGroundTilesDrawn {get;private set;}
    public int LandscapeForegroundDrawCommands {get;private set;}
    public int BridgeTilesDrawn {get;private set;}
    public int BridgeSpansDrawn {get;private set;}

    struct LandscapeTile
    {
        public int X,Y,Frame,FaceStart,FaceCount;
        public string Template;
        public Vector2 A,B,C,D;
        public float HA,HB,HC,HD,MaximumHeight;
        public Rect2 Bounds;
        public Color Light;
    }
    struct LandscapeFace
    {
        public Vector2 A,B,BottomA,BottomB,WorldA,WorldB;
        public float HeightA,HeightB;
        public Rect2 Bounds;
        public Color Color;
    }
    static Rect2 LandscapeBounds(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
    {
        var low=new Vector2(MathF.Min(MathF.Min(a.X,b.X),MathF.Min(c.X,d.X)),MathF.Min(MathF.Min(a.Y,b.Y),MathF.Min(c.Y,d.Y)));
        var high=new Vector2(MathF.Max(MathF.Max(a.X,b.X),MathF.Max(c.X,d.X)),MathF.Max(MathF.Max(a.Y,b.Y),MathF.Max(c.Y,d.Y)));
        return new Rect2(low,high-low);
    }
    float LandscapeHeight(Vector2 cell)=>(float)(Controller.Battle?.Map.SurfaceHeightAt(cell.X,cell.Y)??0)*LandscapeLevelRise;
    Vector2 LandscapeCorner(int x,int y,int fixedHeight)=>Project(new Vector2(x,y))-new Vector2(0,fixedHeight/(float)Rules.Scale*LandscapeLevelRise*Zoom);
    Vector2 LandscapePoint(Vector2 cell,float height)=>Project(cell)-new Vector2(0,height*LandscapeLevelRise*Zoom);
    Vector2[] GroundFootprint(int x,int y,int width,int height)
    {
        if(width==1 && height==1 && Controller.Battle is { } world)
        {
            var c=world.Map.SurfaceCornersAt(x,y);
            return [LandscapeCorner(x,y,c.NorthWest),LandscapeCorner(x+1,y,c.NorthEast),LandscapeCorner(x+1,y+1,c.SouthEast),LandscapeCorner(x,y+1,c.SouthWest)];
        }
        var up=new Vector2(0,LandscapeHeight(new Vector2(x+width*.5f,y+height*.5f))*Zoom);
        return [Project(new Vector2(x,y))-up,Project(new Vector2(x+width,y))-up,Project(new Vector2(x+width,y+height))-up,Project(new Vector2(x,y+height))-up];
    }
    void BeginLandscapeFrame()
    {
        landscapeTiles.Clear();landscapeFaces.Clear();landscapeBridgeArt.Clear();originalVisibleTiles.Clear();
        OriginalBridgePiecesDrawn=0;OriginalBridgeForegroundDraws=0;
        LandscapeCliffFacesDrawn=0;LandscapeRampTilesDrawn=0;LandscapeGroundTilesDrawn=0;
        LandscapeForegroundDrawCommands=0;BridgeTilesDrawn=0;BridgeSpansDrawn=0;
        landscapeForegroundBudget=768;BeginVehiclePoseFrame();
    }
    void QueueLandscapeTile(World world,int x,int y,string template,int frame,Color light,bool groundOnly=false)
    {
        var map=world.Map;var c=map.SurfaceCornersAt(x,y);
        // Original 2x2 shoreline templates keep wet/dry pixels on one canonical
        // surface. Water, flat banks and low-bridge cells share that map level;
        // never discard the shore art or force a synthetic lower water plane.
        var slope=map.SurfaceSlopeFixedAt(x,y);
        light*=new Color(Math.Clamp(1-.09f*slope.X/Rules.Scale-.06f*slope.Y/Rules.Scale,.82f,1.12f),Math.Clamp(1-.09f*slope.X/Rules.Scale-.06f*slope.Y/Rules.Scale,.82f,1.12f),Math.Clamp(1-.09f*slope.X/Rules.Scale-.06f*slope.Y/Rules.Scale,.82f,1.12f));
        var tile=new LandscapeTile{X=x,Y=y,Template=template,Frame=frame,Light=light,
            A=LandscapeCorner(x,y,c.NorthWest),B=LandscapeCorner(x+1,y,c.NorthEast),C=LandscapeCorner(x+1,y+1,c.SouthEast),D=LandscapeCorner(x,y+1,c.SouthWest),
            HA=c.NorthWest/(float)Rules.Scale,HB=c.NorthEast/(float)Rules.Scale,HC=c.SouthEast/(float)Rules.Scale,HD=c.SouthWest/(float)Rules.Scale,FaceStart=landscapeFaces.Count};
        tile.MaximumHeight=MathF.Max(MathF.Max(tile.HA,tile.HB),MathF.Max(tile.HC,tile.HD));
        tile.Bounds=LandscapeBounds(tile.A,tile.B,tile.C,tile.D);
        // Unknown shroud fringe needs only the top diamond beneath the alpha
        // ramp. Its vertical faces can extend beyond that mask into black space.
        if(!groundOnly && !map.HasBridge(x,y))
        {
            if(TryLandscapeFace(world,x,y,true,out var east)){east.Color=new Color("77765b")*light;landscapeFaces.Add(east);}
            if(TryLandscapeFace(world,x,y,false,out var south)){south.Color=new Color("565f4a")*light;landscapeFaces.Add(south);}
        }
        tile.FaceCount=landscapeFaces.Count-tile.FaceStart;landscapeTiles.Add(tile);
    }
    bool TryLandscapeFace(World world,int x,int y,bool east,out LandscapeFace face)
    {
        var map=world.Map;var own=map.SurfaceCornersAt(x,y);var other=map.SurfaceCornersAt(x+(east?1:0),y+(east?0:1));
        var a=east?own.NorthEast:own.SouthWest;var b=own.SouthEast;
        var belowA=east?other.NorthWest:other.NorthWest;var belowB=east?other.SouthWest:other.NorthEast;
        var dropA=a-belowA;var dropB=b-belowB;
        face=default;if(dropA<=0 && dropB<=0)return false;
        var wa=east?new Vector2(x+1,y):new Vector2(x,y+1);var wb=new Vector2(x+1,y+1);
        var from=0f;var to=1f;
        if(dropA<0)from=-dropA/(float)(dropB-dropA);
        if(dropB<0)to=dropA/(float)(dropA-dropB);
        var aa=wa.Lerp(wb,from);var bb=wa.Lerp(wb,to);
        var ah=Mathf.Lerp(a,b,from)/Rules.Scale;var bh=Mathf.Lerp(a,b,to)/Rules.Scale;
        var al=Mathf.Lerp(belowA,belowB,from)/Rules.Scale;var bl=Mathf.Lerp(belowA,belowB,to)/Rules.Scale;
        face=new LandscapeFace{WorldA=aa,WorldB=bb,HeightA=ah,HeightB=bh,A=LandscapePoint(aa,ah),B=LandscapePoint(bb,bh),BottomA=LandscapePoint(aa,al),BottomB=LandscapePoint(bb,bl)};
        face.Bounds=LandscapeBounds(face.A,face.B,face.BottomA,face.BottomB);return true;
    }
    void CopyLandscapeTile(LandscapeTile tile)
    {
        landscapeQuad[0]=tile.A;landscapeQuad[1]=tile.B;landscapeQuad[2]=tile.C;landscapeQuad[3]=tile.D;
        var depth=tile.X+tile.Y;landscapeDepths[0]=depth;landscapeDepths[1]=depth+1;landscapeDepths[2]=depth+2;landscapeDepths[3]=depth+1;
        landscapeHeights[0]=tile.HA;landscapeHeights[1]=tile.HB;landscapeHeights[2]=tile.HC;landscapeHeights[3]=tile.HD;
    }
    void DrawLandscapeGround(World world)
    {
        if(world.Map.OriginalDefinition is not null){DrawOriginalMapGround(world);return;}
        landscapeTiles.Sort(static(a,b)=>a.X+a.Y!=b.X+b.Y?(a.X+a.Y).CompareTo(b.X+b.Y):a.X.CompareTo(b.X));
        foreach(var tile in landscapeTiles)
        {
            for(var i=0;i<tile.FaceCount;i++){DrawLandscapeFace(landscapeFaces[tile.FaceStart+i],0,1);LandscapeCliffFacesDrawn++;}
            CopyLandscapeTile(tile);
            if(!Controller.Graphics.DrawTerrainSurface(this,tile.Template,tile.Frame,landscapeQuad,tile.Light))DrawColoredPolygon(landscapeQuad,new Color("939955")*tile.Light);
            LandscapeGroundTilesDrawn++;if(world.Map.IsRamp(tile.X,tile.Y))LandscapeRampTilesDrawn++;
        }
        foreach(var tile in landscapeTiles)
            if(world.Map.IsPlayable(tile.X,tile.Y))DrawOreTile(world,tile.X,tile.Y,ProjectGround(new Vector2(tile.X+.5f,tile.Y+.5f)),BattleMap.Noise(tile.X,tile.Y,world.Settings.Seed),Controller.ViewVisible(tile.X,tile.Y),tile.Light);
        foreach(var span in world.Map.BridgeSpans)DrawLandscapeBridge(world,span);
    }
    void DrawLandscapeFace(LandscapeFace face,float from,float to)
    {
        var a=face.A.Lerp(face.B,from);var b=face.A.Lerp(face.B,to);var ba=face.BottomA.Lerp(face.BottomB,from);var bb=face.BottomA.Lerp(face.BottomB,to);
        if(a.DistanceSquaredTo(ba)<.001f)
        {landscapeTriangle[0]=a;landscapeTriangle[1]=b;landscapeTriangle[2]=bb;DrawColoredPolygon(landscapeTriangle,face.Color);return;}
        if(b.DistanceSquaredTo(bb)<.001f)
        {landscapeTriangle[0]=a;landscapeTriangle[1]=b;landscapeTriangle[2]=ba;DrawColoredPolygon(landscapeTriangle,face.Color);return;}
        landscapeQuad[0]=a;landscapeQuad[1]=b;landscapeQuad[2]=bb;landscapeQuad[3]=ba;
        landscapeColors[0]=face.Color.Lightened(.08f);landscapeColors[1]=face.Color;landscapeColors[2]=face.Color.Darkened(.22f);landscapeColors[3]=face.Color.Darkened(.28f);
        DrawPolygon(landscapeQuad,landscapeColors);
    }
    static bool ClipLandscapeRange(ref float from,ref float to,float a,float b,float minimum)
    {
        if(a<minimum && b<minimum)return false;
        if(a<minimum)from=MathF.Max(from,(minimum-a)/(b-a));
        if(b<minimum)to=MathF.Min(to,(a-minimum)/(a-b));
        return to-from>.0001f;
    }
    void DrawLandscapeForegroundFace(LandscapeFace face,Rect2 actorBounds,float depth,float height)
    {
        if(landscapeForegroundBudget<=0 || !face.Bounds.Intersects(actorBounds))return;
        var from=0f;var to=1f;
        if(!ClipLandscapeRange(ref from,ref to,face.WorldA.X+face.WorldA.Y,face.WorldB.X+face.WorldB.Y,depth+.0001f) ||
           !ClipLandscapeRange(ref from,ref to,face.HeightA,face.HeightB,height+.001f))return;
        DrawLandscapeFace(face,from,to);landscapeForegroundBudget--;LandscapeForegroundDrawCommands++;
    }
    void DrawLandscapeForeground(World world,Entity entity,Vector2 point)
    {
        if(world.Map.OriginalDefinition is not null){DrawOriginalMapForeground(world,entity,point);return;}
        var cell=new Vector2(entity.X/(float)Rules.Scale,entity.Y/(float)Rules.Scale);
        var height=(float)world.Map.SurfaceHeightAt(cell.X,cell.Y);
        var depth=entity is Building building?building.CellLeft+building.CellTop+building.Definition.Width+building.Definition.Height:cell.X+cell.Y;
        var bounds=new Rect2(point-new Vector2(entity is Building?140:44,entity is Building?170:48)*Zoom,new Vector2(entity is Building?280:88,entity is Building?210:76)*Zoom);
        foreach(var tile in landscapeTiles)
        {
            if(tile.MaximumHeight>height+.001f && tile.X+tile.Y+2>depth && tile.Bounds.Intersects(bounds) && landscapeForegroundBudget>0)
            {
                var own=tile.X==entity.CellX && tile.Y==entity.CellY;
                var continuous=entity is Unit && Math.Abs(tile.X-entity.CellX)<=1 && Math.Abs(tile.Y-entity.CellY)<=1 && world.Map.CanTraverseTerrain(new Cell(entity.CellX,entity.CellY),new Cell(tile.X,tile.Y));
                if(!own && !continuous)
                {
                    CopyLandscapeTile(tile);
                    if(Controller.Graphics.DrawTerrainOccluder(this,tile.Template,tile.Frame,landscapeQuad,landscapeDepths,landscapeHeights,depth,height,tile.Light))
                    {landscapeForegroundBudget--;LandscapeForegroundDrawCommands++;}
                }
            }
            for(var i=0;i<tile.FaceCount;i++)DrawLandscapeForegroundFace(landscapeFaces[tile.FaceStart+i],bounds,depth,height);
        }
        foreach(var art in landscapeBridgeArt)DrawLandscapeBridgeForeground(art,bounds,depth,height);
    }
    void LandscapeRectangle(Vector2 origin,int width,int height,float level)
    {
        landscapeQuad[0]=LandscapePoint(origin,level);landscapeQuad[1]=LandscapePoint(origin+new Vector2(width,0),level);
        landscapeQuad[2]=LandscapePoint(origin+new Vector2(width,height),level);landscapeQuad[3]=LandscapePoint(origin+new Vector2(0,height),level);
    }
    void DrawLandscapeBridge(World world,BridgeSpan span)
    {
        var layout=Controller.Graphics.OriginalLowBridge(span.Axis);
        if(layout is null || layout.CrossLaneCount!=span.Width || layout.Middle.Length==0)return;
        var level=span.DeckHeightFixed/(float)Rules.Scale;var origin=new Vector2(span.Origin.X,span.Origin.Y);
        LandscapeRectangle(origin,span.SizeX,span.SizeY,level);
        if(!LandscapeBounds(landscapeQuad[0],landscapeQuad[1],landscapeQuad[2],landscapeQuad[3]).Grow(90*Zoom).Intersects(new Rect2(Vector2.Zero,Size)))return;
        var along=span.Axis==BridgeAxis.X?Vector2I.Right:Vector2I.Down;
        var cross=span.Axis==BridgeAxis.X?Vector2I.Down:Vector2I.Right;
        var drawn=false;
        // Original map OverlayData selects only middle cross-lane frame 1.
        // Each native 180x120 strip covers all three lanes, once per along cell.
        // Begin/end sprites occupy the bank cells outside the water span.
        for(var i=-1;i<=span.Length;i++)
        {
            var start=new Vector2I(span.Origin.X,span.Origin.Y)+along*i;
            var explored=true;var visible=true;
            for(var lane=0;lane<span.Width;lane++)
            {
                var cell=start+cross*lane;
                if(!world.Map.Inside(cell.X,cell.Y) || !Controller.ViewExplored(cell.X,cell.Y)){explored=false;break;}
                if(!Controller.ViewVisible(cell.X,cell.Y))visible=false;
            }
            if(!explored)continue;
            var piece=i<0?layout.Start:i==span.Length?layout.End:layout.Middle[(span.Id+i)%layout.Middle.Length];
            var anchor=start+cross*layout.DrawCrossLane;
            var point=LandscapePoint(new Vector2(anchor.X,anchor.Y),level);
            var bounds=Controller.Graphics.OriginalTerrainBounds(piece.Template,piece.Frame,point,Zoom);
            if(!bounds.Intersects(new Rect2(Vector2.Zero,Size)))continue;
            var light=visible?Colors.White:new Color(.32f,.32f,.32f);
            if(!Controller.Graphics.DrawOriginalTerrain(this,piece.Template,piece.Frame,point,Zoom,light))continue;
            OriginalBridgePiecesDrawn++;drawn=true;
            if(i>=0 && i<span.Length)BridgeTilesDrawn+=span.Width;
            var edge=start+cross*span.Width;var end=edge+along;
            landscapeBridgeArt.Add(new LandscapeBridgeArt{Template=piece.Template,Frame=piece.Frame,Point=point,Bounds=bounds,EdgeA=new Vector2(edge.X,edge.Y),EdgeB=new Vector2(end.X,end.Y),Level=level,Light=light});
        }
        if(drawn)BridgeSpansDrawn++;
    }
    void DrawLandscapeBridgeForeground(LandscapeBridgeArt art,Rect2 bounds,float depth,float height)
    {
        if(landscapeForegroundBudget<=0 || height>art.Level+.001f || !art.Bounds.Intersects(bounds))return;
        var from=art.EdgeA.X+art.EdgeA.Y;var to=art.EdgeB.X+art.EdgeB.Y;
        if(depth>=to)return;
        var right=art.EdgeB.X>art.EdgeA.X;
        var cut=depth<=from?(right?float.NegativeInfinity:float.PositiveInfinity):Mathf.Lerp(Project(art.EdgeA).X,Project(art.EdgeB).X,(depth-from)/(to-from));
        if(Controller.Graphics.DrawOriginalTerrainForeground(this,art.Template,art.Frame,art.Point,Zoom,art.Light,cut,right))
        {landscapeForegroundBudget--;LandscapeForegroundDrawCommands++;OriginalBridgeForegroundDraws++;}
    }
    static bool LandscapeTriangleHit(Vector2 point,Vector2 a,Vector2 b,Vector2 c,out Vector3 weights)
    {
        var ab=b-a;var ac=c-a;var ap=point-a;var det=ab.Cross(ac);
        if(MathF.Abs(det)<.00001f){weights=default;return false;}
        var u=ap.Cross(ac)/det;var v=ab.Cross(ap)/det;weights=new Vector3(1-u-v,u,v);
        return weights.X>=-.0001f && weights.Y>=-.0001f && weights.Z>=-.0001f;
    }
    Vector2 UnprojectLandscape(Vector2 screen)
    {
        var world=Controller.Battle;var flat=Unproject(screen);if(world is null || !float.IsFinite(screen.X) || !float.IsFinite(screen.Y))return flat;
        var estimate=flat;for(var i=0;i<6;i++)estimate=Unproject(screen+new Vector2(0,LandscapeHeight(estimate)*Zoom));
        var best=estimate;var bestDepth=float.NegativeInfinity;var pickBase=world.Map.OriginalDefinition is null?flat:estimate;var baseX=(int)MathF.Floor(pickBase.X);var baseY=(int)MathF.Floor(pickBase.Y);
        for(var y=baseY-LandscapePickRadius;y<=baseY+LandscapePickRadius;y++)for(var x=baseX-LandscapePickRadius;x<=baseX+LandscapePickRadius;x++)
        {
            if(!world.Map.Inside(x,y))continue;var c=world.Map.SurfaceCornersAt(x,y);
            var a=LandscapeCorner(x,y,c.NorthWest);var b=LandscapeCorner(x+1,y,c.NorthEast);var cc=LandscapeCorner(x+1,y+1,c.SouthEast);var d=LandscapeCorner(x,y+1,c.SouthWest);
            Vector2 cell;
            if(LandscapeTriangleHit(screen,a,b,cc,out var weights))cell=new Vector2(x+weights.Y+weights.Z,y+weights.Z);
            else if(LandscapeTriangleHit(screen,a,cc,d,out weights))cell=new Vector2(x+weights.Y,y+weights.Y+weights.Z);
            else cell=new Vector2(float.NaN,float.NaN);
            if(float.IsFinite(cell.X) && cell.X+cell.Y>bestDepth){best=cell;bestDepth=cell.X+cell.Y;}
            if(world.Map.HasBridge(x,y))continue;
            for(var side=0;side<2;side++)
            {
                if(!TryLandscapeFace(world,x,y,side==0,out var face))continue;
                float along;
                if(LandscapeTriangleHit(screen,face.A,face.B,face.BottomB,out weights))along=weights.Y+weights.Z;
                else if(LandscapeTriangleHit(screen,face.A,face.BottomB,face.BottomA,out weights))along=weights.Y;
                else continue;
                var hit=face.WorldA.Lerp(face.WorldB,along);var depth=hit.X+hit.Y;
                if(depth>bestDepth){best=new Vector2(Math.Clamp(hit.X,x+.0001f,x+1-.0001f),Math.Clamp(hit.Y,y+.0001f,y+1-.0001f));bestDepth=depth;}
            }
        }
        return best;
    }
}
