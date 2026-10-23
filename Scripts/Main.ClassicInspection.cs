using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    Godot.Collections.Dictionary classicVisualReport=new();
    int classicOcclusionUnitId;
    public Godot.Collections.Dictionary GetClassicVisualAudit()=>classicVisualReport;
    int CountOpaqueTerrainBluePixels(Image image)
    {
        image.Convert(Image.Format.Rgba8);var bytes=image.GetData();var width=image.GetWidth();var count=0;
        var right=Math.Clamp((int)(width*battlefield!.Size.X/GetViewport().GetVisibleRect().Size.X),0,width);
        for(var y=0;y<image.GetHeight();y++)for(var x=0;x<right;x++){var i=(y*width+x)*4;if(bytes[i]==0 && bytes[i+1]==0 && bytes[i+2]==124 && bytes[i+3]==255)count++;}
        return count;
    }
    Godot.Collections.Dictionary classicGameplayReport=new();
    public Godot.Collections.Dictionary GetClassicGameplayAudit()=>classicGameplayReport;
    public async void RunClassicGameplayChecks()
    {
        if(classicGameplayReport.TryGetValue("running",out var running) && running.AsBool())return;
        var folder=ProjectSettings.GlobalizePath("res://Reports/skirmish-parity/gameplay-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
        classicGameplayReport=new(){{"running",true},{"completed",false},{"folder",folder}};
        try
        {
            OpenClassicInspection("mp08t2","play",0);var world=localWorld!;
            var mcv=world.Units.Single(u=>u.Owner==0 && u.Type==UnitType.Mcv);var tank=world.Units.First(u=>u.Owner==0 && u.Type==UnitType.Grizzly);
            var target=world.SpawnUnit(1,UnitType.Rhino,tank.CellX+3,tank.CellY,Country.Russia);target.DisabledUntil=int.MaxValue;
            world.ClassicCrates.Add(new(world.NextClassicCrateId++,tank.CellX,tank.CellY,ClassicCrateKind.Credits,world.Tick,world.Tick+150,500));
            var weapons=gameAudio?.ObservedOriginalWeaponEvents??0;var missing=gameAudio?.MissingOriginalWeaponCues??0;var crates=gameAudio?.PlayedCrateCues??0;
            battlefield!.Selection.Clear();battlefield.Selection.Add(tank.Id);AudioSelection();
            Issue(new(){Type=CommandType.Deploy,Actors=[mcv.Id]});Issue(new(){Type=CommandType.Attack,Actors=[tank.Id],Target=target.Id});
            terrainInspection=false;accumulator=0;
            await ToSignal(GetTree().CreateTimer(2.6),SceneTreeTimer.SignalName.Timeout);
            terrainInspection=true;terrainInspectionMotion=false;
            if(world.Tick<20 || !world.HasBuilding(0,BuildingType.ConstructionYard) || !world.HasBuilding(1,BuildingType.ConstructionYard))throw new InvalidOperationException("Real game loop / MCV deployment / AI bootstrap failed");
            if((gameAudio?.ObservedOriginalWeaponEvents??0)<=weapons || (gameAudio?.MissingOriginalWeaponCues??0)!=missing)throw new InvalidOperationException("Real original weapon audio event was absent or unmapped");
            if((gameAudio?.PlayedCrateCues??0)<=crates)throw new InvalidOperationException("Real crate pickup audio event was absent");
            await PointerParityFrame();await PointerParityAfterDraw();using var image=GetViewport().GetTexture().GetImage();var path=Path.Combine(folder,"native-gameplay.png");
            if(image.SavePng(path)!=Error.Ok)throw new IOException("Gameplay PNG failed");
            classicGameplayReport["passed"]=true;classicGameplayReport["png"]=path;classicGameplayReport["tick"]=world.Tick;classicGameplayReport["acceptedCommands"]=world.AcceptedCommands;classicGameplayReport["rejectedCommands"]=world.RejectedCommands;
            classicGameplayReport["localYardDeployed"]=world.HasBuilding(0,BuildingType.ConstructionYard);classicGameplayReport["aiYardDeployed"]=world.HasBuilding(1,BuildingType.ConstructionYard);classicGameplayReport["audio"]=GetAudioAudit();
            classicGameplayReport["weaponEventsDelta"]=(gameAudio?.ObservedOriginalWeaponEvents??0)-weapons;classicGameplayReport["crateCuesDelta"]=(gameAudio?.PlayedCrateCues??0)-crates;
        }
        catch(Exception error){classicGameplayReport["passed"]=false;classicGameplayReport["error"]=error.ToString();GD.PushError("Classic native gameplay: "+error.Message);}
        finally
        {
            terrainInspection=true;terrainInspectionMotion=false;classicGameplayReport["running"]=false;classicGameplayReport["completed"]=true;File.WriteAllText(Path.Combine(folder,"report.json"),Godot.Json.Stringify(classicGameplayReport,"\t"));
        }
    }
    public Godot.Collections.Dictionary OpenClassicInspection(string mapId="mp23t4",string feature="map",int nation=0)
    {
        CancelLocalPreparation();StopExternalAi();network?.Dispose();network=null;
        var info=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath)).Find(mapId)??throw new ArgumentException("Unknown official map");
        var country=(Country)Math.Clamp(nation,0,8);
        localWorld=new RedAlert.Simulation.World(MatchSettings.Practice(2) with{MapId=mapId,Width=info.Width,Height=info.Height,StartingUnits=10,Crates=feature=="play",Players=[new("指挥官",country,0),new("对手",Country.Russia,1)]});
        var world=localWorld;terrainInspection=true;terrainInspectionMotion=false;accumulator=0;ended=false;failure="";localPending.Clear();localReplay.Clear();
        foreach(var player in world.Players)Array.Fill(player.Visibility,(byte)3);
        var spawn=world.Map.Spawns[0];var focus=new Cell(spawn.X,spawn.Y);
        if(feature is "roster" or "buildings" or "special")
        {
            var candidates=world.Map.OriginalDefinition!.RuntimeCells.Where(c=>c.Playable && c.Buildable && c.X>8 && c.Y>8 && c.X<world.Map.Width-18 && c.Y<world.Map.Height-18);
            foreach(var cell in candidates.OrderBy(c=>Math.Abs(c.X-spawn.X)+Math.Abs(c.Y-spawn.Y)))
            {
                var flat=true;for(var y=cell.Y;y<cell.Y+13 && flat;y++)for(var x=cell.X;x<cell.X+15;x++)
                    if(!world.Map.Walkable(x,y) || world.Map.ElevationAt(x,y)!=cell.SurfaceLevel || feature=="buildings" && (world.Map.Ore[world.Map.Index(x,y)]>0 || world.Map.OriginalCellAt(x,y)?.Buildable!=true)){flat=false;break;}
                if(flat){focus=new Cell(cell.X+7,cell.Y+6);break;}
            }
            var index=0;
            if(feature=="special")
            {
                foreach(var origin in Enum.GetValues<Country>())foreach(var type in Enum.GetValues<UnitType>())
                {
                    var def=Rules.Unit(type,origin);if(def.Country!=origin || !Rules.Allowed(def,origin))continue;
                    var unit=world.SpawnUnit(0,type,focus.X-6+index%6*2,focus.Y-4+index/6*3,origin);unit.DisabledUntil=int.MaxValue;if(def.Domain==MovementDomain.Air)unit.Altitude=Rules.Scale*5;index++;
                }
            }
            else if(feature=="roster")
            foreach(var type in Enum.GetValues<UnitType>())
            {
                var def=Rules.Unit(type,country);if(def.Domain==MovementDomain.Water || !Rules.Allowed(def,country))continue;
                var unit=world.SpawnUnit(0,type,focus.X-6+index%7*2,focus.Y-5+index/7*2,country);unit.DisabledUntil=int.MaxValue;index++;
            }
            else foreach(var item in Rules.Buildings)
            {
                var def=Rules.Building(item.Type,country);if(def.WaterBound || !Rules.Allowed(def,country))continue;
                var x=focus.X-7+index%3*6;var y=focus.Y-6+index/3*5;
                if(world.Map.FootprintFree(def,x,y) && !world.BuildingAreaHasUnits(x,y,def.Width,def.Height))world.SpawnBuilding(0,item.Type,x,y,foundationCountry:country);
                index++;if(index>=9)break;
            }
        }
        else if(feature=="naval")
        {
            var water=world.Map.OriginalDefinition!.RuntimeCells.Where(c=>c.Playable && c.WaterPassable && c.X>10 && c.Y>10 && c.X<world.Map.Width-10 && c.Y<world.Map.Height-10).OrderBy(c=>Math.Abs(c.X-world.Map.Width/2)+Math.Abs(c.Y-world.Map.Height/2)).ToArray();
            var best=-1;
            foreach(var cell in water)
            {
                var area=0;for(var y=cell.Y-5;y<=cell.Y+5;y++)for(var x=cell.X-7;x<=cell.X+7;x++)if(world.Map.Navigable(x,y,MovementDomain.Water))area++;
                if(area>best){best=area;focus=new Cell(cell.X,cell.Y);if(area==165)break;}
            }
            var index=0;foreach(var type in Enum.GetValues<UnitType>())
            {
                var origin=Rules.Allowed(Rules.Unit(type,country),country)?country:Country.Russia;var def=Rules.Unit(type,origin);
                if(def.Domain!=MovementDomain.Water)continue;
                var unit=world.SpawnUnit(0,type,focus.X-5+index%5*3,focus.Y-3+index/5*4,origin);unit.DisabledUntil=int.MaxValue;index++;
            }
        }
        else if(feature=="occlusion")
        {
            var structure=world.Map.OriginalObjects.First(item=>item.Name=="CANWY26");focus=new Cell(structure.X+structure.Width/2,structure.Y+structure.Height/2);
            var unit=world.SpawnUnit(0,UnitType.Grizzly,structure.X+structure.Width,structure.Y,Country.America);unit.DisabledUntil=int.MaxValue;classicOcclusionUnitId=unit.Id;
            if(unit.CellX!=structure.X+structure.Width || unit.CellY!=structure.Y)throw new InvalidOperationException("Original structure side cell is not a valid tank fixture");
        }
        else if(feature is "city" or "forest")
        {
            var candidates=world.Map.OriginalObjects.Where(p=>feature=="city"?p.Definition.Kind==ClassicMapObjectKind.Structure:p.Definition.Kind==ClassicMapObjectKind.Terrain && p.Name.StartsWith("TREE",StringComparison.Ordinal)).ToArray();
            if(candidates.Length>0)
            {
                var item=feature=="city"?candidates.MaxBy(p=>p.Width*p.Height):candidates.MaxBy(p=>candidates.Count(q=>Math.Abs(p.X-q.X)<7 && Math.Abs(p.Y-q.Y)<7));
                focus=new Cell(item.X+item.Width/2,item.Y+item.Height/2);
            }
        }
        else if(feature=="highbridge")
        {
            var raisedSpan=world.Map.BridgeSpans.FirstOrDefault(s=>world.Map.OriginalCellAt(s.Origin.X+s.SizeX/2,s.Origin.Y+s.SizeY/2)?.HighBridge==true);
            if(raisedSpan.Length>0)focus=new Cell(raisedSpan.Origin.X+raisedSpan.SizeX/2,raisedSpan.Origin.Y+raisedSpan.SizeY/2);
        }
        else if(feature=="bridge" && world.Map.BridgeSpans.FirstOrDefault() is { } bridge)
            focus=new Cell(bridge.Origin.X+bridge.SizeX/2,bridge.Origin.Y+bridge.SizeY/2);
        else if(feature=="cliff")
        {
            var cliff=world.Map.OriginalDefinition!.RuntimeCells.FirstOrDefault(c=>c.Playable && c.Template.Contains("cliff",StringComparison.OrdinalIgnoreCase));
            if(cliff is not null)focus=new Cell(cliff.X,cliff.Y);
        }
        ShowBattle();battlefield!.SetCameraCell(new Vector2(focus.X+.5f,focus.Y+.5f)-Vector2.One*(world.Map.SurfaceHeightFixed(focus.X*Rules.Scale+Rules.Scale/2,focus.Y*Rules.Scale+Rules.Scale/2)/(float)Rules.Scale*.5f));
        battlefield.Selection.Clear();RefreshHud();
        return new(){{"mapId",mapId},{"feature",feature},{"country",country.ToString()},{"width",world.Map.Width},{"height",world.Map.Height},{"sourceSha256",world.Map.OriginalDefinition!.SourceSha256},{"sourceCells",world.Map.OriginalDefinition.RuntimeCells.Length},{"units",world.Units.Count},{"focus",new Vector2(focus.X,focus.Y)}};
    }
    public void RunClassicVisualChecks()=>RunClassicVisualBatch(false);
    public void RunClassicSupplementalVisualChecks()=>RunClassicVisualBatch(true);
    async void RunClassicVisualBatch(bool supplemental)
    {
        if(classicVisualReport.TryGetValue("running",out var running) && running.AsBool())return;
        var captures=new Godot.Collections.Array<Godot.Collections.Dictionary>();var folder=ProjectSettings.GlobalizePath("res://Reports/skirmish-parity/visual-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);classicVisualReport=new(){{"running",true},{"completed",false},{"folder",folder},{"captures",captures},{"oldLongSuitesRun",false}};
        try
        {
            var cases=supplemental?new[]{("mp01t4","city",0),("mp02t2","city",1),("mp23t4","forest",0),("mp23t4","highbridge",0),("mp01t4","naval",0),("mp08t2","buildings",1),("mp08t2","special",0),("mp02t2","occlusion",0)}:new[]{("mp01t4","map",0),("mp02t2","map",1),("mp08t2","map",0),("mp23t4","map",1),("mp25t6","map",0),("mp08t2","roster",0),("mp08t2","roster",1),("mp01t4","naval",0),("mp23t4","bridge",0)};
            foreach(var (map,feature,country) in cases)
            {
                classicVisualReport["phase"]=map+"-"+feature+"-"+country;
                var audit=OpenClassicInspection(map,feature,country);await PointerParityFrame();await PointerParityFrame();await PointerParityAfterDraw();
                if(battlefield!.LandscapeGroundTilesDrawn<=0 || !Graphics.HasOriginalMapTerrain)throw new InvalidOperationException("Official terrain did not reach native rendering");
                if(Graphics.OriginalMapObjectTypeCount!=91 || localWorld!.Map.UnmappedOriginalObjects>0 || battlefield.OriginalObjectDrawFailures>0 || (feature is "city" or "forest") && battlefield.OriginalObjectSpritesDrawn==0)throw new InvalidOperationException("Original map objects did not reach native rendering");
                if(feature=="occlusion")
                {
                    var ordered=battlefield.EntityDrawsAfterOriginalStructure(classicOcclusionUnitId,"CANWY26");audit["eastSideTankDrawsInFront"]=ordered;audit["surfaceConstraints"]=battlefield.BuildingDepthConstraintCount;audit["surfaceFallbacks"]=battlefield.BuildingDepthFallbacks;
                    if(!ordered)throw new InvalidOperationException("Neutral building overdraws its east-side tank");
                }
                if(Graphics.OriginalMapRockTypeCount!=10 || battlefield.OriginalRockDrawFailures>0)throw new InvalidOperationException("Original rock overlays did not reach native rendering");
                using var image=GetViewport().GetTexture().GetImage();var file=Path.Combine(folder,map+"-"+feature+"-"+country+".png");var bluePixels=CountOpaqueTerrainBluePixels(image);
                if(feature=="forest" && bluePixels>0)throw new InvalidOperationException("Opaque original TMP transparent-blue pixels remain: "+bluePixels);
                var result=image.SavePng(file);if(result!=Error.Ok)throw new IOException("Native capture: "+result);
                audit["png"]=file;audit["widthPixels"]=image.GetWidth();audit["heightPixels"]=image.GetHeight();audit["renderedFrame"]=Engine.GetFramesDrawn();audit["groundTilesDrawn"]=battlefield.LandscapeGroundTilesDrawn;audit["bridgePiecesDrawn"]=battlefield.OriginalBridgePiecesDrawn;audit["cliffsDrawn"]=battlefield.LandscapeCliffFacesDrawn;audit["mapObjectTypes"]=Graphics.OriginalMapObjectTypeCount;audit["mapObjects"]=localWorld!.Map.OriginalObjects.Count;audit["visibleMapObjects"]=battlefield.VisibleOriginalObjects;audit["mapObjectSpritesDrawn"]=battlefield.OriginalObjectSpritesDrawn;audit["mapObjectDrawFailures"]=battlefield.OriginalObjectDrawFailures;audit["rockOverlayTypes"]=Graphics.OriginalMapRockTypeCount;audit["rockOverlaySpritesDrawn"]=battlefield.OriginalRockSpritesDrawn;audit["rockOverlayDrawFailures"]=battlefield.OriginalRockDrawFailures;audit["opaqueTerrainBluePixels"]=bluePixels;
                captures.Add(audit);File.WriteAllText(Path.ChangeExtension(file,".json"),Godot.Json.Stringify(audit,"\t"));
            }
            classicVisualReport["passed"]=true;
        }
        catch(Exception error){classicVisualReport["passed"]=false;classicVisualReport["error"]=error.ToString();GD.PushError("Classic native checks: "+error.Message);}
        finally
        {
            classicVisualReport["running"]=false;classicVisualReport["completed"]=true;classicVisualReport["captureCount"]=captures.Count;
            File.WriteAllText(Path.Combine(folder,"report.json"),Godot.Json.Stringify(classicVisualReport,"\t"));
        }
    }
}
