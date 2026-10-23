using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield : Control
{
    public required Main Controller {get;init;}
    public HashSet<int> Selection {get;}=[];
    public int DrawCalls {get;private set;}
    public Vector2 CameraCell=new(15,15);
    // Original battlefield art and sidebar must share the same pixel scale.
    // The 900px logical canvas therefore shows 600 original-art vertical pixels.
    public const float ClassicZoom=1.5f;
    public float Zoom=>ClassicZoom;
    public float SpectatorZoomMultiplier=>1;
    public float VisibleArtHeight=>Size.Y/Zoom;
    public BuildingType? Placement;
    public bool SupportMode;
    public bool AttackMode;
    readonly Dictionary<int,int[]> groups=[];
    readonly List<(int Depth,int Order,Entity? Entity,int X,int Y)> depthItems=[];
    static readonly (int X,int Y,int A,int B)[] FogEdges=[(0,-1,0,1),(1,0,1,2),(0,1,2,3),(-1,0,3,0)];
    Vector2 pressPosition;
    Vector2 pointer;
    bool dragging;
    bool panning;
    // Original rules.ini [Colors] multiplayer HSV values, normalized from 0..255.
    public static readonly Color[] PlayerColors=[Color.FromHsv(41/255f,240/255f,230/255f),Color.FromHsv(0,230/255f,1),Color.FromHsv(153/255f,214/255f,212/255f),Color.FromHsv(81/255f,200/255f,210/255f),Color.FromHsv(25/255f,230/255f,1),Color.FromHsv(131/255f,200/255f,230/255f),Color.FromHsv(201/255f,201/255f,189/255f),Color.FromHsv(221/255f,102/255f,1)];
    public override void _Ready(){MouseFilter=MouseFilterEnum.Stop;FocusMode=FocusModeEnum.All;Resized+=()=>SetCameraCell(CameraCell);}
    public Vector2 Project(Vector2 cell)
    {
        var delta=cell-CameraCell;
        return Size/2+new Vector2((delta.X-delta.Y)*30,(delta.X+delta.Y)*15)*Zoom;
    }
    public Vector2 Unproject(Vector2 screen)
    {
        var d=(screen-Size/2)/Zoom;
        return CameraCell+new Vector2(d.X/60+d.Y/30,d.Y/30-d.X/60);
    }
    public Vector2 ProjectViewPlane(Vector2 cell)=>Project(cell);
    public Vector2 UnprojectViewPlane(Vector2 screen)=>Unproject(screen);
    public Vector2 ProjectGround(Vector2 cell)=>Project(cell)-new Vector2(0,LandscapeHeight(cell)*Zoom);
    public Vector2 UnprojectGround(Vector2 screen)=>UnprojectLandscape(screen);
    public void PanScreen(Vector2 pixels)
    {
        var w=Controller.Battle;if(w is null)return;
        var d=pixels/Zoom;SetCameraCell(CameraCell+new Vector2(d.X/60+d.Y/30,d.Y/30-d.X/60));
    }
    public Rect2 CameraUvRect
    {
        get
        {
            var half=new Vector2(Size.X/(60*Zoom),Size.Y/(30*Zoom));
            return new Rect2(new Vector2(CameraCell.X-CameraCell.Y,CameraCell.X+CameraCell.Y)-half,half*2);
        }
    }
    Vector2 ClampCameraCell(Vector2 cell)
    {
        var map=Controller.Battle!.Map;
        var halfU=Size.X/(60*Zoom);var halfV=Size.Y/(30*Zoom);
        const float inset=.001f;
        var minU=map.PlayableMinU+halfU+inset;var maxU=map.PlayableMaxU-halfU-inset;
        var minV=map.PlayableMinV+halfV+inset;var maxV=map.PlayableMaxV-halfV-inset;
        // U=x-y and V=x+y use continuous tile coordinates. Screen X/Y align
        // directly with U/V, so these halfspans bound the entire viewport.
        var u=minU<=maxU?Math.Clamp(cell.X-cell.Y,minU,maxU):(map.PlayableMinU+map.PlayableMaxU)*.5f;
        var v=minV<=maxV?Math.Clamp(cell.X+cell.Y,minV,maxV):(map.PlayableMinV+map.PlayableMaxV)*.5f;
        return new Vector2((u+v)*.5f,(v-u)*.5f);
    }
    public void SetCameraCell(Vector2 cell)
    {
        if(Controller.Battle is null || !float.IsFinite(cell.X) || !float.IsFinite(cell.Y))return;
        CameraCell=ClampCameraCell(cell);QueueRedraw();
    }
    // Compatibility entry point: zoom is fixed for players and spectators alike.
    public void ZoomSpectator(float factor,Vector2 anchor) { }
    public void CaptureEvents()=>CaptureWeaponEffects();
    public override void _Draw()
    {
        DrawCalls++;
        pointer=GetGlobalTransformWithCanvas().AffineInverse()*Controller.BattlePointerPosition();
        var w=Controller.Battle;DrawRect(new Rect2(Vector2.Zero,Size),Colors.Black);if(w is null)return;
        CameraCell=ClampCameraCell(CameraCell);BeginMiningFrame(w);BeginLandscapeFrame();BeginShroudFrame();
        var viewer=Math.Max(0,Controller.LocalPlayer);
        var terrain=w.Map.OriginalDefinition is null?Controller.Graphics.GroundPlan(w.Map,w.Settings.Seed):null;depthItems.Clear();
        for(var y=0;y<w.Map.Height;y++)for(var x=0;x<w.Map.Width;x++)
        {
            if(w.Map.OriginalDefinition is not null && w.Map.OriginalCellAt(x,y) is null)continue;
            var pos=ProjectGround(new Vector2(x+.5f,y+.5f));if(pos.X < -100 || pos.Y < -250 || pos.X>Size.X+100 || pos.Y>Size.Y+250)continue;
            // Array padding is decorative scenery, not explored/targetable cells.
            var playable=w.Map.IsPlayable(x,y);var known=Controller.ViewExplored(x,y);
            var fringe=playable && !known && ShroudGroundFringe(x,y);if(playable && !known && !fringe)continue;
            var visible=playable && Controller.ViewVisible(x,y);
            if(playable && known && !visible)knownTerrainDarkened++;
            var light=visible || fringe?Colors.White:playable?new Color(.32f,.32f,.32f):new Color(.6f,.6f,.6f);
            if(fringe)shroudGroundFringeCells++;
            if(w.Map.OriginalDefinition is not null){QueueOriginalMapTile(w,x,y,light,groundOnly:fringe);continue;}
            var tile=terrain![x,y];QueueLandscapeTile(w,x,y,tile.Template,tile.Frame,light,groundOnly:fringe);
            if(!fringe && w.Map.Ground[w.Map.Index(x,y)]==Terrain.Rock)depthItems.Add(((x+y+1)*Rules.Scale,-1,null,x,y));
        }
        // Opaque ground is complete before any unit sprite is submitted. A tile
        // centroid is not a safe sprite depth for units moving within that tile.
        QueueOriginalMapObjects(w);QueueOriginalMapRocks(w);DrawLandscapeGround(w);DrawOriginalMapSmudges();DrawOriginalMapRubble();DrawClassicCrates(w);
        foreach(var entity in w.Buildings.Cast<Entity>().Concat(w.Units).Where(e=>e.Alive && w.Map.IsPlayable(e.CellX,e.CellY)
            && (e is Unit unit?unit.TransportId==0 && unit.ParasiteHostId==0 && (Controller.IsSpectating || w.ClassicUnitVisibleTo(viewer,unit)):e.Owner==viewer || Controller.ViewVisible(e.CellX,e.CellY))))
        {
            if(!EntitySurfaceOnscreen(entity))continue;
            depthItems.Add((entity.X+entity.Y+(entity is Unit{Altitude:>0}?1_000_000:0),entity.Id,entity,0,0));
        }
        SortBuildingSurfaces();
        foreach(var item in depthItems)
        {
            var entity=item.Entity;
            if(entity is null)
            {
                if(item.Y==OriginalObjectDepthSentinel){DrawOriginalMapObjectAt(item.X);continue;}
                if(item.Y==OriginalRockDepthSentinel){DrawOriginalMapRockAt(item.X);continue;}
                var pos=ProjectGround(new Vector2(item.X+.5f,item.Y+.5f));
                var noise=BattleMap.Noise(item.X,item.Y,w.Settings.Seed);
                var playable=w.Map.IsPlayable(item.X,item.Y);var visible=playable && Controller.ViewVisible(item.X,item.Y);
                var light=visible?Colors.White:playable?new Color(.32f,.32f,.32f):new Color(.6f,.6f,.6f);
                if(!Controller.Graphics.DrawScenery(this,noise%3==0?$"trock{noise%5+1:00}":$"tree{noise%8+1:00}",0,pos,Zoom,light))DrawRock(pos,light*new Color("858674"));
                continue;
            }
            var point=entity is Building surface?BuildingGroundAnchor(surface):UnitActorPoint((Unit)entity);
            if(entity is Building building)DrawBuilding(w,building,point);else DrawUnit((Unit)entity,point);
            DrawLandscapeForeground(w,entity,point);
        }
        if(Controller.ShowOrderLines)
        foreach(var unit in w.Units.Where(u=>u.Alive && u.Owner==viewer && Selection.Contains(u.Id) && (u.GoalX!=u.CellX || u.GoalY!=u.CellY)))
        {
            var a=ProjectGround(new Vector2(unit.X/(float)Rules.Scale,unit.Y/(float)Rules.Scale));var b=ProjectGround(new Vector2(unit.GoalX+.5f,unit.GoalY+.5f));
            DrawLine(a,b,unit.TargetId>0?new Color(1,.15f,.1f,.65f):new Color(.2f,1,.1f,.65f),Zoom);
        }
        if(Controller.ShowHiddenUnits)
        foreach(var unit in w.Units.Where(u=>u.Alive && u.Owner==viewer))
        {
            var p=ProjectGround(new Vector2(unit.X/(float)Rules.Scale,unit.Y/(float)Rules.Scale))-new Vector2(0,10)*Zoom;
            if(w.Buildings.Any(b=>b.Alive && ProjectGround(new Vector2(b.X/(float)Rules.Scale,b.Y/(float)Rules.Scale)).DistanceSquaredTo(p)<1600*Zoom*Zoom))DrawArc(p,5*Zoom,0,MathF.Tau,12,new Color(.15f,1,.1f,.7f),Zoom);
        }
        DrawClassicProjectiles(w);DrawWeaponEffects();
        DrawShroudEdges(w,viewer);
        var cell=UnprojectGround(pointer);var cx=(int)MathF.Floor(cell.X);var cy=(int)MathF.Floor(cell.Y);
        if(Placement is not null)
        {
            var def=Rules.Building(Placement.Value,w.Players[Math.Max(0,Controller.LocalPlayer)].Setup.Country);var free=w.CanPlaceBuilding(Controller.LocalPlayer,Placement.Value,cx,cy);
            var corners=Footprint(cx,cy,def.Width,def.Height);DrawColoredPolygon(corners,free?new Color(.45f,1,.5f,.3f):new Color(1,.2f,.15f,.35f));DrawPolyline([..corners,corners[0]],free?new Color("b9ef94"):new Color("ef7665"),2);
        }
        else if(SupportMode || AttackMode)
        {DrawArc(pointer,14*Zoom,0,MathF.Tau,24,new Color("e5c46e"),2);DrawLine(pointer-new Vector2(20,0),pointer+new Vector2(20,0),new Color("e5c46e"));DrawLine(pointer-new Vector2(0,20),pointer+new Vector2(0,20),new Color("e5c46e"));}
        if(dragging && pressPosition.DistanceTo(pointer)>5)
        {var rect=new Rect2(pressPosition,pointer-pressPosition).Abs();DrawRect(rect,new Color(.4f,.8f,.5f,.1f));DrawRect(rect,new Color("bcdf92"),false,1);}

    }
    void DrawShroudEdges(World world,int viewer)=>DrawOriginalShroud(world);
    void Diamond(Vector2 pos,float halfW,float halfH,Color color)=>DrawColoredPolygon([pos+new Vector2(0,-halfH),pos+new Vector2(halfW,0),pos+new Vector2(0,halfH),pos+new Vector2(-halfW,0)],color);
    void DrawRock(Vector2 p,Color color)
    {DrawColoredPolygon([p+new Vector2(-14,1)*Zoom,p+new Vector2(-6,-12)*Zoom,p+new Vector2(8,-13)*Zoom,p+new Vector2(17,2)*Zoom,p+new Vector2(2,7)*Zoom],color);DrawLine(p+new Vector2(-6,-12)*Zoom,p+new Vector2(2,7)*Zoom,color.Darkened(.25f),2*Zoom);}
    Vector2[] Footprint(int x,int y,int width,int height)=>GroundFootprint(x,y,width,height);
    void DrawBuilding(World world,Building b,Vector2 p)
    {
        var d=b.Definition;var color=PlayerColors[b.Owner%8];var corners=Footprint(b.CellLeft,b.CellTop,d.Width,d.Height);
        if(Selection.Contains(b.Id))DrawPolyline([..corners,corners[0]],new Color("c4e997"),2*Zoom);
        if(Controller.Graphics.DrawBuilding(this,b,b.FoundationCountry,p,Zoom,world))
        {
            if(Selection.Contains(b.Id) || b.Health<b.MaxHealth)HealthBar(p-new Vector2(0,65)*Zoom,b.Health,b.MaxHealth,54*Zoom);
            if(!b.Complete)ProgressBar(p+new Vector2(0,12)*Zoom,b.ConstructionProgress,new Color("d9bd6e"));
            else if(b.Queue.Count>0)ProgressBar(p+new Vector2(0,12)*Zoom,b.ProductionProgress/(float)Rules.Unit(b.Queue[0]).BuildTicks,new Color("94c8e0"));
            if(Selection.Contains(b.Id))DrawString(ThemeDB.FallbackFont,p+new Vector2(-38,30)*Zoom,d.Name,fontSize:13,modulate:new Color("e2e9c6"));
            return;
        }
        DrawColoredPolygon(corners,new Color("252e27"));
        var height=b.Type switch{BuildingType.ConstructionYard=>42,BuildingType.PowerPlant=>33,BuildingType.Refinery=>32,BuildingType.WarFactory=>34,BuildingType.Radar=>28,BuildingType.BattleLab=>47,BuildingType.GrandCannon=>24,BuildingType.Wall=>12,_=>23};
        if(!b.Complete)height=(int)(8+(height-8)*b.ConstructionProgress);
        var up=new Vector2(0,-height)*Zoom;var roof=corners.Select(c=>c+up).ToArray();
        DrawColoredPolygon([corners[1],corners[2],roof[2],roof[1]],new Color("687367").Lerp(color,.22f));
        DrawColoredPolygon([corners[2],corners[3],roof[3],roof[2]],new Color("404d47").Lerp(color,.17f));
        DrawColoredPolygon(roof,new Color("9b9d86").Lerp(color,.18f));DrawPolyline([..roof,roof[0]],new Color("b4b8a3"),1);
        var center=p+up;
        if(b.Type==BuildingType.PowerPlant)
        {
            for(var i=-1;i<=1;i+=2){var chimney=center+new Vector2(i*12,-4)*Zoom;DrawRect(new Rect2(chimney+new Vector2(-5,-24)*Zoom,new Vector2(10,26)*Zoom),new Color("515c55"));DrawCircle(chimney-new Vector2(0,24)*Zoom,6*Zoom,color);}
        }
        else if(b.Type==BuildingType.ConstructionYard)
        {
            DrawLine(center+new Vector2(-17,5)*Zoom,center+new Vector2(-17,-29)*Zoom,color,4*Zoom);
            DrawLine(center+new Vector2(-17,-29)*Zoom,center+new Vector2(27,-20)*Zoom,color,4*Zoom);
            DrawLine(center+new Vector2(27,-20)*Zoom,center+new Vector2(27,-6)*Zoom,new Color("39443d"),1);
            DrawCircle(center+new Vector2(27,-5)*Zoom,3*Zoom,new Color("343b31"));
        }
        else if(b.Type==BuildingType.WarFactory)
        {
            DrawColoredPolygon([corners[2]+new Vector2(-20,-4)*Zoom,corners[2]+new Vector2(2,-13)*Zoom,corners[2]+new Vector2(2,-30)*Zoom,corners[2]+new Vector2(-20,-21)*Zoom],new Color("192421"));
            DrawRect(new Rect2(center+new Vector2(-17,-8)*Zoom,new Vector2(34,10)*Zoom),color);
        }
        else if(b.Type==BuildingType.Radar)
        {DrawLine(center,center+new Vector2(0,-25)*Zoom,color,4*Zoom);DrawArc(center+new Vector2(0,-25)*Zoom,15*Zoom,.1f,MathF.PI-.1f,16,new Color("c4c6a6"),5*Zoom);}
        else if(b.Type is BuildingType.GrandCannon or BuildingType.TeslaCoil or BuildingType.Pillbox)
        {DrawCircle(center,10*Zoom,color.Darkened(.1f));DrawLine(center,center+new Vector2(23,-11)*Zoom,new Color("38463f"),b.Type==BuildingType.GrandCannon?7*Zoom:4*Zoom);if(b.Type==BuildingType.TeslaCoil)for(var i=0;i<3;i++)DrawEllipse(center-new Vector2(0,7+i*7)*Zoom,new Vector2(9-i*2,3)*Zoom,color);}
        else
        {DrawRect(new Rect2(center+new Vector2(-12,-5)*Zoom,new Vector2(24,10)*Zoom),color);DrawLine(center+new Vector2(-16,9)*Zoom,center+new Vector2(16,-7)*Zoom,new Color("d8cc92"),2*Zoom);}
        if(Selection.Contains(b.Id) || b.Health<b.MaxHealth)HealthBar(p+new Vector2(0,-height-25)*Zoom,b.Health,b.MaxHealth,54*Zoom);
        if(!b.Complete)ProgressBar(p+new Vector2(0,12)*Zoom,b.ConstructionProgress,new Color("d9bd6e"));
        else if(b.Queue.Count>0)ProgressBar(p+new Vector2(0,12)*Zoom,b.ProductionProgress/(float)Rules.Unit(b.Queue[0]).BuildTicks,new Color("94c8e0"));
        if(b.Repairing && b.Health<b.MaxHealth)DrawString(ThemeDB.FallbackFont,center-new Vector2(4,15)*Zoom,"+",fontSize:22,modulate:new Color("b9e58b"));
        if(Selection.Contains(b.Id))DrawString(ThemeDB.FallbackFont,p+new Vector2(-38,30)*Zoom,d.Name,fontSize:13,modulate:new Color("e2e9c6"));
    }
    void DrawEllipse(Vector2 p,Vector2 radius,Color color)
    {
        var points=Enumerable.Range(0,20).Select(i=>p+new Vector2(MathF.Cos(i*MathF.Tau/20)*radius.X,MathF.Sin(i*MathF.Tau/20)*radius.Y)).ToArray();DrawColoredPolygon(points,color);
    }
    void DrawUnit(Unit u,Vector2 p)
    {
        var drawOwner=Controller.IsSpectating?u.Owner:Controller.Battle!.ClassicApparentOwner(Math.Max(0,Controller.LocalPlayer),u);
        var color=PlayerColors[drawOwner%8];var infantry=u.Definition.Infantry;
        var pose=infantry || u.Definition.Domain!=MovementDomain.Land?new VehicleSurfacePose(Vector2.Right,Vector2.Down,Vector2.Zero,0,0):SampleVehiclePose(u);
        if(!infantry)
        {
            p+=pose.Lift;DrawVehicleGroundShadow(u,p,pose);
        }
        if(Selection.Contains(u.Id))DrawSelectionCorners(p,new Vector2(infantry?8:21,infantry?26:24)*Zoom);
        var dir=new Vector2((u.FacingX-u.FacingY)*2,u.FacingX+u.FacingY).Normalized();if(dir==Vector2.Zero)dir=Vector2.Right;
        var body=p-new Vector2(0,infantry?8:6)*Zoom;
        var original=infantry?Controller.Graphics.DrawInfantry(this,u,p,Zoom,Controller.Battle!.Tick,u.OriginCountry,drawOwner):Controller.Graphics.DrawVehicleSurfacePose(this,u,p,Zoom,Controller.Battle!,pose.Gradient,drawOwner);
        if(!original && infantry)
        {
            DrawLine(p+new Vector2(-2,0)*Zoom,body+new Vector2(-2,0)*Zoom,new Color("1a2623"),3*Zoom);
            DrawLine(p+new Vector2(3,0)*Zoom,body+new Vector2(2,0)*Zoom,new Color("1a2623"),3*Zoom);
            DrawRect(new Rect2(body+new Vector2(-4,-5)*Zoom,new Vector2(8,10)*Zoom),u.Type==UnitType.Engineer?new Color("d9bf64"):color);
            DrawCircle(body-new Vector2(0,8)*Zoom,3.8f*Zoom,new Color("c4b69a"));
            DrawLine(body,body+dir*(u.Type==UnitType.Sniper?15:9)*Zoom,new Color("263128"),2*Zoom);
            if(u.Type==UnitType.Dog){DrawEllipse(body+new Vector2(0,5)*Zoom,new Vector2(7,4)*Zoom,color);}
        }
        else if(!original)
        {
            var front=dir*14*Zoom;var side=new Vector2(-dir.Y,dir.X)*9*Zoom;
            DrawLine(body-front-side,body+front-side,new Color("1b2421"),6*Zoom);DrawLine(body-front+side,body+front+side,new Color("1b2421"),6*Zoom);
            DrawColoredPolygon([body+front+side,body+front-side,body-front-side,body-front+side],color.Darkened(.2f));
            DrawLine(body-front-side,body+front-side,color.Lightened(.18f),2*Zoom);
            if(u.Definition.Miner)
            {
                DrawRect(new Rect2(body+new Vector2(-8,-7)*Zoom,new Vector2(15,10)*Zoom),new Color("909484"));
                if(u.Cargo>0)DrawRect(new Rect2(body+new Vector2(-6,-5)*Zoom,new Vector2(12*u.CargoBales/(float)World.HarvesterStorageBales(u.Type),5)*Zoom),new Color("d9bb62"));
            }
            else
            {
                DrawEllipse(body-new Vector2(0,4)*Zoom,new Vector2(8,6)*Zoom,color.Lightened(.1f));
                DrawLine(body-new Vector2(0,5)*Zoom,body+dir*23*Zoom-new Vector2(0,5)*Zoom,u.Type==UnitType.TeslaTank?new Color("9adce2"):new Color("485b52"),u.Type==UnitType.Apocalypse?6*Zoom:4*Zoom);
            }
        }
        if(u.Definition.Miner)DrawMiningActivity(u,p);
        if(Selection.Contains(u.Id) || u.Health<u.MaxHealth)HealthBar(p+new Vector2(0,infantry?-26:-24)*Zoom,u.Health,u.MaxHealth,(infantry?24:38)*Zoom);
        if(u.Kills>=3)DrawString(ThemeDB.FallbackFont,p+new Vector2(9,-18)*Zoom,"★",fontSize:12,modulate:new Color("f6d878"));
    }
    void DrawSelectionCorners(Vector2 p,Vector2 half)
    {
        var rect=new Rect2(p-new Vector2(half.X,half.Y),new Vector2(half.X*2,half.Y+5*Zoom));
        var color=new Color("00ed00");var arm=4*Zoom;
        foreach(var corner in new[]{rect.Position,new Vector2(rect.End.X,rect.Position.Y),rect.End,new Vector2(rect.Position.X,rect.End.Y)})
        {
            var dx=corner.X==rect.Position.X?arm:-arm;var dy=corner.Y==rect.Position.Y?arm:-arm;
            DrawLine(corner,corner+new Vector2(dx,0),color,Zoom);DrawLine(corner,corner+new Vector2(0,dy),color,Zoom);
        }
    }
    void HealthBar(Vector2 p,int health,int max,float width)
    {
        var ratio=Math.Clamp(health/(float)max,0,1);var origin=p-new Vector2(width/2,0);
        DrawRect(new Rect2(origin-new Vector2(1,1)*Zoom,new Vector2(width+2*Zoom,5*Zoom)),Colors.Black);
        var color=ratio>.5f?new Color("00df00"):ratio>.25f?new Color("eee300"):new Color("e00000");
        DrawRect(new Rect2(origin,new Vector2(width*ratio,3*Zoom)),color);
        for(var x=3*Zoom;x<width;x+=4*Zoom)DrawLine(origin+new Vector2(x,0),origin+new Vector2(x,3*Zoom),new Color(0,0,0,.65f),Zoom);
    }
    void ProgressBar(Vector2 p,float progress,Color color)
    {DrawRect(new Rect2(p-new Vector2(25,0)*Zoom,new Vector2(50,3)*Zoom),new Color("18201b"));DrawRect(new Rect2(p-new Vector2(25,0)*Zoom,new Vector2(50*Math.Clamp(progress,0,1),3)*Zoom),color);}

    public override void _GuiInput(InputEvent input)
    {
        var world=Controller.Battle;if(world is null || Controller.MatchMenuOpen)return;
        if(Controller.ChatTyping && input is InputEventMouseButton{Pressed:true}){Controller.CloseBattleChat();AcceptEvent();return;}
        if(HandleMaintenanceInput(input))return;
        if(HandleDeploymentRelease(input))return;
        if(input is InputEventMouseMotion motion)
        {pointer=motion.Position;if(panning)PanScreen(-motion.Relative);QueueRedraw();return;}
        if(input is not InputEventMouseButton button)return;
        pointer=button.Position;
        if(button.ButtonIndex==MouseButton.Middle){panning=button.Pressed;AcceptEvent();return;}
        if(button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {AcceptEvent();return;}
        if(button.ButtonIndex==MouseButton.Right && button.Pressed)
        {
            GrabFocus();if(Controller.IsSpectating){AcceptEvent();return;}
            if(Placement is not null || SupportMode || AttackMode){Placement=null;Controller.CancelClassicPowerTarget();AttackMode=false;Controller.Notify("已取消当前命令");}
            else CommandAt(pointer,false);
            AcceptEvent();return;
        }
        if(button.ButtonIndex!=MouseButton.Left)return;
        if(button.Pressed)
        {
            GrabFocus();
            if(HandleDeploymentPress(button))return;
            if(Placement is null && !SupportMode && (button.CtrlPressed || AttackMode && Hit(pointer) is null))
            {
                IssueTerrainAttackAt(pointer,true);AttackMode=false;dragging=false;AcceptEvent();return;
            }
            var cell=UnprojectGround(pointer);var x=(int)MathF.Floor(cell.X);var y=(int)MathF.Floor(cell.Y);
            if(!world.Map.IsPlayable(x,y)){AcceptEvent();return;}
            if(Placement is not null)
            {
                if(world.CanPlaceBuilding(Controller.LocalPlayer,Placement.Value,x,y)){Controller.Issue(new GameCommand{Type=CommandType.Build,Building=Placement.Value,X=x,Y=y});Placement=null;}
                else Controller.Notify("该位置不能建造，请重新选择；建筑仍保持就绪");
                AcceptEvent();return;
            }
            if(SupportMode){Controller.IssueClassicPowerAt(x,y);return;}
            if(AttackMode){CommandAt(pointer,true);AttackMode=false;return;}
            if(button.DoubleClick && Hit(pointer) is Unit same)
            {
                if(!button.ShiftPressed)Selection.Clear();foreach(var u in world.Units)if(u.Alive && u.TransportId==0 && u.ParasiteHostId==0 && u.Owner==Controller.LocalPlayer && u.Type==same.Type && new Rect2(Vector2.Zero,Size).HasPoint(UnitActorPoint(u)))Selection.Add(u.Id);return;
            }
            pressPosition=pointer;dragging=true;
        }
        else if(dragging)
        {
            dragging=false;if(!button.ShiftPressed)Selection.Clear();
            if(pressPosition.DistanceTo(pointer)>5)
            {
                var rect=new Rect2(pressPosition,pointer-pressPosition).Abs();
                foreach(var u in world.Units)if(u.Alive && u.TransportId==0 && u.ParasiteHostId==0 && u.Owner==Controller.LocalPlayer && rect.HasPoint(UnitActorPoint(u)))Selection.Add(u.Id);
            }
            else if(Hit(pointer) is Entity e)
            {if(button.ShiftPressed && Selection.Contains(e.Id))Selection.Remove(e.Id);else Selection.Add(e.Id);}
        }
        if(button.ButtonIndex==MouseButton.Left && !button.Pressed && Selection.Count>0)Controller.AudioSelection();
        AcceptEvent();
    }
    Entity? Hit(Vector2 point)
    {
        var w=Controller.Battle!;
        var unit=w.Units.Where(u=>u.Alive && u.TransportId==0 && u.ParasiteHostId==0 && (Controller.IsSpectating || w.ClassicUnitVisibleTo(Math.Max(0,Controller.LocalPlayer),u)))
            .Select(u=>(Unit:u,Distance:UnitActorPoint(u).DistanceSquaredTo(point+new Vector2(0,7)*Zoom)))
            .Where(e=>e.Distance<225*Zoom*Zoom).OrderBy(e=>e.Distance).ThenBy(e=>e.Unit.Id).FirstOrDefault().Unit;
        if(unit is not null)return unit;
        foreach(var b in w.Buildings.AsEnumerable().Reverse())
        {
            if(b.Owner!=Controller.LocalPlayer && !Controller.ViewVisible(b.CellX,b.CellY))continue;
            var corners=Footprint(b.CellLeft,b.CellTop,b.Definition.Width,b.Definition.Height);
            var left=corners.Min(p=>p.X);var right=corners.Max(p=>p.X);var top=corners.Min(p=>p.Y)-45*Zoom;var bottom=corners.Max(p=>p.Y);
            if(new Rect2(left,top,right-left,bottom-top).HasPoint(point))return b;
        }
        return null;
    }
    void CommandAt(Vector2 point,bool attackMove)
    {
        if(!attackMove && IssueMcvDeploymentAt(point))return;
        if(Hit(point) is null && IssueTerrainAttackAt(point,attackMove))return;
        var w=Controller.Battle!;var cell=UnprojectGround(point);var x=(int)MathF.Floor(cell.X);var y=(int)MathF.Floor(cell.Y);
        if(!w.Map.IsPlayable(x,y))return;
        var actors=Selection.Select(w.Find).OfType<Unit>().Where(u=>u.Owner==Controller.LocalPlayer).Select(u=>u.Id).ToArray();
        if(actors.Length>0)
        {
            var hit=Hit(point);var engineer=actors.All(id=>((Unit)w.Find(id)!).Type==UnitType.Engineer);
            if(hit is Unit transport && transport.Owner==Controller.LocalPlayer && transport.Definition.Passengers>0 && actors.Any(id=>id!=transport.Id))
            {Controller.Issue(new GameCommand{Type=CommandType.EnterTransport,Actors=actors,Target=transport.Id});return;}
            if(hit is not null)
            {
                var special=actors.Where(id=>w.Find(id) is Unit unit && (unit.Type is UnitType.Spy or UnitType.CrazyIvan or UnitType.ChronoIvan or UnitType.Tanya or UnitType.ChronoCommando || unit.Type==UnitType.TeslaTrooper && hit is Building{Type:BuildingType.TeslaCoil} || unit.Type==UnitType.Ifv && w.Allied(Controller.LocalPlayer,hit.Owner))).ToArray();
                if(special.Length>0)
                {
                    Controller.Issue(new GameCommand{Type=CommandType.Special,Actors=special,Target=hit.Id,X=hit.CellX,Y=hit.CellY});
                    actors=actors.Except(special).ToArray();if(actors.Length==0)return;
                }
            }
            if(hit is not null && (!w.Allied(Controller.LocalPlayer,hit.Owner)||engineer&&hit is Building))
                Controller.Issue(new GameCommand{Type=CommandType.Attack,Actors=actors,Target=hit.Id,X=hit.CellX,Y=hit.CellY});
            else Controller.Issue(new GameCommand{Type=attackMove?CommandType.AttackMove:CommandType.Move,Actors=actors,X=x,Y=y});
        }
        else
        {
            var building=Selection.Select(w.Find).OfType<Building>().FirstOrDefault(b=>b.Owner==Controller.LocalPlayer);
            if(building is not null)Controller.Issue(new GameCommand{Type=CommandType.Rally,Actors=[building.Id],X=x,Y=y});
        }
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if(Controller.ChatTyping || Controller.MatchMenuOpen || Controller.IsSpectating || input is not InputEventKey {Pressed:true,Echo:false} key || Controller.Battle is null)return;
        if(key.Keycode==Controller.AttackShortcut){AttackMode=true;Placement=null;SupportMode=false;Controller.Notify("左键选择攻击目标或地面；Ctrl + 左键强制开火");}
        else if(key.Keycode==Controller.StopShortcut)Controller.StopSelection();
        else if(key.Keycode==Key.D)Controller.Issue(new GameCommand{Type=CommandType.Deploy,Actors=Selection.ToArray()});
        else if(key.Keycode==Key.U)Controller.Issue(new GameCommand{Type=CommandType.Unload,Actors=Selection.ToArray()});
        else if(key.Keycode==Key.Escape){Placement=null;Controller.CancelClassicPowerTarget();AttackMode=false;Selection.Clear();}
        else if(key.Keycode==Key.Space)
        {
            var selected=Selection.Select(Controller.Battle.Find).FirstOrDefault(e=>e is not null);
            if(selected is not null)SetCameraCell(new(selected.X/(float)Rules.Scale,selected.Y/(float)Rules.Scale));
        }
        else if((long)key.Keycode>=48 && (long)key.Keycode<=57)
        {
            var group=(int)key.Keycode-48;
            if(key.CtrlPressed)groups[group]=Selection.ToArray();
            else if(groups.TryGetValue(group,out var ids)){Selection.Clear();foreach(var id in ids)if(Controller.Battle.Find(id) is {Owner:var owner}&&owner==Controller.LocalPlayer)Selection.Add(id);}
        }
        else return;
        GetViewport().SetInputAsHandled();
    }
}
