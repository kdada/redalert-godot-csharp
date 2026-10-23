using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    bool terrainInspection,terrainInspectionMotion;
    int inspectionUnit;

    // Developer-only entry, callable from the connected game debugger. Uses the
    // actual battlefield/art renderer and normal movement simulation, never a
    // separately reconstructed screenshot or product-facing inspection menu.
    public Godot.Collections.Dictionary OpenTerrainInspection(string feature="flat",float progress=.5f)
    {
        if(!terrainInspection || localWorld is null)
        {
            CancelLocalPreparation();StopExternalAi();network?.Dispose();network=null;
            localWorld=new RedAlert.Simulation.World(MatchSettings.LegacyFixture(2));
            terrainInspection=true;terrainInspectionMotion=false;accumulator=0;ended=false;failure="";
            localPending.Clear();localReplay.Clear();ShowBattle();
        }
        var world=localWorld!;var map=world.Map;var tank=world.Units.First(u=>u.Owner==0 && !u.Definition.Infantry && !u.Definition.Miner);
        inspectionUnit=tank.Id;terrainInspectionMotion=false;localPending.Clear();
        var spawn=map.Spawns[0];var point=new Vector2(spawn.X+1.22f,spawn.Y+5.22f);var heading=new Cell(1,0);
        if(feature.StartsWith("ramp",StringComparison.Ordinal))
        {
            var index=Array.FindIndex(map.Ramps,r=>r==RampDirection.RiseEast);
            if(index<0)index=Array.FindIndex(map.Ramps,r=>r!=RampDirection.None);
            if(index>=0)
            {
                var x=index%map.Width;var y=index/map.Width;var slope=map.SurfaceSlopeFixedAt(x,y);var along=new Vector2(Math.Sign(slope.X),Math.Sign(slope.Y));
                var t=Math.Clamp(progress,.02f,.98f);point=new Vector2(x+.5f,y+.5f)+along*(t-.5f);heading=new Cell((int)along.X,(int)along.Y);
                if(feature=="ramp-down")heading=new Cell(-heading.X,-heading.Y);
                if(feature=="ramp-cross")heading=new Cell(-heading.Y,heading.X);
            }
        }
        else if(feature=="bridge" && map.BridgeSpans.Length>0)
        {
            var span=map.BridgeSpans.OrderBy(s=>Math.Abs(s.Origin.Y-47)).First();
            point=span.Axis==BridgeAxis.X?new Vector2(span.Origin.X+span.Length*Math.Clamp(progress,.02f,.98f),span.Origin.Y+span.Width*.5f)
                :new Vector2(span.Origin.X+span.Width*.5f,span.Origin.Y+span.Length*Math.Clamp(progress,.02f,.98f));
            heading=span.Axis==BridgeAxis.X?new Cell(1,0):new Cell(0,1);
        }
        else if(feature=="plateau")
        {
            var index=Array.FindIndex(map.Elevation,e=>e==2);
            if(index>=0)point=new Vector2(index%map.Width+.35f,index/map.Width+.35f);
        }
        SetInspectionUnit(tank,point,heading);battlefield!.Selection.Clear();battlefield.Selection.Add(tank.Id);
        battlefield.SetCameraCell(point);RevealTerrainInspection();RefreshHud();battlefield.QueueRedraw();minimap?.InvalidateTerrain();
        return GetTerrainInspectionAudit();
    }
    static void SetInspectionUnit(Unit tank,Vector2 point,Cell heading)
    {
        tank.X=(int)MathF.Round(point.X*Rules.Scale);tank.Y=(int)MathF.Round(point.Y*Rules.Scale);
        tank.Order=UnitOrder.Idle;tank.Path.Clear();tank.PathIndex=0;tank.TargetId=0;tank.GoalX=tank.CellX;tank.GoalY=tank.CellY;
        tank.BodyFacingX=heading.X;tank.BodyFacingY=heading.Y;tank.FacingX=heading.X;tank.FacingY=heading.Y;
    }
    void RevealTerrainInspection()
    {
        if(localWorld is not{} world)return;
        foreach(var player in world.Players)
        for(var y=0;y<world.Map.Height;y++)for(var x=0;x<world.Map.Width;x++)
            player.Visibility[world.Map.Index(x,y)]=world.Map.IsPlayable(x,y)?(byte)3:(byte)0;
    }
    public Godot.Collections.Dictionary MoveTerrainInspection(string feature="bridge")
    {
        OpenTerrainInspection(feature,.05f);var world=localWorld!;var map=world.Map;
        var unit=world.Units.First(u=>u.Id==inspectionUnit);Cell start,target;
        if(feature=="bridge")
        {
            var span=map.BridgeSpans.OrderBy(s=>Math.Abs(s.Origin.Y-47)).First();start=span.BankA;target=span.BankB;
        }
        else
        {
            var index=Array.FindIndex(map.Ramps,r=>r==RampDirection.RiseEast);if(index<0)throw new InvalidOperationException("No east-facing inspection ramp");
            var ramp=new Cell(index%map.Width,index/map.Width);var slope=map.SurfaceSlopeFixedAt(ramp);
            var direction=new Cell(Math.Sign(slope.X),Math.Sign(slope.Y));
            start=new Cell(ramp.X-direction.X,ramp.Y-direction.Y);target=ramp;
            while(map.IsRamp(target))target=new Cell(target.X+direction.X,target.Y+direction.Y);
            if(feature=="ramp-down")(start,target)=(target,start);
        }
        SetInspectionUnit(unit,new Vector2(start.X+.5f,start.Y+.5f),new Cell(Math.Sign(target.X-start.X),Math.Sign(target.Y-start.Y)));
        localPending.Clear();Issue(new GameCommand{Type=CommandType.Move,Actors=[unit.Id],X=target.X,Y=target.Y});
        terrainInspectionMotion=true;accumulator=0;battlefield!.SetCameraCell(new Vector2((start.X+target.X+1)*.5f,(start.Y+target.Y+1)*.5f));
        return GetTerrainInspectionAudit();
    }
    void ProcessTerrainInspection(double delta)
    {
        if(localWorld is null)return;
        if(terrainInspectionMotion)
        {
            accumulator+=Math.Min(delta,.15);
            while(accumulator>=1.0/Rules.TicksPerSecond)
            {
                var commands=localPending.ToArray();localPending.Clear();localWorld.Step(commands);accumulator-=1.0/Rules.TicksPerSecond;
                var tank=localWorld.Units.FirstOrDefault(u=>u.Id==inspectionUnit);
                if(tank is null || tank.Order==UnitOrder.Idle){terrainInspectionMotion=false;break;}
            }
        }
        RevealTerrainInspection();UpdateCamera(delta);hudTimer+=delta;
        if(hudTimer>.15){hudTimer=0;RefreshHud();}
        battlefield?.QueueRedraw();minimap?.QueueRedraw();
    }
    public void CloseTerrainInspection()=>ReturnToDeployment();
    public async void CaptureTerrainInspectionFrame(string tag)
    {
        try{await SaveTerrainInspectionFrame(tag);}
        catch(Exception error){GD.PushError("Terrain inspection capture: "+error.Message);}
    }
    async Task SaveTerrainInspectionFrame(string tag)
    {
        if(tag.Length==0 || tag.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='-'))throw new ArgumentException("Invalid capture name");
        var drawn=new TaskCompletionSource();
        void AfterDraw(){RenderingServer.FramePostDraw-=AfterDraw;drawn.TrySetResult();}
        RenderingServer.FramePostDraw+=AfterDraw;await drawn.Task;
        var folder=ProjectSettings.GlobalizePath("res://Reports/terrain-visual");System.IO.Directory.CreateDirectory(folder);
        using var image=GetViewport().GetTexture().GetImage();var result=image.SavePng(System.IO.Path.Combine(folder,tag+".png"));
        if(result!=Error.Ok)throw new IOException("PNG save failed: "+result);
        var audit=GetTerrainInspectionAudit();audit["frame"]=Engine.GetFramesDrawn();audit["width"]=image.GetWidth();audit["heightPixels"]=image.GetHeight();
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder,tag+".json"),Godot.Json.Stringify(audit,"\t"));
    }
    public async void CaptureTerrainInspectionMotion(string feature)
    {
        try
        {
            MoveTerrainInspection(feature);
            for(var i=0;i<7;i++)
            {
                await SaveTerrainInspectionFrame(feature+"-motion-"+i.ToString("00"));
                if(!terrainInspectionMotion)break;
                await ToSignal(GetTree().CreateTimer(.35),SceneTreeTimer.SignalName.Timeout);
            }
        }
        catch(Exception error){GD.PushError("Terrain motion capture: "+error.Message);}
    }
    public Godot.Collections.Dictionary GetTerrainInspectionAudit()
    {
        var world=localWorld;var tank=world?.Units.FirstOrDefault(u=>u.Id==inspectionUnit);
        return new(){["inspection"]=terrainInspection,["moving"]=terrainInspectionMotion,["display"]=DisplayServer.GetName(),["renderer"]=RenderingServer.GetCurrentRenderingMethod(),
            ["tick"]=world?.Tick??0,["unit"]=tank?.Id??0,["cell"]=tank is null?Vector2.Zero:new Vector2(tank.X/(float)Rules.Scale,tank.Y/(float)Rules.Scale),
            ["height"]=tank is null?0:world!.Map.SurfaceHeightFixed(tank.X,tank.Y)/(float)Rules.Scale,["ramps"]=world?.Map.Ramps.Count(r=>r!=RampDirection.None)??0,
            ["bridgeSpans"]=world?.Map.BridgeSpans.Length??0,["drawCalls"]=battlefield?.DrawCalls??0,
            ["slopeVehicles"]=battlefield?.VehiclesWithSlopePose??0,["pitchDegrees"]=battlefield?.MaximumVehiclePitchDegrees??0,
            ["rollDegrees"]=battlefield?.MaximumVehicleRollDegrees??0,["rampTilesDrawn"]=battlefield?.LandscapeRampTilesDrawn??0,
            ["bridgeSpansDrawn"]=battlefield?.BridgeSpansDrawn??0};
    }
}
