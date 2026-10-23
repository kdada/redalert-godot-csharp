using Godot;
using RedAlert.Simulation;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RedAlert;

public partial class Main
{
    bool classicShroudChecksRunning;
    Godot.Collections.Dictionary classicShroudReport=new();
    public Godot.Collections.Dictionary GetClassicShroudAudit()=>classicShroudReport;

    static void ClassicShroudRequire(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
    static MatchSettings ClassicShroudSettings()=>new(20261018,
        [new("Shroud scout",Country.America,0),new("Gap fixture",Country.America,1),new("Gap ally fixture",Country.America,1)],
        ClassicStart:true,StartingUnits:0,ShortGame:false,Crates:false,SuperWeapons:false);
    static Godot.Collections.Dictionary ClassicShroudCell(World world,int player,Cell cell)=>new()
    {
        ["x"]=cell.X,["y"]=cell.Y,["player"]=player,["bits"]=(int)world.Players[player].Visibility[world.Map.Index(cell.X,cell.Y)],
        ["explored"]=world.Explored(player,cell.X,cell.Y),["visible"]=world.Visible(player,cell.X,cell.Y),
        ["directSight"]=world.ClassicDirectSight(player,cell.X,cell.Y),["satelliteActive"]=world.ClassicSpySatelliteActive(player)
    };
    static Building ClassicShroudBuilding(World world,int owner,BuildingType type,Cell near,Func<Cell,bool>? allow=null)
    {
        var definition=Rules.Building(type,Country.America);Cell? best=null;var distance=int.MaxValue;
        for(var y=0;y<world.Map.Height;y++)for(var x=0;x<world.Map.Width;x++)
        {
            var cell=new Cell(x,y);var d=Math.Abs(x-near.X)+Math.Abs(y-near.Y);
            if(d>=distance || allow is not null && !allow(cell) || !world.Map.FootprintFree(definition,x,y)
                || world.BuildingAreaHasUnits(x,y,definition.Width,definition.Height))continue;
            best=cell;distance=d;
        }
        ClassicShroudRequire(best is not null,"No legal unchanged-terrain fixture footprint for "+type);
        return world.SpawnBuilding(owner,type,best!.Value.X,best.Value.Y,foundationCountry:Country.America);
    }
    static Godot.Collections.Dictionary ClassicShroudRadarSample(MiniMap radar,Cell cell)
    {
        var bounds=radar.RadarBounds;var width=Math.Clamp((int)Math.Ceiling(bounds.Size.X),1,256);
        var height=Math.Clamp((int)Math.Ceiling(bounds.Size.Y),1,256);var pixels=radar.CopyTerrainPixels();
        ClassicShroudRequire(pixels.Length==width*height*4,"Radar terrain raster is stale or has unexpected dimensions");
        var point=(radar.MapToRadar(new Vector2(cell.X+.5f,cell.Y+.5f))-bounds.Position)/bounds.Size;
        var x=Math.Clamp((int)(point.X*width),0,width-1);var y=Math.Clamp((int)(point.Y*height),0,height-1);var index=(y*width+x)*4;
        return new(){["pixelX"]=x,["pixelY"]=y,["width"]=width,["height"]=height,
            ["r"]=(int)pixels[index],["g"]=(int)pixels[index+1],["b"]=(int)pixels[index+2],["a"]=(int)pixels[index+3]};
    }
    double ClassicShroudPatchBrightness(Image image,Battlefield view,Cell cell)
    {
        var point=view.GetGlobalTransformWithCanvas()*view.ProjectGround(new Vector2(cell.X+.5f,cell.Y+.5f));
        var viewport=GetViewport().GetVisibleRect().Size;var x=(int)(point.X*image.GetWidth()/viewport.X);var y=(int)(point.Y*image.GetHeight()/viewport.Y);
        ClassicShroudRequire(view.GetGlobalRect().HasPoint(point) && x>=5 && y>=4 && x+5<image.GetWidth() && y+4<image.GetHeight(),"GPU terrain probe is outside the rendered battlefield");
        double total=0;var count=0;
        for(var dy=-3;dy<=3;dy++)for(var dx=-4;dx<=4;dx++)
        {var color=image.GetPixel(x+dx,y+dy);total+=(color.R+color.G+color.B)*255/3;count++;}
        return total/count;
    }

    // Explicit native operator for task37. Core commands/pathfinding are real;
    // spawned support structures, damage-to-power and reveal-crate state are
    // declared fixtures. This never calls the older suites or an inspection mode.
    public async void RunClassicShroudChecks()
    {
        if(classicShroudChecksRunning)return;classicShroudChecksRunning=true;
        var savedProcessing=IsProcessing();var savedFast=fast;var savedEdges=clientPreferences.EdgeScrolling;
        var clock=Stopwatch.StartNew();var error="";var folder="";var totalSteps=0;var commandSequence=0;
        const int maximumSteps=2000,maximumMoveSteps=400,maximumSeconds=90;
        var checks=new Godot.Collections.Array<string>();var errors=new Godot.Collections.Array<string>();
        var captures=new Godot.Collections.Array<Godot.Collections.Dictionary>();var cases=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var required=new[]{"classic-start","original-shroud-edges","scout-path-out","scout-path-back","explored-bright-outside-los",
            "ordinary-enemy-visible","attack-beyond-own-sight","gap-erases-exploration","gap-own-and-allies","gap-direct-scout",
            "gap-regrows","gap-off-no-memory-restore","satellite-gap-priority","satellite-low-power","satellite-sell-reset",
            "satellite-destroy-reset","one-shot-reveal","submarine-hidden","destroyer-detects","native-captures","menu-cleanup"};
        classicShroudReport=new()
        {
            ["running"]=true,["completed"]=false,["passed"]=false,["phase"]="starting",["checks"]=checks,["errors"]=errors,["cases"]=cases,["captures"]=captures,
            ["fixtureSpawns"]=true,["productionTested"]=false,["terrainWrites"]=0,["visibilityByteWrites"]=0,["oldLongSuitesRun"]=false,["aiCommands"]=false,
            ["maximumWorldSteps"]=maximumSteps,["maximumMoveSteps"]=maximumMoveSteps,["maximumSeconds"]=maximumSeconds,
            ["captureSource"]="actual Main Viewport.GetTexture().GetImage after QueueRedraw, PointerParityFrame and PointerParityAfterDraw",
            ["nativeLowPowerParityProven"]=false,["nativePaletteAlphaProven"]=false,["terrainInspectionUsed"]=false,
            ["fixtureNotes"]="Generated practice terrain is unchanged. SpawnUnit/SpawnBuilding bypass production. Reactor Health=1 supplies the low-power fixture; satellite Health=0 supplies the destruction fixture. Crates=false: RevealClassicMap plus CrateMapRevealed=true declares the one-shot reward; no pickup is claimed."
        };
        void Budget()
        {if(clock.Elapsed.TotalSeconds>maximumSeconds)throw new TimeoutException("Classic shroud checks exceeded the 90-second budget");}
        async Task Frame()
        {Budget();await PointerParityFrame().WaitAsync(TimeSpan.FromSeconds(Math.Max(.1,maximumSeconds-clock.Elapsed.TotalSeconds)));Budget();}
        void Mark(string name){if(!checks.Contains(name))checks.Add(name);classicShroudReport["checkCount"]=checks.Count;}
        void Step(World world,GameCommand? command=null)
        {
            Budget();if(totalSteps>=maximumSteps)throw new TimeoutException("Classic shroud checks exceeded the Core step cap");
            if(command is null)world.Step();
            else
            {
                var accepted=false;var applied=command with{Tick=world.Tick,Sequence=++commandSequence};
                world.Step([applied],(_,ok)=>accepted=ok);ClassicShroudRequire(accepted,"Core rejected "+command.Type+" in "+classicShroudReport["phase"].AsString());
            }
            totalSteps++;
        }
        void VisibilityTick(World world){for(var i=0;i<6;i++)Step(world);}
        async Task Move(World world,Unit scout,Cell goal)
        {
            var paths=world.PathSearches;Step(world,new(){Type=CommandType.Move,Player=scout.Owner,Actors=[scout.Id],X=goal.X,Y=goal.Y});
            var ticks=1;
            while(scout.CellX!=goal.X || scout.CellY!=goal.Y || scout.Order==UnitOrder.Move)
            {
                if(ticks++>=maximumMoveSteps)throw new TimeoutException("Scout Move did not reach "+goal+" within 400 Core ticks");
                Step(world);if(ticks%32==0)await Frame();
            }
            VisibilityTick(world);ClassicShroudRequire(world.PathSearches>paths,"Scout movement did not use actual Core pathfinding");
            cases.Add(new(){["kind"]="Core Move",["actor"]=scout.Id,["x"]=goal.X,["y"]=goal.Y,["ticks"]=ticks,["pathSearches"]=world.PathSearches-paths});
        }
        async Task<Godot.Collections.Dictionary> Capture(string tag,World world,Cell probe)
        {
            Budget();var view=battlefield??throw new InvalidOperationException("Actual Battlefield is missing");
            var radar=minimap??throw new InvalidOperationException("Actual MiniMap is missing");var before=view.DrawCalls;
            ClassicShroudRequire(!IsProcessing() && !terrainInspection && !terrainInspectionMotion && ReferenceEquals(Battle,world),"Capture requires the paused Main and its actual non-inspection world");
            RefreshHud();radar.InvalidateTerrain();view.QueueRedraw();await Frame();await PointerParityAfterDraw();Budget();
            ClassicShroudRequire(view.DrawCalls>before,"Battlefield did not draw a new native shroud frame");
            var render=view.GetClassicShroudRenderAudit();
            ClassicShroudRequire(render["sourceFrames"].AsInt32()==96 && render["storedRasterFrames"].AsInt32()==48 && render["textureLoaded"].AsBool(),"Original 96-source/48-raster shroud texture is unavailable");
            ClassicShroudRequire(render["knownTerrainDarkened"].AsInt32()==0 && !render["fogLayerDrawn"].AsBool(),"Known terrain was darkened or a fog layer was drawn");
            using var image=GetViewport().GetTexture().GetImage();
            ClassicShroudRequire(image.GetWidth()>0 && image.GetHeight()>0,"Actual native viewport image is empty");
            var png=Path.Combine(folder,tag+".png");ClassicShroudRequire(image.SavePng(png)==Error.Ok,"Native shroud PNG save failed");
            var capture=new Godot.Collections.Dictionary
            {
                ["tag"]=tag,["png"]=png,["nativeViewport"]=true,["renderedFrame"]=Engine.GetFramesDrawn(),["width"]=image.GetWidth(),["height"]=image.GetHeight(),
                ["tick"]=world.Tick,["camera"]=view.CameraCell,["cell"]=ClassicShroudCell(world,0,probe),["mask"]=view.OriginalShroudMaskAt(probe.X,probe.Y),
                ["render"]=render,["panelNoLayer"]=render["knownTerrainDarkened"].AsInt32()==0 && !render["fogLayerDrawn"].AsBool(),
                ["radar"]=ClassicShroudRadarSample(radar,probe),["terrainPatchBrightness"]=ClassicShroudPatchBrightness(image,view,probe)
            };
            captures.Add(capture);File.WriteAllText(Path.Combine(folder,tag+".json"),Godot.Json.Stringify(capture,"\t"));return capture;
        }
        async Task CpuCase(string phase,Func<Task> run)
        {
            classicShroudReport["phase"]=phase;
            try{Budget();await run();}
            catch(Exception exception){errors.Add(phase+": "+exception);}
        }
        try
        {
            ClassicShroudRequire(!headless && DisplayServer.GetName()!="headless","Classic shroud checks require the real native GPU viewport");
            ClassicShroudRequire(!pointerParityChecksRunning && !terrainDestructionChecksRunning && !vehicleSlopeChecksRunning && !directionDeployChecksRunning && !oreVisibilityChecksRunning,
                "Another native checker is running");
            folder=ProjectSettings.GlobalizePath("res://Reports/shroud-parity-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Godot.Time.GetTicksMsec());
            Directory.CreateDirectory(folder);classicShroudReport["folder"]=folder;
            ReturnToDeployment();SetProcess(true);fast=false;clientPreferences.EdgeScrolling=false;await Frame();await Frame();
            fullPlaythroughControlsEnemy=true;StartPractice(Country.America,ClassicShroudSettings());
            while(Battle is null || battlefield is null || preparingLocalWorld is not null)await Frame();
            var world=localWorld??throw new InvalidOperationException("StartPractice did not produce a local world");
            var view=battlefield??throw new InvalidOperationException("StartPractice did not produce a Battlefield");
            var radar=minimap??throw new InvalidOperationException("StartPractice did not produce a MiniMap");
            SetProcess(false);localPending.Clear();
            ClassicShroudRequire(!terrainInspection && !terrainInspectionMotion && radar.IsProcessing(),"Main freeze must leave MiniMap processing enabled and inspection disabled");
            ClassicShroudRequire(world.Settings.ClassicStart && world.Settings.StartingUnits==0 && !world.Settings.Crates && world.Buildings.Count==0,"Expected the actual MCV-only classic start");
            var scout=world.Units.Single(unit=>unit.Owner==0 && unit.Type==UnitType.Mcv);var home=new Cell(scout.CellX,scout.CellY);
            var visited=world.NearestFree(home.X+15,home.Y,true)??throw new InvalidOperationException("No legal scout destination");
            var probe=new Cell(visited.X-2,visited.Y);ClassicShroudRequire(world.Map.IsPlayable(probe.X,probe.Y) && !world.Explored(0,probe.X,probe.Y),"Scout probe was already explored at classic start");
            classicShroudReport["startTick"]=world.Tick;classicShroudReport["mainProcessingDuringChecks"]=IsProcessing();
            classicShroudReport["mapId"]=world.Settings.MapId;classicShroudReport["terrainSource"]="unchanged generated practice map";Mark("classic-start");
            view.SetCameraCell(new Vector2(home.X+9.5f,home.Y+.5f));
            classicShroudReport["phase"]="original-shrouded-edges";var initial=await Capture("01-original-shrouded-edges",world,probe);
            var initialRender=initial["render"].AsGodotDictionary();
            ClassicShroudRequire(initialRender["edgeMasks"].AsInt32()>0 && initialRender["fullMasks"].AsInt32()>0,"Initial GPU frame must contain original shroud edges and fully shrouded cells");Mark("original-shroud-edges");
            classicShroudReport["phase"]="scout-out";await Move(world,scout,visited);Mark("scout-path-out");
            ClassicShroudRequire(world.ClassicDirectSight(0,probe.X,probe.Y),"Scout did not physically reveal the visited probe");
            var arrived=await Capture("02-scout-physical-sight",world,probe);
            classicShroudReport["phase"]="scout-return";await Move(world,scout,home);Mark("scout-path-back");
            ClassicShroudRequire(world.Explored(0,probe.X,probe.Y) && world.Visible(0,probe.X,probe.Y) && !world.ClassicDirectSight(0,probe.X,probe.Y),"Departed scout cell must remain explored and visible beyond current physical sight");
            var departed=await Capture("03-explored-after-depart",world,probe);
            ClassicShroudRequire(departed["mask"].AsInt32()==0 && departed["terrainPatchBrightness"].AsDouble()>initial["terrainPatchBrightness"].AsDouble()+8,"GPU departed terrain did not stay bright after uncovering");
            var arrivalRadar=arrived["radar"].AsGodotDictionary();var departureRadar=departed["radar"].AsGodotDictionary();
            ClassicShroudRequire(new[]{"r","g","b","a"}.All(channel=>arrivalRadar[channel].AsInt32()==departureRadar[channel].AsInt32()) && departureRadar["r"].AsInt32()+departureRadar["g"].AsInt32()+departureRadar["b"].AsInt32()>40,"Radar dimmed naturally explored terrain after departure");Mark("explored-bright-outside-los");

            classicShroudReport["phase"]="ordinary-enemy-and-long-range-order";
            var enemy=world.SpawnUnit(1,UnitType.Mcv,visited.X,visited.Y);var launcher=world.SpawnUnit(0,UnitType.V3Launcher,home.X+2,home.Y,Country.Russia);
            enemy.DisabledUntil=int.MaxValue;VisibilityTick(world);
            ClassicShroudRequire(world.Visible(0,enemy.CellX,enemy.CellY) && !world.ClassicDirectSight(0,enemy.CellX,enemy.CellY) && world.ClassicUnitVisibleTo(0,enemy),"Ordinary enemy in explored ground disappeared outside current LOS");Mark("ordinary-enemy-visible");
            var range=launcher.Definition.PrimaryWeapon?.RangeFixed??0;var distance=World.DistanceSquared(launcher,enemy);
            ClassicShroudRequire(distance>(long)launcher.Sight*launcher.Sight*Rules.Scale*Rules.Scale && distance<=(long)range*range && world.CanTarget(launcher,enemy),"V3 fixture is not a legal target in weapon range beyond its own sight");
            Step(world,new(){Type=CommandType.Attack,Player=0,Actors=[launcher.Id],Target=enemy.Id});
            ClassicShroudRequire(launcher.TargetId==enemy.Id && launcher.Order==UnitOrder.Attack,"Actual Attack command failed beyond the launcher's own sight");
            cases.Add(new(){["kind"]="V3 Attack command",["fixtureSpawns"]=true,["attacker"]=launcher.Id,["target"]=enemy.Id,["sight"]=launcher.Sight,["rangeFixed"]=range,["distanceFixed"]=World.IntegerSqrt(distance),["targetOutsideAllPhysicalLOS"]=!world.ClassicDirectSight(0,enemy.CellX,enemy.CellY),["fired"]=launcher.LastFiredTick>=world.Tick-1});
            Step(world,new(){Type=CommandType.Stop,Player=0,Actors=[launcher.Id]});launcher.DisabledUntil=int.MaxValue;Mark("attack-beyond-own-sight");

            classicShroudReport["phase"]="one-shot-reveal-and-gap-hole";
            world.Players[0].CrateMapRevealed=true;world.RevealClassicMap(0);
            ClassicShroudRequire(!world.ClassicSpySatelliteActive(0),"Declared one-shot reveal incorrectly activated a satellite");
            var power=ClassicShroudBuilding(world,1,BuildingType.NuclearReactor,new(70,47));
            var gap=ClassicShroudBuilding(world,1,BuildingType.GapGenerator,new(visited.X+5,visited.Y),cell=>
                (cell.X-probe.X)*(cell.X-probe.X)+(cell.Y-probe.Y)*(cell.Y-probe.Y) is >25 and <=100);
            VisibilityTick(world);world.RevealClassicMap(1);world.RevealClassicMap(2);
            ClassicShroudRequire(world.Players[1].Powered && !world.Explored(0,probe.X,probe.Y) && !world.Visible(0,probe.X,probe.Y) && !world.ClassicDirectSight(0,probe.X,probe.Y),"Powered enemy Gap did not cover previously explored terrain outside physical LOS");
            var rim=new Cell(gap.CellX+10,gap.CellY);var beyondRim=new Cell(gap.CellX+11,gap.CellY);
            ClassicShroudRequire(world.Map.IsPlayable(rim.X,rim.Y) && world.Map.IsPlayable(beyondRim.X,beyondRim.Y)
                && !world.ClassicDirectSight(0,rim.X,rim.Y) && !world.Explored(0,rim.X,rim.Y) && world.Explored(0,beyondRim.X,beyondRim.Y),"Gap radius must include ten cells and leave eleven-cell exploration uncovered");
            cases.Add(new(){["kind"]="Gap radius boundary",["radius10"]=ClassicShroudCell(world,0,rim),["radius11"]=ClassicShroudCell(world,0,beyondRim)});Mark("gap-erases-exploration");
            Cell? alliedProbe=null;
            for(var dy=-10;dy<=10 && alliedProbe is null;dy++)for(var dx=-10;dx<=10;dx++)
            {
                var cell=new Cell(gap.CellX+dx,gap.CellY+dy);
                if(dx*dx+dy*dy is >25 and <=100 && world.Map.IsPlayable(cell.X,cell.Y)
                    && !world.ClassicDirectSight(1,cell.X,cell.Y) && !world.ClassicDirectSight(2,cell.X,cell.Y)){alliedProbe=cell;break;}
            }
            ClassicShroudRequire(alliedProbe is not null,"No Gap owner/ally probe outside their physical sight");var friendly=alliedProbe!.Value;
            ClassicShroudRequire(world.Allied(1,2) && world.Explored(1,friendly.X,friendly.Y) && world.Explored(2,friendly.X,friendly.Y),"Gap blocked exploration belonging to its owner or ally");
            cases.Add(new(){["kind"]="Gap owner and ally",["ownerCell"]=ClassicShroudCell(world,1,friendly),["allyCell"]=ClassicShroudCell(world,2,friendly)});Mark("gap-own-and-allies");
            view.SetCameraCell(new Vector2(probe.X+4.5f,probe.Y+.5f));var hole=await Capture("04-gap-hole-after-one-shot-reveal",world,probe);
            ClassicShroudRequire(hole["mask"].AsInt32()==255,"Gap hole must render a fully shrouded probe");
            classicShroudReport["gapFixture"]=new Godot.Collections.Dictionary{["generator"]=gap.Id,["x"]=gap.CellX,["y"]=gap.CellY,["reactor"]=power.Id,["radiusCells"]=10,["probe"]=ClassicShroudCell(world,0,probe)};
            var gapScoutDestination=world.NearestFree(probe.X,probe.Y,true)??throw new InvalidOperationException("No legal direct-scout destination inside the Gap");
            await Move(world,scout,gapScoutDestination);
            ClassicShroudRequire(world.ClassicDirectSight(0,probe.X,probe.Y) && world.Explored(0,probe.X,probe.Y),"Direct scout sight could not uncover the Gap");Mark("gap-direct-scout");
            await Capture("05-direct-scout-inside-gap",world,probe);await Move(world,scout,home);
            ClassicShroudRequire(!world.Explored(0,probe.X,probe.Y) && !world.ClassicDirectSight(0,probe.X,probe.Y),"Gap failed to regrow when the real scout departed");Mark("gap-regrows");
            power.Health=1;VisibilityTick(world);
            ClassicShroudRequire(!world.Players[1].Powered && !world.Explored(0,probe.X,probe.Y),"Unpowered Gap restored erased old exploration without a new reveal");Mark("gap-off-no-memory-restore");
            ClassicShroudRequire(world.Players[0].CrateMapRevealed && !world.ClassicSpySatelliteActive(0) && !world.Explored(0,probe.X,probe.Y),"One-shot reveal became a permanent map revealer or Gap suppressor");Mark("one-shot-reveal");
            ClassicShroudRequire(captures.Count>=4 && captures.All(capture=>capture["nativeViewport"].AsBool() && capture["panelNoLayer"].AsBool()),"Required actual GPU capture states are missing");Mark("native-captures");

            await CpuCase("satellite-low-power-sell-destroy",async()=>
            {
                var satWorld=new World(ClassicShroudSettings());var satScout=satWorld.Units.Single(unit=>unit.Owner==0);
                var natural=new Cell(satScout.CellX,satScout.CellY);var destination=satWorld.NearestFree(natural.X+15,natural.Y,true)??throw new InvalidOperationException("No satellite scout route");
                await Move(satWorld,satScout,destination);
                var reactor=ClassicShroudBuilding(satWorld,0,BuildingType.NuclearReactor,new(50,65));
                var satellite=ClassicShroudBuilding(satWorld,0,BuildingType.SpySatellite,new(55,65));VisibilityTick(satWorld);
                Cell? satelliteOnly=null;
                for(var y=0;y<satWorld.Map.Height && satelliteOnly is null;y++)for(var x=0;x<satWorld.Map.Width;x++)
                    if(satWorld.Map.IsPlayable(x,y) && (satWorld.Players[0].Visibility[satWorld.Map.Index(x,y)]&7)==4){satelliteOnly=new(x,y);break;}
                ClassicShroudRequire(satelliteOnly is not null && satWorld.ClassicSpySatelliteActive(0) && !satWorld.ClassicDirectSight(0,natural.X,natural.Y),"Satellite fixtures need current satellite-only and departed natural cells");
                var remote=satelliteOnly!.Value;var powered=ClassicShroudCell(satWorld,0,remote);reactor.Health=1;VisibilityTick(satWorld);
                ClassicShroudRequire(!satWorld.Players[0].Powered && !satWorld.ClassicSpySatelliteActive(0) && satWorld.Visible(0,natural.X,natural.Y)
                    && !satWorld.Visible(0,remote.X,remote.Y) && (satWorld.Players[0].Visibility[satWorld.Map.Index(remote.X,remote.Y)]&12)==8,"Satellite low power did not withdraw only current coverage while preserving natural knowledge and provenance");
                cases.Add(new(){["kind"]="satellite low power",["fixtureSpawns"]=true,["nativeLowPowerParityProven"]=false,["poweredRemote"]=powered,["lowPowerRemote"]=ClassicShroudCell(satWorld,0,remote),["naturalPreserved"]=ClassicShroudCell(satWorld,0,natural)});Mark("satellite-low-power");
                reactor.Health=reactor.MaxHealth;VisibilityTick(satWorld);
                var enemyPower=ClassicShroudBuilding(satWorld,1,BuildingType.NuclearReactor,new(70,47));
                var enemyGap=ClassicShroudBuilding(satWorld,1,BuildingType.GapGenerator,new(45,47),cell=>
                    (cell.X-destination.X)*(cell.X-destination.X)+(cell.Y-destination.Y)*(cell.Y-destination.Y)>256);
                VisibilityTick(satWorld);var gapCell=new Cell(enemyGap.CellX,enemyGap.CellY);
                ClassicShroudRequire(satWorld.ClassicSpySatelliteActive(0) && satWorld.Players[1].Powered && !satWorld.ClassicDirectSight(0,gapCell.X,gapCell.Y)
                    && !satWorld.Explored(0,gapCell.X,gapCell.Y) && (satWorld.Players[0].Visibility[satWorld.Map.Index(gapCell.X,gapCell.Y)]&12)==8,"Powered satellite defeated enemy Gap priority");
                cases.Add(new(){["kind"]="powered satellite versus powered Gap",["satellite"]=satellite.Id,["gap"]=enemyGap.Id,["reactor"]=enemyPower.Id,["cell"]=ClassicShroudCell(satWorld,0,gapCell)});Mark("satellite-gap-priority");
                Step(satWorld,new(){Type=CommandType.Sell,Player=0,Actors=[satellite.Id]});VisibilityTick(satWorld);
                ClassicShroudRequire(satWorld.Find(satellite.Id) is null && !satWorld.ClassicSpySatelliteActive(0) && !satWorld.Explored(0,natural.X,natural.Y)
                    && satWorld.Players[0].Visibility.All(value=>(value&8)==0),"Selling the last satellite did not reset natural and satellite memory outside current LOS");Mark("satellite-sell-reset");
                satWorld.RevealClassicMap(0);var replacement=ClassicShroudBuilding(satWorld,0,BuildingType.SpySatellite,new(satellite.CellLeft,satellite.CellTop));VisibilityTick(satWorld);
                ClassicShroudRequire(satWorld.Explored(0,natural.X,natural.Y) && satWorld.ClassicSpySatelliteActive(0),"Replacement satellite fixture did not re-establish resettable knowledge");
                replacement.Health=0;VisibilityTick(satWorld);
                ClassicShroudRequire(satWorld.Find(replacement.Id) is null && !satWorld.Explored(0,natural.X,natural.Y) && satWorld.Players[0].Visibility.All(value=>(value&8)==0),"Destroying the last satellite did not reset natural and satellite memory");
                cases.Add(new(){["kind"]="last satellite destruction",["destructionFixtureHealthZero"]=true,["productionTested"]=false,["cellAfterReset"]=ClassicShroudCell(satWorld,0,natural)});Mark("satellite-destroy-reset");
            });
            await CpuCase("globally-revealed-submarine-detection",()=>
            {
                var waterWorld=new World(ClassicShroudSettings());Cell? water=null;
                bool HasWaterPair(World candidate)
                {
                    for(var y=0;y<candidate.Map.Height;y++)for(var x=0;x<candidate.Map.Width-1;x++)
                        if(candidate.Map.Navigable(x,y,MovementDomain.Water) && candidate.Map.Navigable(x+1,y,MovementDomain.Water)
                            && !candidate.Units.Any(unit=>unit.CellY==y && (unit.CellX==x || unit.CellX==x+1))){water=new(x,y);return true;}
                    return false;
                }
                if(!HasWaterPair(waterWorld))
                {
                    var catalog=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath));
                    foreach(var info in catalog.Maps.Where(info=>info.NavalSuitable).OrderByDescending(info=>info.WaterFraction))
                    {
                        var settings=ClassicShroudSettings() with{Players=ClassicShroudSettings().Players.Take(2).ToArray(),Width=info.Width,Height=info.Height,MapId=info.Id};
                        waterWorld=new World(settings);if(HasWaterPair(waterWorld))break;Budget();
                    }
                }
                ClassicShroudRequire(water is not null,"No playable adjacent navigable water cells exist for the unchanged-terrain submarine fixture");
                var source=water!.Value;var submarine=waterWorld.SpawnUnit(1,UnitType.AttackSubmarine,source.X,source.Y,Country.Russia);submarine.DisabledUntil=int.MaxValue;
                waterWorld.RevealClassicMap(0);waterWorld.Players[0].CrateMapRevealed=true;
                ClassicShroudRequire(submarine.Definition.Submersible && waterWorld.Visible(0,submarine.CellX,submarine.CellY) && !waterWorld.ClassicUnitVisibleTo(0,submarine),"Globally revealed terrain exposed a submerged submarine without a detector");Mark("submarine-hidden");
                var detector=waterWorld.SpawnUnit(0,UnitType.Destroyer,source.X+1,source.Y,Country.America);detector.DisabledUntil=int.MaxValue;
                ClassicShroudRequire(World.DistanceSquared(detector,submarine)<=64L*Rules.Scale*Rules.Scale && waterWorld.ClassicUnitVisibleTo(0,submarine) && waterWorld.CanTarget(detector,submarine),"Nearby Destroyer sensors did not reveal and target the submarine");
                cases.Add(new(){["kind"]="submarine detection",["mapId"]=waterWorld.Settings.MapId,["fixtureSpawns"]=true,["submarine"]=submarine.Id,["destroyer"]=detector.Id,["waterX"]=submarine.CellX,["waterY"]=submarine.CellY,["globallyRevealed"]=true});Mark("destroyer-detects");return Task.CompletedTask;
            });
        }
        catch(Exception exception){error=exception.ToString();GD.PushError("Classic shroud checks: "+error);}
        finally
        {
            classicShroudReport["phase"]="returning-to-front-menu";
            try
            {
                fast=savedFast;clientPreferences.EdgeScrolling=savedEdges;ReturnToDeployment();SetProcess(savedProcessing);
                await PointerParityFrame().WaitAsync(TimeSpan.FromSeconds(8));await PointerParityFrame().WaitAsync(TimeSpan.FromSeconds(8));
                ClassicShroudRequire(Battle is null && !terrainInspection && !terrainInspectionMotion && IsProcessing()==savedProcessing,"Shroud checker left a frozen battle or inspection active");
                classicShroudReport["returnedToDeployment"]=true;classicShroudReport["processingRestored"]=IsProcessing()==savedProcessing;Mark("menu-cleanup");
            }
            catch(Exception cleanupError){error+=(error.Length==0?"":"\n")+"Menu cleanup: "+cleanupError;classicShroudReport["returnedToDeployment"]=false;SetProcess(savedProcessing);}
            var missing=new Godot.Collections.Array<string>();foreach(var condition in required)if(!checks.Contains(condition))missing.Add(condition);
            classicShroudReport["running"]=false;classicShroudReport["completed"]=true;classicShroudReport["phase"]="complete";
            classicShroudReport["error"]=error;classicShroudReport["missingConditions"]=missing;classicShroudReport["checkCount"]=checks.Count;classicShroudReport["captureCount"]=captures.Count;
            classicShroudReport["worldSteps"]=totalSteps;classicShroudReport["elapsedSeconds"]=clock.Elapsed.TotalSeconds;
            classicShroudReport["passed"]=error.Length==0 && errors.Count==0 && missing.Count==0 && captures.Count>=4;
            try{if(folder.Length>0)File.WriteAllText(Path.Combine(folder,"acceptance.json"),Godot.Json.Stringify(classicShroudReport,"\t"));}
            catch(Exception writeError){classicShroudReport["passed"]=false;classicShroudReport["reportWriteError"]=writeError.ToString();GD.PushError("Classic shroud acceptance report: "+writeError.Message);}
            classicShroudChecksRunning=false;
        }
    }
}
