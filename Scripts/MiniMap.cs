using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class MiniMap : Control
{
    public required Main Controller {get;init;}
    public required Battlefield View {get;init;}
    public bool DraggingViewport {get;private set;}
    public int DecorativePixels {get;private set;}
    public int OrePixels {get;private set;}
    ImageTexture? terrainTexture;
    World? terrainWorld,dragWorld;
    byte[] terrainPixels=[];
    int terrainWidth,terrainHeight;
    int terrainPlayer=int.MinValue,terrainTick=-1;
    bool terrainSpectating;
    double terrainCooldown;
    Vector2 grabOffset;
    static readonly Color Grass=new("8e9852"),Dirt=new("8c7b4e"),Rock=new("626759"),Water=new("345b7f"),Ore=new("e8bd42");

    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Stop;FocusMode=FocusModeEnum.None;TextureFilter=TextureFilterEnum.Nearest;ClipContents=true;
        Resized+=()=>{terrainWidth=0;terrainCooldown=0;QueueRedraw();};
        if(Controller.Battle is{} world)RefreshTerrain(world);
    }
    Rect2 MapBounds()
    {
        var area=new Rect2(new Vector2(2,2),new Vector2(Math.Max(1,Size.X-4),Math.Max(1,Size.Y-4)));
        if(Controller.Battle is not{} world)return area;
        // Radar keeps the battlefield compass but removes its 2:1 vertical
        // foreshortening. U and V are equal ground distances: fit them with one
        // scale, leaving bars instead of independently stretching both axes.
        var map=world.Map;var span=new Vector2(Math.Max(1,map.PlayableMaxU-map.PlayableMinU),Math.Max(1,map.PlayableMaxV-map.PlayableMinV));
        var size=span*Math.Min(area.Size.X/span.X,area.Size.Y/span.Y);
        return new Rect2(area.GetCenter()-size/2,size);
    }
    public Rect2 RadarBounds=>MapBounds();
    public Vector2 MapToRadar(Vector2 cell)
    {
        if(Controller.Battle is not{} world)return Vector2.Zero;
        var p=new MapPreviewProjection(world.Map).Project(cell.X,cell.Y);var bounds=MapBounds();
        return bounds.Position+new Vector2(p.X,p.Y)*bounds.Size;
    }
    public Vector2 RadarToMap(Vector2 point)
    {
        if(Controller.Battle is not{} world)return Vector2.Zero;
        var bounds=MapBounds();var p=(point-bounds.Position)/bounds.Size;
        var cell=new MapPreviewProjection(world.Map).Unproject(p.X,p.Y);return new(cell.X,cell.Y);
    }
    public Vector2[] ViewportPolygon()
    {
        if(Controller.Battle is null || View.Size.X<=0 || View.Size.Y<=0)return [];
        // Remove the battlefield foreshortening with the same radar transform
        // used for terrain, markers and input. The compass axes stay aligned,
        // so this is a taller rectangle, not a diamond or a copied screen box.
        return [MapToRadar(View.UnprojectViewPlane(Vector2.Zero)),MapToRadar(View.UnprojectViewPlane(new Vector2(View.Size.X,0))),
            MapToRadar(View.UnprojectViewPlane(View.Size)),MapToRadar(View.UnprojectViewPlane(new Vector2(0,View.Size.Y)))];
    }
    public Vector2[] PlayablePolygon()
    {
        if(Controller.Battle is not{} w)return [];
        Vector2 Corner(float u,float v)=>MapToRadar(new Vector2((u+v)/2,(v-u)/2));var m=w.Map;
        return [Corner(m.PlayableMinU,m.PlayableMinV),Corner(m.PlayableMaxU,m.PlayableMinV),
            Corner(m.PlayableMaxU,m.PlayableMaxV),Corner(m.PlayableMinU,m.PlayableMaxV)];
    }
    bool NavigationBlocked=>Controller.Battle is null || Controller.ChatTyping || Controller.MatchMenuOpen || !IsVisibleInTree();
    public override void _Process(double delta)
    {
        if(DraggingViewport && (NavigationBlocked || !ReferenceEquals(dragWorld,Controller.Battle) || !Input.IsMouseButtonPressed(MouseButton.Left)))EndDrag();
        terrainCooldown-=delta;
        if(Controller.Battle is{} world)
        {
            var changed=!ReferenceEquals(terrainWorld,world) || terrainPlayer!=Controller.LocalPlayer || terrainSpectating!=Controller.IsSpectating;
            if(changed || terrainTexture is null || terrainWidth==0 || terrainCooldown<=0 && terrainTick!=world.Tick)RefreshTerrain(world);
        }
        QueueRedraw();
    }
    void RefreshTerrain(World world)
    {
        // Bounded raster work and Image/texture access stay on the main thread.
        var bounds=MapBounds();var width=Math.Clamp((int)Math.Ceiling(bounds.Size.X),1,256);var height=Math.Clamp((int)Math.Ceiling(bounds.Size.Y),1,256);
        var resized=terrainWidth!=width || terrainHeight!=height;
        if(resized){terrainWidth=width;terrainHeight=height;terrainPixels=new byte[width*height*4];}
        var map=world.Map;var projection=new MapPreviewProjection(map);DecorativePixels=0;OrePixels=0;
        for(var py=0;py<height;py++)for(var px=0;px<width;px++)
        {
            var cell=projection.Unproject((px+.5f)/width,(py+.5f)/height);var x=(int)MathF.Floor(cell.X);var y=(int)MathF.Floor(cell.Y);
            var pixel=(py*width+px)*4;var playable=map.IsPlayable(x,y);
            var ground=MapPreviewProjection.BackingTerrain(map,x,y,world.Settings.Seed);
            var color=map.HasBridge(x,y)?new Color("b9a477"):ground switch{Terrain.Water=>Water,Terrain.Rock=>Rock,Terrain.Dirt=>Dirt,_=>Grass};
            var shade=(.91f+(BattleMap.Noise(x,y,world.Settings.Seed)&7)*.012f)*Math.Clamp(.80f+map.ElevationAt(x,y)*.06f,.8f,1);
            if(!playable){shade*=.55f;DecorativePixels++;}
            else if(!Controller.ViewExplored(x,y)){color=new Color("040806");shade=1;}
            else
            {
                if(map.Ore[map.Index(x,y)]>0){color=Ore;OrePixels++;}
                if(!Controller.ViewVisible(x,y))shade*=.32f;
            }
            terrainPixels[pixel]=(byte)(color.R*shade*255);terrainPixels[pixel+1]=(byte)(color.G*shade*255);terrainPixels[pixel+2]=(byte)(color.B*shade*255);terrainPixels[pixel+3]=255;
        }
        using var image=Image.CreateFromData(width,height,false,Image.Format.Rgba8,terrainPixels);
        if(terrainTexture is null || resized){terrainTexture?.Dispose();terrainTexture=ImageTexture.CreateFromImage(image);}else terrainTexture.Update(image);
        terrainWorld=world;terrainPlayer=Controller.LocalPlayer;terrainSpectating=Controller.IsSpectating;terrainTick=world.Tick;terrainCooldown=.2;
    }
    public byte[] CopyTerrainPixels()=>(byte[])terrainPixels.Clone();
    public void InvalidateTerrain(){terrainTick=-1;terrainCooldown=0;QueueRedraw();}
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("080e0d"));if(Controller.Battle is not{} world)return;
        var bounds=MapBounds();
        if(terrainTexture is not null && ReferenceEquals(terrainWorld,world) && terrainPlayer==Controller.LocalPlayer && terrainSpectating==Controller.IsSpectating)DrawTextureRect(terrainTexture,bounds,false);
        var edge=PlayablePolygon();if(edge.Length==4)DrawPolyline([..edge,edge[0]],new Color(.72f,.69f,.45f,.65f),1,true);
        foreach(var building in world.Buildings)
        {
            if(!building.Alive || !world.Map.IsPlayable(building.CellX,building.CellY) || building.Owner!=Controller.LocalPlayer && !Controller.ViewVisible(building.CellX,building.CellY))continue;
            var center=MapToRadar(new Vector2(building.X/(float)Rules.Scale,building.Y/(float)Rules.Scale));
            DrawRect(new Rect2(center-new Vector2(1.5f,1.5f),new Vector2(3,3)),Battlefield.PlayerColors[building.Owner]);
        }
        foreach(var unit in world.Units)
        {
            if(!unit.Alive || unit.TransportId!=0 || unit.ParasiteHostId!=0 || !world.Map.IsPlayable(unit.CellX,unit.CellY) || !Controller.IsSpectating && !world.ClassicUnitVisibleTo(Math.Max(0,Controller.LocalPlayer),unit))continue;
            if(unit.Type==UnitType.Nighthawk && unit.Owner!=Controller.LocalPlayer && !Controller.IsSpectating)continue;
            var owner=Controller.IsSpectating?unit.Owner:world.ClassicApparentOwner(Math.Max(0,Controller.LocalPlayer),unit);
            var center=MapToRadar(new Vector2(unit.X/(float)Rules.Scale,unit.Y/(float)Rules.Scale));DrawRect(new Rect2(center-Vector2.One,Vector2.One*2),Battlefield.PlayerColors[owner]);
        }
        var footprint=ClipToBounds(ViewportPolygon(),bounds);
        if(footprint.Length>=3){DrawColoredPolygon(footprint,new Color(1,1,1,.045f));DrawPolyline([..footprint,footprint[0]],Colors.White,1.25f,true);}
        DrawRect(bounds,new Color("64705b"),false,1);
    }
    static Vector2[] ClipToBounds(Vector2[] polygon,Rect2 bounds)
    {
        var points=polygon.ToList();
        for(var edge=0;edge<4 && points.Count>0;edge++)
        {
            var output=new List<Vector2>();var previous=points[^1];
            bool Inside(Vector2 p)=>edge switch{0=>p.X>=bounds.Position.X,1=>p.X<=bounds.End.X,2=>p.Y>=bounds.Position.Y,_=>p.Y<=bounds.End.Y};
            Vector2 Crossing(Vector2 a,Vector2 b)
            {var vertical=edge<2;var boundary=edge switch{0=>bounds.Position.X,1=>bounds.End.X,2=>bounds.Position.Y,_=>bounds.End.Y};var t=vertical?(boundary-a.X)/(b.X-a.X):(boundary-a.Y)/(b.Y-a.Y);return a.Lerp(b,t);}
            foreach(var current in points){var currentInside=Inside(current);var previousInside=Inside(previous);if(currentInside!=previousInside)output.Add(Crossing(previous,current));if(currentInside)output.Add(current);previous=current;}
            points=output;
        }
        return points.ToArray();
    }
    void EndDrag(){DraggingViewport=false;dragWorld=null;grabOffset=Vector2.Zero;}
    void DragTo(Vector2 point){View.SetCameraCell(RadarToMap(point)+grabOffset);View.QueueRedraw();QueueRedraw();}
    public override void _GuiInput(InputEvent input)
    {
        if(input is not InputEventMouseButton{ButtonIndex:MouseButton.Left} button)return;
        if(!button.Pressed){if(DraggingViewport){EndDrag();AcceptEvent();}return;}
        if(NavigationBlocked){EndDrag();AcceptEvent();return;}
        if(!MapBounds().HasPoint(button.Position)){AcceptEvent();return;}
        var cell=RadarToMap(button.Position);var polygon=ViewportPolygon();
        grabOffset=polygon.Length>=3 && Geometry2D.IsPointInPolygon(button.Position,polygon)?View.CameraCell-cell:Vector2.Zero;
        DraggingViewport=true;dragWorld=Controller.Battle;DragTo(button.Position);AcceptEvent();
    }
    public override void _Input(InputEvent input)
    {
        if(!DraggingViewport)return;
        if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false}){EndDrag();GetViewport().SetInputAsHandled();return;}
        if(NavigationBlocked || !ReferenceEquals(dragWorld,Controller.Battle)){EndDrag();return;}
        if(input is not InputEventMouseMotion motion)return;
        if((motion.ButtonMask&MouseButtonMask.Left)==0){EndDrag();return;}
        DragTo(GetGlobalTransformWithCanvas().AffineInverse()*Controller.BattlePointerPosition());GetViewport().SetInputAsHandled();
    }
    public override void _ExitTree()
    {EndDrag();terrainTexture?.Dispose();terrainTexture=null;terrainWorld=null;terrainPixels=[];terrainWidth=0;terrainHeight=0;terrainTick=-1;}
}
