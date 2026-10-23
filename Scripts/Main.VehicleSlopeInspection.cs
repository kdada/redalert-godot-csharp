using Godot;
using RedAlert.Simulation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RedAlert;

public partial class Main
{
    bool vehicleSlopeChecksRunning;
    Godot.Collections.Dictionary vehicleSlopeReport=new();
    public Godot.Collections.Dictionary GetVehicleSlopeAudit()=>vehicleSlopeReport;

    sealed record VehicleSlopeSite(ClassicMapInfo Info,BattleMap Map,ClassicMapCell Cell);
    static MatchSettings VehicleSlopeSettings(ClassicMapInfo info)=>new(20261022,
        [new PlayerSetup("Slope inspection",Country.America,0),new PlayerSetup("Fixture survivor",Country.Russia,1)],
        Width:info.Width,Height:info.Height,MapId:info.Id,ClassicStart:true,StartingUnits:0,ShortGame:false,Crates:false);
    static void VehicleSlopeRequire(bool value,string message)
    {if(!value)throw new InvalidOperationException(message);}
    static string VehicleSlopeDirection(Cell slope)=>Math.Sign(slope.X)+","+Math.Sign(slope.Y);
    static bool VehicleSlopeFits(BattleMap map,ClassicMapCell cell)
    {
        var x=cell.X*Rules.Scale+Rules.Scale/2;var y=cell.Y*Rules.Scale+Rules.Scale/2;
        return cell.Playable && cell.LandPassable && map.Walkable(cell.X,cell.Y)
            && !map.BuildingBlocksMovement(x,y,x,y,Rules.Scale/3);
    }
    static double VehicleSlopeCentrality(VehicleSlopeSite site)
    {
        var map=site.Map;var c=site.Cell;
        var u=c.X-c.Y-(map.PlayableMinU+map.PlayableMaxU)*.5;
        var v=c.X+c.Y+1-(map.PlayableMinV+map.PlayableMaxV)*.5;
        return u*u+v*v;
    }
    Godot.Collections.Dictionary VehicleSlopeGraphicsAudit()=>new()
    {
        ["poseCacheCount"]=Graphics.VehiclePoseCacheCount,["poseCacheBytes"]=Graphics.VehiclePoseCacheBytes,
        ["poseRenders"]=Graphics.VehiclePoseRenders,["poseCacheHits"]=Graphics.VehiclePoseCacheHits,
        ["minimumPoseOpaquePixels"]=Graphics.VehiclePoseMinimumOpaquePixels,["minimumPoseOpaqueHeight"]=Graphics.VehiclePoseMinimumOpaqueHeight,
        ["poseFallbacks"]=Graphics.VehiclePoseFallbacks,["pendingPoses"]=Graphics.VehiclePosePendingCount,
        ["maximumPoseRenderMilliseconds"]=Graphics.VehiclePoseMaximumRenderMilliseconds,
        ["textures"]=Graphics.TexturesCreated,["vehicleAtlases"]=Graphics.VehicleCount
    };
    Godot.Collections.Dictionary VehicleSlopeRenderAudit()=>new()
    {
        ["display"]=DisplayServer.GetName(),["renderer"]=RenderingServer.GetCurrentRenderingMethod(),
        ["drawCalls"]=battlefield?.DrawCalls??0,["slopeVehicles"]=battlefield?.VehiclesWithSlopePose??0,
        ["maximumPitchDegrees"]=battlefield?.MaximumVehiclePitchDegrees??0,
        ["maximumRollDegrees"]=battlefield?.MaximumVehicleRollDegrees??0,
        ["rampTilesDrawn"]=battlefield?.LandscapeRampTilesDrawn??0,
        ["bridgeTilesDrawn"]=battlefield?.BridgeTilesDrawn??0,
        ["originalBridgePiecesDrawn"]=battlefield?.OriginalBridgePiecesDrawn??0,
        ["graphics"]=VehicleSlopeGraphicsAudit()
    };
    Godot.Collections.Dictionary VehicleSlopePositionAudit(World world,Unit unit)
    {
        var map=world.Map;var c=map.OriginalCellAt(unit.CellX,unit.CellY)
            ??throw new InvalidOperationException("Vehicle left the original map cells");
        var slope=map.SurfaceSlopeFixedAt(unit.CellX,unit.CellY);var corners=map.SurfaceCornersAt(unit.CellX,unit.CellY);
        var point=new Vector2(unit.X/(float)Rules.Scale,unit.Y/(float)Rules.Scale);
        var height=map.SurfaceHeightFixed(unit.X,unit.Y)/(float)Rules.Scale;
        return new()
        {
            ["mapId"]=world.Settings.MapId,["sourceSha256"]=map.OriginalDefinition!.SourceSha256,
            ["tick"]=world.Tick,["unit"]=unit.Id,["type"]=unit.Type.ToString(),["rulesId"]=unit.Definition.RulesId,
            ["owner"]=unit.Owner,["originCountry"]=unit.OriginCountry.ToString(),["worldFixedX"]=unit.X,["worldFixedY"]=unit.Y,
            ["x"]=point.X,["y"]=point.Y,["cellX"]=c.X,["cellY"]=c.Y,["sourceX"]=c.SourceX,["sourceY"]=c.SourceY,
            ["rawLevel"]=c.Level,["surfaceBaseLevel"]=c.SurfaceLevel,["surfaceHeight"]=height,
            ["surfaceHeightFixed"]=map.SurfaceHeightFixed(unit.X,unit.Y),["rampType"]=c.RampType,
            ["tileIndex"]=c.TileIndex,["subTile"]=c.SubTile,["template"]=c.Template,
            ["playable"]=map.IsPlayable(c.X,c.Y),["walkable"]=map.Walkable(c.X,c.Y),
            ["canOccupy"]=world.CanOccupyUnitPosition(unit,unit.X,unit.Y),["bridge"]=c.Bridge,["highBridge"]=c.HighBridge,
            ["slopeX"]=slope.X/(float)Rules.Scale,["slopeY"]=slope.Y/(float)Rules.Scale,
            ["slopeDirection"]=VehicleSlopeDirection(slope),["slopeSum"]= (slope.X+slope.Y)/(float)Rules.Scale,
            ["oldAffineBasisY"]=1-(slope.X+slope.Y)/(2f*Rules.Scale),
            ["cornerHeightsFixed"]=new Godot.Collections.Array<int>{corners.NorthWest,corners.NorthEast,corners.SouthEast,corners.SouthWest},
            ["bodyFacingX"]=unit.BodyFacingX,["bodyFacingY"]=unit.BodyFacingY,["turretFacingX"]=unit.FacingX,["turretFacingY"]=unit.FacingY,
            ["order"]=unit.Order.ToString(),["pathIndex"]=unit.PathIndex,["pathCount"]=unit.Path.Count,
            ["projectedGround"]=battlefield!.ProjectGround(point),["camera"]=battlefield.CameraCell
        };
    }
    (World World,Unit Grizzly,Unit Rhino) VehicleSlopeOpenFixture(ClassicMapInfo info)
    {
        CancelLocalPreparation();StopExternalAi();network?.Dispose();network=null;
        var world=new World(VehicleSlopeSettings(info));
        VehicleSlopeRequire(world.Map.OriginalDefinition?.Id==info.Id,"Inspection must use the registered original map: "+info.Id);
        VehicleSlopeRequire(world.Map.OriginalDefinition!.SourceSha256==info.SourceSha256,"Original map hash differs from the catalog: "+info.Id);
        localWorld=world;terrainInspection=true;terrainInspectionMotion=false;accumulator=0;ended=false;failure="";
        localPending.Clear();localReplay.Clear();
        var start=world.Map.Spawns[0];
        var grizzly=world.SpawnUnit(0,UnitType.Grizzly,start.X+3,start.Y+1,Country.America);
        var rhino=world.SpawnUnit(0,UnitType.Rhino,start.X+4,start.Y+1,Country.Russia);
        inspectionUnit=grizzly.Id;ShowBattle();RevealTerrainInspection();RefreshHud();minimap?.InvalidateTerrain();
        return(world,grizzly,rhino);
    }
    void VehicleSlopePlace(World world,Unit unit,Cell cell,Cell heading)
    {
        SetInspectionUnit(unit,new Vector2(cell.X+.5f,cell.Y+.5f),heading);
        unit.PathVersion=-1;unit.NextPathTick=0;unit.BlockedTicks=0;inspectionUnit=unit.Id;
        VehicleSlopeRequire(world.Map.OriginalCellAt(cell.X,cell.Y) is not null && world.CanOccupyUnitPosition(unit,unit.X,unit.Y),
            "Original-map tank placement is not walkable: "+world.Settings.MapId+" "+cell);
        RevealTerrainInspection();battlefield!.Selection.Clear();battlefield.Selection.Add(unit.Id);
        VehicleSlopeCenter(world,unit);RefreshHud();battlefield.QueueRedraw();
    }
    void VehicleSlopeCenter(World world,Unit unit)
    {
        var point=new Vector2(unit.X/(float)Rules.Scale,unit.Y/(float)Rules.Scale);
        var level=(float)world.Map.SurfaceHeightAt(point.X,point.Y);
        battlefield!.SetCameraCell(point-Vector2.One*(level*.5f));
        // Keep ordinary edge scrolling from moving the camera between captures.
        if(softwareBattlePointer)softwarePointer=battlefield.GetGlobalRect().GetCenter();
    }
    async Task<Godot.Collections.Dictionary> VehicleSlopeCapture(string folder,string tag,World world,Unit unit,string mode,bool ramp)
    {
        var view=battlefield??throw new InvalidOperationException("Actual Battlefield is missing");
        VehicleSlopeRequire(ReferenceEquals(Battle,world) && ReferenceEquals(view.Controller,this),"Capture is not using this Main's actual world and renderer");
        var drawBefore=view.DrawCalls;var rendersBefore=(long)Graphics.VehiclePoseRenders;var hitsBefore=(long)Graphics.VehiclePoseCacheHits;
        view.QueueRedraw();await PointerParityFrame();await PointerParityAfterDraw();
        VehicleSlopeRequire(view.DrawCalls>drawBefore,"Battlefield did not draw the fixture");
        VehicleSlopeRequire(world.CanOccupyUnitPosition(unit,unit.X,unit.Y),"Captured tank is outside its physical support");
        var anchor=view.ProjectGround(new Vector2(unit.X/(float)Rules.Scale,unit.Y/(float)Rules.Scale));
        VehicleSlopeRequire(new Rect2(Vector2.Zero,view.Size).Grow(-24).HasPoint(anchor),"Tank is outside the visible native battlefield");
        if(ramp)
        {
            VehicleSlopeRequire(world.Map.OriginalCellAt(unit.CellX,unit.CellY)?.RampType>0,"Ramp capture lost its real original ramp");
            VehicleSlopeRequire(view.LandscapeRampTilesDrawn>0 && view.VehiclesWithSlopePose>0,"No real ramp and slope vehicle were drawn");
            VehicleSlopeRequire((long)Graphics.VehiclePoseRenders+(long)Graphics.VehiclePoseCacheHits>rendersBefore+hitsBefore,
                "Actual slope frame did not use the runtime VXL pose renderer/cache");
            VehicleSlopeRequire(mode=="cross"?view.MaximumVehicleRollDegrees>.05f:view.MaximumVehiclePitchDegrees>.05f,
                "Expected "+mode+" support angle was not observed by Battlefield");
        }
        VehicleSlopeRequire(double.IsFinite(Graphics.VehiclePoseMaximumRenderMilliseconds),"Pose render timing is not finite");
        using var image=GetViewport().GetTexture().GetImage();
        VehicleSlopeRequire(image.GetWidth()>0 && image.GetHeight()>0,"Native viewport image is empty");
        var png=Path.Combine(folder,tag+".png");var result=image.SavePng(png);
        VehicleSlopeRequire(result==Error.Ok,"Native viewport PNG save failed: "+result);
        var audit=new Godot.Collections.Dictionary
        {
            ["tag"]=tag,["mode"]=mode,["png"]=png,["nativeViewport"]=true,["frame"]=Engine.GetFramesDrawn(),
            ["width"]=image.GetWidth(),["height"]=image.GetHeight(),["position"]=VehicleSlopePositionAudit(world,unit),
            ["render"]=VehicleSlopeRenderAudit(),["poseRendersDuringCapture"]=(long)Graphics.VehiclePoseRenders-rendersBefore,
            ["poseHitsDuringCapture"]=(long)Graphics.VehiclePoseCacheHits-hitsBefore
        };
        File.WriteAllText(Path.Combine(folder,tag+".json"),Godot.Json.Stringify(audit,"\t"));return audit;
    }
    static Godot.Collections.Array<Godot.Collections.Dictionary> VehicleSlopeSourceAudit()
    {
        var sources=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach(var source in new[]{"gtnk","gtnktur","gtnkbarl","htnk","htnktur","htnkbarl"})
        {
            var vxl="res://Assets/Reference/Battlefield/Voxels/"+source+".vxl";
            var hva="res://Assets/Reference/Battlefield/Voxels/"+source+".hva";
            VehicleSlopeRequire(Godot.FileAccess.FileExists(vxl) && Godot.FileAccess.FileExists(hva),"Original VXL/HVA source missing: "+source);
            sources.Add(new(){["vxl"]=vxl,["hva"]=hva});
        }
        return sources;
    }

    // Native Main-root entry. Only official catalog maps, original VXL vehicles,
    // the existing Battlefield, real post-draw images, and at most 48 World.Step calls.
    // Missing official-map coverage is reported explicitly; terrain is never edited.
    public async void RunVehicleSlopeChecks()
    {
        if(vehicleSlopeChecksRunning)return;vehicleSlopeChecksRunning=true;
        var checks=new Godot.Collections.Array<string>();var missing=new Godot.Collections.Array<string>();var coverageNotes=new Godot.Collections.Array<string>();
        var captures=new Godot.Collections.Array<Godot.Collections.Dictionary>();var scans=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var movements=new Godot.Collections.Array<Godot.Collections.Dictionary>();var error="";var folder="";var totalSteps=0;
        vehicleSlopeReport=new()
        {
            ["running"]=true,["completed"]=false,["passed"]=false,["phase"]="starting",["checks"]=checks,
            ["missingConditions"]=missing,["coverageNotes"]=coverageNotes,["captures"]=captures,["mapSearch"]=scans,["movement"]=movements,
            ["oldLongSuitesRun"]=false,["terrainModified"]=false,["maximumWorldSteps"]=48,
            ["captureSource"]="actual Main viewport after Battlefield draw and RenderingServer.FramePostDraw"
        };
        try
        {
            VehicleSlopeRequire(!headless && DisplayServer.GetName()!="headless","Vehicle slope inspection needs the actual native Main root");
            VehicleSlopeRequire(!pointerParityChecksRunning && !battlePolishChecksRunning,"Another native inspection is running");
            ReturnToDeployment();await PointerParityFrame();await PointerParityFrame();
            folder=ProjectSettings.GlobalizePath("res://Reports/vehicle-slope/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Time.GetTicksMsec());
            Directory.CreateDirectory(folder);vehicleSlopeReport["folder"]=folder;vehicleSlopeReport["sources"]=VehicleSlopeSourceAudit();
            vehicleSlopeReport["graphicsBefore"]=VehicleSlopeGraphicsAudit();
            var catalog=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath));
            var sites=new List<VehicleSlopeSite>();var flats=new List<VehicleSlopeSite>();var bridges=new List<VehicleSlopeSite>();
            vehicleSlopeReport["phase"]="searching-official-maps";
            foreach(var info in catalog.Maps)
            {
                var definition=ClassicMapRepository.Get(info.Id);
                VehicleSlopeRequire(definition.SourceSha256==info.SourceSha256,"Registered map differs from catalog: "+info.Id);
                var map=new BattleMap(VehicleSlopeSettings(info));var rampTypes=new Godot.Collections.Dictionary();
                var directions=new HashSet<string>();var walkableRamps=0;var diagonalSumTwo=0;var zeroGradientRamps=0;
                foreach(var cell in definition.RuntimeCells)
                {
                    if(!VehicleSlopeFits(map,cell))continue;
                    var site=new VehicleSlopeSite(info,map,cell);
                    if(cell.Bridge){if(cell.RampType==0 && map.SurfaceCornersAt(cell.X,cell.Y).IsFlat)bridges.Add(site);continue;}
                    if(cell.RampType==0){if(map.SurfaceCornersAt(cell.X,cell.Y).IsFlat)flats.Add(site);continue;}
                    walkableRamps++;var type=cell.RampType.ToString();rampTypes[type]=rampTypes.ContainsKey(type)?rampTypes[type].AsInt32()+1:1;
                    var slope=map.SurfaceSlopeFixedAt(cell.X,cell.Y);
                    if(slope.X==0 && slope.Y==0){zeroGradientRamps++;continue;}
                    sites.Add(site);directions.Add(VehicleSlopeDirection(slope));
                    if(slope.X!=0 && slope.Y!=0 && slope.X+slope.Y==2*Rules.Scale)diagonalSumTwo++;
                }
                scans.Add(new()
                {
                    ["mapId"]=info.Id,["name"]=info.Name,["sourceSha256"]=definition.SourceSha256,
                    ["originalRampCells"]=definition.RuntimeCells.Count(c=>c.RampType>0),["walkableRampCells"]=walkableRamps,
                    ["originalRampType13Cells"]=definition.RuntimeCells.Count(c=>c.RampType==13),
                    ["walkableRampTypes"]=rampTypes,["directions"]=new Godot.Collections.Array<string>(directions.OrderBy(d=>d)),
                    ["diagonalSumGradientTwo"]=diagonalSumTwo,["zeroCenterGradientRamps"]=zeroGradientRamps,
                    ["walkableFlatCells"]=flats.Count(s=>s.Info.Id==info.Id),["walkableBridgeCells"]=bridges.Count(s=>s.Info.Id==info.Id)
                });
                await PointerParityFrame();
            }
            var selected=sites.GroupBy(s=>VehicleSlopeDirection(s.Map.SurfaceSlopeFixedAt(s.Cell.X,s.Cell.Y)))
                .Select(g=>g.OrderByDescending(s=>{var v=s.Map.SurfaceSlopeFixedAt(s.Cell.X,s.Cell.Y);return Math.Abs(v.X)+Math.Abs(v.Y);})
                    .ThenBy(VehicleSlopeCentrality).ThenBy(s=>s.Info.Id,StringComparer.Ordinal).ThenBy(s=>s.Cell.Y).ThenBy(s=>s.Cell.X).First())
                .OrderBy(s=>s.Info.Id,StringComparer.Ordinal).ThenBy(s=>s.Cell.RampType).ToArray();
            vehicleSlopeReport["slopeDirectionCount"]=selected.Length;
            if(selected.Length<4)missing.Add("Fewer than four distinct walkable real slope directions in the registered official maps");
            if(!sites.Any(s=>{var v=s.Map.SurfaceSlopeFixedAt(s.Cell.X,s.Cell.Y);return v.X!=0 && v.Y!=0 && v.X+v.Y==2*Rules.Scale;}))
            {
                coverageNotes.Add("No walkable official diagonal with slopeX+slopeY=2 (old affine basisY=0); all catalog maps searched, no tile synthesized");
                vehicleSlopeReport["positiveDiagonalUnavailable"]=true;
            }
            if(flats.Count==0)missing.Add("No walkable flat control in the registered official maps");
            if(bridges.Count==0)missing.Add("No walkable original bridge control in the registered official maps");
            VehicleSlopeRequire(selected.Length>0,"Official-map search found no usable ramps; ramp coverage cannot be skipped");
            checks.Add("Searched every registered official catalog map without changing terrain, corners, passability, or ramp types");

            World? world=null;Unit? grizzly=null;Unit? rhino=null;Vector2 grizzlyPark=default,rhinoPark=default;
            async Task Open(VehicleSlopeSite site)
            {
                if(world?.Settings.MapId==site.Info.Id)return;
                (world,grizzly,rhino)=VehicleSlopeOpenFixture(site.Info);
                grizzlyPark=new Vector2(grizzly.X/(float)Rules.Scale,grizzly.Y/(float)Rules.Scale);
                rhinoPark=new Vector2(rhino.X/(float)Rules.Scale,rhino.Y/(float)Rules.Scale);
                await PointerParityFrame();await PointerParityFrame();
            }
            void Park(Unit unit,Vector2 point)=>SetInspectionUnit(unit,point,new Cell(1,0));
            foreach(var site in selected)
            {
                await Open(site);var slope=world!.Map.SurfaceSlopeFixedAt(site.Cell.X,site.Cell.Y);
                var along=new Cell(Math.Sign(slope.X),Math.Sign(slope.Y));var cell=new Cell(site.Cell.X,site.Cell.Y);
                foreach(var mode in new[]{"up","down","cross"})
                {
                    vehicleSlopeReport["phase"]=site.Info.Id+"-ramp-"+site.Cell.RampType+"-"+mode;
                    Park(grizzly!,grizzlyPark);Park(rhino!,rhinoPark);var unit=mode=="down"?rhino!:grizzly!;
                    var heading=mode=="down"?new Cell(-along.X,-along.Y):mode=="cross"?new Cell(-along.Y,along.X):along;
                    VehicleSlopePlace(world,unit,cell,heading);
                    captures.Add(await VehicleSlopeCapture(folder,site.Info.Id+"-ramp-"+site.Cell.RampType+"-"+mode,world,unit,mode,true));
                }
            }
            checks.Add("Original Grizzly/Rhino VXL tanks rendered uphill, downhill, and across "+selected.Length+" real slope directions");
            foreach(var control in new[]{(Name:"flat",Sites:flats),(Name:"bridge",Sites:bridges)})
            {
                if(control.Sites.Count==0)continue;
                var site=control.Sites.OrderBy(VehicleSlopeCentrality).ThenBy(s=>s.Info.Id,StringComparer.Ordinal).First();await Open(site);
                Park(grizzly!,grizzlyPark);Park(rhino!,rhinoPark);
                var span=world!.Map.BridgeAt(site.Cell.X,site.Cell.Y);var heading=span?.Axis==BridgeAxis.Y?new Cell(0,1):new Cell(1,0);
                VehicleSlopePlace(world,grizzly!,new Cell(site.Cell.X,site.Cell.Y),heading);
                captures.Add(await VehicleSlopeCapture(folder,site.Info.Id+"-"+control.Name,world,grizzly!,control.Name,false));
                if(control.Name=="bridge")VehicleSlopeRequire(battlefield!.OriginalBridgePiecesDrawn>0,"Bridge control did not draw original bridge artwork");
                checks.Add(control.Name+" control: original-map physical support and native Battlefield screenshot");
            }

            // Prefer an actual height-changing cardinal edge. A* and World.Step
            // handle the movement; poses are never advanced with fabricated positions.
            (VehicleSlopeSite Site,Cell Low,Cell High)? edge=null;
            foreach(var site in sites.OrderBy(VehicleSlopeCentrality))
            {
                var c=new Cell(site.Cell.X,site.Cell.Y);var center=site.Map.SurfaceHeightFixed(c.X*Rules.Scale+Rules.Scale/2,c.Y*Rules.Scale+Rules.Scale/2);
                foreach(var d in new[]{new Cell(1,0),new Cell(0,1),new Cell(-1,0),new Cell(0,-1)})
                {
                    var n=new Cell(c.X+d.X,c.Y+d.Y);var original=site.Map.OriginalCellAt(n.X,n.Y);
                    if(original is null || !VehicleSlopeFits(site.Map,original) || !site.Map.CanTraverseTerrain(c,n) || !site.Map.CanTraverseTerrain(n,c))continue;
                    var h=site.Map.SurfaceHeightFixed(n.X*Rules.Scale+Rules.Scale/2,n.Y*Rules.Scale+Rules.Scale/2);
                    if(h==center)continue;edge=h<center?(site,n,c):(site,c,n);break;
                }
                if(edge is not null)break;
            }
            if(edge is {} e)
            {
                await Open(e.Site);
                foreach(var down in new[]{false,true})
                {
                    Park(grizzly!,grizzlyPark);Park(rhino!,rhinoPark);var unit=down?rhino!:grizzly!;
                    var start=down?e.High:e.Low;var goal=down?e.Low:e.High;var mode=down?"down":"up";
                    vehicleSlopeReport["phase"]=e.Site.Info.Id+"-motion-"+mode;
                    VehicleSlopePlace(world!,unit,start,new Cell(goal.X-start.X,goal.Y-start.Y));
                    var initialX=unit.X;var initialY=unit.Y;var initialHeight=world!.Map.SurfaceHeightFixed(unit.X,unit.Y);
                    var samples=new Godot.Collections.Array<Godot.Collections.Dictionary>{VehicleSlopePositionAudit(world,unit)};
                    var acceptedBefore=world.AcceptedCommands;var rejectedBefore=world.RejectedCommands;var tickBefore=world.Tick;
                    for(var step=0;step<24;step++)
                    {
                        if(step==0)world.Step([new GameCommand{Tick=world.Tick,Player=0,Sequence=++sequence,Type=CommandType.Move,Actors=[unit.Id],X=goal.X,Y=goal.Y}]);
                        else world.Step();totalSteps++;
                        if((step+1)%6!=0)continue;
                        RevealTerrainInspection();VehicleSlopeCenter(world,unit);battlefield!.QueueRedraw();
                        samples.Add(VehicleSlopePositionAudit(world,unit));
                        captures.Add(await VehicleSlopeCapture(folder,e.Site.Info.Id+"-motion-"+mode+"-"+(step+1).ToString("00"),world,unit,"motion-"+mode,false));
                    }
                    var moved=unit.X!=initialX || unit.Y!=initialY;var finalHeight=world.Map.SurfaceHeightFixed(unit.X,unit.Y);
                    movements.Add(new()
                    {
                        ["mode"]=mode,["mapId"]=world.Settings.MapId,["steps"]=world.Tick-tickBefore,["moved"]=moved,
                        ["acceptedCommands"]=world.AcceptedCommands-acceptedBefore,["rejectedCommands"]=world.RejectedCommands-rejectedBefore,
                        ["initialHeightFixed"]=initialHeight,["finalHeightFixed"]=finalHeight,
                        ["startX"]=start.X,["startY"]=start.Y,["goalX"]=goal.X,["goalY"]=goal.Y,["samples"]=samples
                    });
                    VehicleSlopeRequire(world.Tick-tickBefore==24 && world.AcceptedCommands==acceptedBefore+1 && world.RejectedCommands==rejectedBefore,
                        "Bounded real movement command was rejected or advanced an unexpected number of ticks");
                    VehicleSlopeRequire(moved && (down?finalHeight<initialHeight:finalHeight>initialHeight),"Real ramp movement did not change position and support height "+mode);
                }
                checks.Add("Two actual Move commands, 24 World.Step calls each, uphill/downhill position and support-height change, eight native motion frames");
            }
            else missing.Add("No bidirectionally traversable height-changing walkable ramp edge; bounded movement was unavailable");
            vehicleSlopeReport["graphicsAfter"]=VehicleSlopeGraphicsAudit();
            checks.Add("Runtime VXL pose renders/cache hits and Battlefield slope metrics observed in real post-draw frames");
        }
        catch(Exception exception){error=exception.ToString();GD.PushError("Vehicle slope checks: "+error);}
        finally
        {
            vehicleSlopeReport["phase"]="returning-to-menu";
            try
            {
                ReturnToDeployment();await PointerParityFrame();await PointerParityFrame();
                VehicleSlopeRequire(Battle is null && !terrainInspection && !terrainInspectionMotion,"Vehicle slope inspection left a frozen battle open");
                vehicleSlopeReport["returnedToDeployment"]=true;
            }
            catch(Exception cleanup){error+=(error.Length==0?"":"\n")+"Menu cleanup: "+cleanup;vehicleSlopeReport["returnedToDeployment"]=false;}
            vehicleSlopeReport["running"]=false;vehicleSlopeReport["completed"]=true;vehicleSlopeReport["phase"]="complete";
            vehicleSlopeReport["error"]=error;vehicleSlopeReport["worldSteps"]=totalSteps;vehicleSlopeReport["captureCount"]=captures.Count;
            vehicleSlopeReport["passedAvailableChecks"]=error.Length==0;vehicleSlopeReport["coverageComplete"]=missing.Count==0 && coverageNotes.Count==0;
            vehicleSlopeReport["passed"]=error.Length==0 && missing.Count==0;
            try{if(folder.Length>0)File.WriteAllText(Path.Combine(folder,"acceptance.json"),Godot.Json.Stringify(vehicleSlopeReport,"\t"));}
            catch(Exception writeError){vehicleSlopeReport["passed"]=false;vehicleSlopeReport["reportWriteError"]=writeError.ToString();GD.PushError("Vehicle slope report: "+writeError.Message);}
            vehicleSlopeChecksRunning=false;
        }
    }
}
