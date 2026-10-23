using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    Godot.Collections.Dictionary oreVisibilityReport=new();
    bool oreVisibilityChecksRunning;
    public Godot.Collections.Dictionary GetOreVisibilityAudit()
    {
        if(battlefield is {} view)
        {
            oreVisibilityReport["oreTilesDrawn"]=view.OreTilesDrawn;oreVisibilityReport["gemTilesDrawn"]=view.GemTilesDrawn;
            oreVisibilityReport["nativeSpritesDrawn"]=view.OreSpriteTilesDrawn;oreVisibilityReport["fallbacks"]=view.OreFallbackTilesDrawn;
            oreVisibilityReport["sourceSpriteCount"]=Graphics.OriginalResourceSpriteCount;
        }
        return oreVisibilityReport;
    }
    static void OreVisibilityRequire(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
    int OreVisibilityPixelDifference(byte[] before,byte[] after,int width,int height,Rect2 rectangle)
    {
        var viewport=GetViewport().GetVisibleRect().Size;var sx=width/viewport.X;var sy=height/viewport.Y;
        var left=Math.Clamp((int)Math.Floor(rectangle.Position.X*sx),0,width);var right=Math.Clamp((int)Math.Ceiling(rectangle.End.X*sx),0,width);
        var top=Math.Clamp((int)Math.Floor(rectangle.Position.Y*sy),0,height);var bottom=Math.Clamp((int)Math.Ceiling(rectangle.End.Y*sy),0,height);var changed=0;
        for(var y=top;y<bottom;y++)for(var x=left;x<right;x++)
        {var i=(y*width+x)*4;if(before[i]!=after[i] || before[i+1]!=after[i+1] || before[i+2]!=after[i+2])changed++;}
        return changed;
    }
    public async void RunOreVisibilityChecks()
    {
        if(oreVisibilityChecksRunning)return;oreVisibilityChecksRunning=true;
        var folder=ProjectSettings.GlobalizePath("res://Reports/skirmish-parity/ore-visibility-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
        oreVisibilityReport=new(){["running"]=true,["completed"]=false,["folder"]=folder};var captures=new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var previousTooltipDelay=ProjectSettings.GetSetting("gui/timers/tooltip_delay_sec",.5).AsDouble();
        async Task Frame(){battlefield?.QueueRedraw();await PointerParityFrame();await PointerParityAfterDraw();}
        async Task<(byte[] Pixels,int Width,int Height)> Capture(string name,Godot.Collections.Dictionary audit)
        {
            await Frame();using var image=GetViewport().GetTexture().GetImage();image.Convert(Image.Format.Rgba8);
            var path=Path.Combine(folder,name+".png");if(image.SavePng(path)!=Error.Ok)throw new IOException("Ore native PNG save failed");
            audit["png"]=path;audit["renderedFrame"]=Engine.GetFramesDrawn();captures.Add(audit);
            return(image.GetData(),image.GetWidth(),image.GetHeight());
        }
        try
        {
            OpenClassicInspection("mp01t4","map",0);
            // Discard the older inspection helper's visibility fixture. A freshly
            // initialized world and its public reveal API establish this view;
            // no visibility-bit meaning is duplicated in this checker.
            var world=new World(localWorld!.Settings);localWorld=world;world.RevealClassicMap(0);ShowBattle();var view=battlefield!;
            OreVisibilityRequire(Graphics.HasOriginalResources,"Original TIB/GEM source art missing");
            foreach(var unit in world.Units)unit.DisabledUntil=int.MaxValue;
            var resourceCells=world.Map.OriginalDefinition!.RuntimeCells.Where(c=>c.ResourceKind>0 && c.Playable && world.Map.Walkable(c.X,c.Y) &&
                !world.Units.Any(u=>Math.Abs(u.CellX-c.X)<3 && Math.Abs(u.CellY-c.Y)<3)).ToArray();
            ClassicMapCell Pick(int kind)=>resourceCells.Where(c=>c.ResourceKind==kind && c.OverlayName is not "TIB02" and not "TIB2_02")
                .OrderBy(c=>Math.Abs(c.X-world.Map.Width/2)+Math.Abs(c.Y-world.Map.Height/2)).First();
            var oreSite=Pick(1);var gemSite=Pick(2);
            foreach(var cell in new[]{oreSite,gemSite})
            {
                var index=world.Map.Index(cell.X,cell.Y);var value=cell.ResourceValue;var step=value*4;var label=cell.ResourceKind==2?"gems":"ore";
                oreVisibilityReport["phase"]=label;view.SetCameraCell(new Vector2(cell.X+.5f-cell.SurfaceLevel*.5f,cell.Y+.5f-cell.SurfaceLevel*.5f));
                OreVisibilityRequire(world.Visible(0,cell.X,cell.Y) && world.Explored(0,cell.X,cell.Y),"Reveal API did not expose original resource");await Frame();
                byte[]? fullPixels=null;byte[]? partialPixels=null;int imageWidth=0,imageHeight=0;
                foreach(var stage in new[]{(Name:"full",Amount:step*12,Density:11),(Name:"partial",Amount:step*3,Density:2),(Name:"depleted",Amount:0,Density:-1)})
                {
                    world.Map.Ore[index]=stage.Amount;await Frame();var audit=view.GetResourceCellAudit(cell.X,cell.Y);
                    OreVisibilityRequire(audit["known"].AsBool() && audit["visible"].AsBool(),"Fixture resource not locally visible");
                    OreVisibilityRequire(audit["amount"].AsInt32()==stage.Amount && audit["density"].AsInt32()==stage.Density,"Resource amount/density mismatch");
                    OreVisibilityRequire(audit["tooltip"].AsString().Length>0,"Known resource quantity tooltip absent");
                    OreVisibilityRequire(audit["drawn"].AsBool()==(stage.Amount>0),"Positive deposit not drawn, or exhausted deposit still drawn");
                    var sprite=audit["sprite"].AsString();OreVisibilityRequire(sprite.StartsWith(cell.ResourceKind==2?"gem":"tib",StringComparison.Ordinal),"Wrong original resource family");
                    if(stage.Amount>0)
                    {
                        var art=Graphics.GetOriginalResourceArtworkAudit(cell.OverlayName,cell.ResourceKind,0,stage.Density);
                        OreVisibilityRequire(art["available"].AsBool() && art["opaquePixels"].AsInt32()>0,"Resource selected an empty original frame");audit["artwork"]=art;
                        OreVisibilityRequire(audit["drawnAmount"].AsInt32()==stage.Amount && audit["drawnDensity"].AsInt32()==stage.Density,"Rendered mineral state is stale");
                    }
                    audit["stage"]=label+"-"+stage.Name;audit["x"]=cell.X;audit["y"]=cell.Y;audit["sourceX"]=cell.SourceX;audit["sourceY"]=cell.SourceY;
                    audit["sourceOverlay"]=cell.OverlayName;audit["fallbacks"]=view.OreFallbackTilesDrawn;
                    var shot=await Capture(label+"-"+stage.Name,audit);imageWidth=shot.Width;imageHeight=shot.Height;
                    if(stage.Name=="full")fullPixels=shot.Pixels;
                    else if(stage.Name=="partial")partialPixels=shot.Pixels;
                    else
                    {
                        var point=view.GlobalPosition+view.ProjectGround(new Vector2(cell.X+.5f,cell.Y+.5f));
                        var region=new Rect2(point-new Vector2(34,34)*view.Zoom,new Vector2(68,68)*view.Zoom);
                        var fullDiff=OreVisibilityPixelDifference(fullPixels!,shot.Pixels,imageWidth,imageHeight,region);
                        var partialDiff=OreVisibilityPixelDifference(partialPixels!,shot.Pixels,imageWidth,imageHeight,region);
                        OreVisibilityRequire(fullDiff>20 && partialDiff>0,"GPU image did not change from full/partial mineral to empty ground");
                        audit["gpuFullVsEmptyPixels"]=fullDiff;audit["gpuPartialVsEmptyPixels"]=partialDiff;
                    }
                    OreVisibilityRequire(view.OreFallbackTilesDrawn==0,"Original mineral art fell back to synthetic nuggets");
                }
            }
            // Use an actual unexplored resource in a natural fresh world. Hidden
            // amount changes stay undisclosed until the public reveal API runs.
            oreVisibilityReport["phase"]="unknown-resource-privacy";var hiddenWorld=new World(world.Settings);localWorld=hiddenWorld;ShowBattle();view=battlefield!;
            var hiddenCell=hiddenWorld.Map.OriginalDefinition!.RuntimeCells.First(c=>c.ResourceKind>0 && c.Playable && !hiddenWorld.Visible(0,c.X,c.Y) && !hiddenWorld.Explored(0,c.X,c.Y));
            var hiddenIndex=hiddenWorld.Map.Index(hiddenCell.X,hiddenCell.Y);var hidden=view.GetResourceCellAudit(hiddenCell.X,hiddenCell.Y);
            OreVisibilityRequire(!hidden["known"].AsBool() && !hidden.ContainsKey("amount") && view.ResourceDescriptionAtCell(hiddenCell.X,hiddenCell.Y)=="","Unexplored quantity leaked");
            hiddenWorld.Map.Ore[hiddenIndex]=hiddenCell.ResourceValue*8;
            OreVisibilityRequire(!view.GetResourceCellAudit(hiddenCell.X,hiddenCell.Y).ContainsKey("amount"),"Hidden extraction leaked");
            hiddenWorld.RevealClassicMap(0);OreVisibilityRequire(view.GetResourceCellAudit(hiddenCell.X,hiddenCell.Y)["amount"].AsInt32()==hiddenCell.ResourceValue*8,"Revealed resource not refreshed");
            localWorld=world;ShowBattle();view=battlefield!;
            // Exercise the real economy on an original ore coordinate. This sets
            // up a native inspection world; it never replaces harvest code.
            oreVisibilityReport["phase"]="actual-harvest";
            Building AddBuilding(BuildingType type)
            {
                var definition=Rules.Building(type,Country.America);
                var cell=world.Map.OriginalDefinition!.RuntimeCells.Where(c=>c.Playable && c.Buildable && world.Map.FootprintFree(definition,c.X,c.Y) &&
                    !world.BuildingAreaHasUnits(c.X,c.Y,definition.Width,definition.Height)).OrderBy(c=>Math.Abs(c.X-oreSite.X)+Math.Abs(c.Y-oreSite.Y)).First();
                return world.SpawnBuilding(0,type,cell.X,cell.Y,foundationCountry:Country.America);
            }
            AddBuilding(BuildingType.PowerPlant);var refinery=AddBuilding(BuildingType.Refinery);refinery.FreeMinerPending=false;
            var miner=world.SpawnUnit(0,UnitType.ChronoMiner,oreSite.X,oreSite.Y,Country.America);
            OreVisibilityRequire(miner.CellX==oreSite.X && miner.CellY==oreSite.Y,"Harvester fixture relocated off original ore");
            foreach(var unit in world.Units)if(unit.Id!=miner.Id)unit.DisabledUntil=int.MaxValue;
            miner.Order=UnitOrder.Harvest;miner.GoalX=oreSite.X;miner.GoalY=oreSite.Y;miner.Path.Clear();miner.PathIndex=0;
            var oreIndex=world.Map.Index(oreSite.X,oreSite.Y);world.Map.Ore[oreIndex]=oreSite.ResourceValue*16;var before=world.Map.Ore[oreIndex];
            for(var tick=0;tick<20;tick++)world.Step();
            var after=world.Map.Ore[oreIndex];OreVisibilityRequire(after<before && miner.Cargo>0 && before-after==miner.Cargo,"Actual extraction failed or cargo differs from depletion");
            world.RevealClassicMap(0);
            view.SetCameraCell(new Vector2(oreSite.X+.5f-oreSite.SurfaceLevel*.5f,oreSite.Y+.5f-oreSite.SurfaceLevel*.5f));await Frame();
            var extraction=view.GetResourceCellAudit(oreSite.X,oreSite.Y);extraction["before"]=before;extraction["after"]=after;extraction["cargo"]=miner.Cargo;extraction["bales"]=miner.CargoBales;extraction["miner"]=miner.Id;
            OreVisibilityRequire(extraction["amount"].AsInt32()==after && extraction["drawnAmount"].AsInt32()==after,"Harvest changed Core but not visible ore/quantity");
            oreVisibilityReport["harvest"]=extraction;await Capture("actual-original-map-harvest",extraction);
            oreVisibilityReport["phase"]="native-quantity-tooltip";ProjectSettings.SetSetting("gui/timers/tooltip_delay_sec",.15);
            MoveBattlePolishPointer(view.GlobalPosition+view.ProjectGround(new Vector2(oreSite.X+.5f,oreSite.Y+.5f)));
            await ToSignal(GetTree().CreateTimer(.65),SceneTreeTimer.SignalName.Timeout);await Frame();
            var tooltip=view.GetResourceTooltipAudit();OreVisibilityRequire(tooltip["visible"].AsBool(),"Actual native resource hover tooltip did not appear");
            for(var tick=0;tick<5;tick++)world.Step();await Frame();var current=view.GetResourceCellAudit(oreSite.X,oreSite.Y);tooltip=view.GetResourceTooltipAudit();
            OreVisibilityRequire(tooltip["visible"].AsBool() && tooltip["text"].AsString()==current["tooltip"].AsString(),"Stationary hover quantity failed to refresh after actual extraction");
            oreVisibilityReport["tooltip"]=tooltip;await Capture("native-live-resource-tooltip",tooltip);
            oreVisibilityReport["passed"]=true;oreVisibilityReport["hiddenQuantityLeak"]=false;
        }
        catch(Exception error){oreVisibilityReport["passed"]=false;oreVisibilityReport["error"]=error.ToString();GD.PushError("Ore visibility checks: "+error.Message);}
        finally
        {
            ProjectSettings.SetSetting("gui/timers/tooltip_delay_sec",previousTooltipDelay);
            oreVisibilityChecksRunning=false;oreVisibilityReport["running"]=false;oreVisibilityReport["completed"]=true;oreVisibilityReport["captures"]=captures;
            GetOreVisibilityAudit();File.WriteAllText(Path.Combine(folder,"report.json"),Godot.Json.Stringify(oreVisibilityReport,"\t"));
        }
    }
}
