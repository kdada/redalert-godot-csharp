using Godot;
using RedAlert.Simulation;
using System.Diagnostics;

namespace RedAlert;

public partial class Main
{
    // Bounded isolated simulation fixtures. These prepare map/units explicitly;
    // they are separate from the real sidebar-input check below.
    public Godot.Collections.Dictionary RunProductionFundingChecks()
    {
        var checks=new Godot.Collections.Array<string>();var errors=new Godot.Collections.Array<string>();var steps=0;var serial=0;
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        void Test(string name,Action action){try{action();checks.Add(name);}catch(Exception e){errors.Add(name+": "+e.Message);}}
        void Step(World w,params GameCommand[] commands)
        {
            if(++steps>1500)throw new InvalidOperationException("Funding fixtures exceeded 1500-step cap");
            w.Step(commands.Select(c=>c with{Tick=w.Tick,Sequence=++serial}).ToArray());
        }
        World Fixture()
        {
            var w=new World(MatchSettings.LegacyFixture(2));
            foreach(var u in w.Units){u.DisabledUntil=int.MaxValue;u.Order=UnitOrder.Idle;u.TargetId=0;}
            for(var y=30;y<=65;y++)for(var x=30;x<=65;x++)
            {var i=w.Map.Index(x,y);w.Map.Ground[i]=Terrain.Grass;w.Map.Elevation[i]=0;w.Map.Bridges[i]=false;w.Map.Ramps[i]=RampDirection.None;w.Map.Ore[i]=0;}
            return w;
        }
        Test("building spends progressively; mining income auto-resumes; manual pause/resume/refund",()=>
        {
            var w=Fixture();var p=w.Players[0];p.Credits=12;
            Require(w.CanQueueBuilding(0,BuildingType.PowerPlant),"Low funds rejected queue admission");
            Step(w,new GameCommand(){Type=CommandType.QueueBuilding,Building=BuildingType.PowerPlant});for(var i=0;i<4;i++)Step(w);
            var progress=p.BuildingProductionProgress;var paid=p.BuildingProductionPaid;
            Require(progress>0 && paid>0 && paid<Rules.Building(BuildingType.PowerPlant,p.Setup.Country).Cost && p.Credits+paid==12,"Queue charged full cost or granted unpaid progress");
            for(var i=0;i<5;i++)Step(w);
            Require(p.BuildingProductionProgress==progress && p.BuildingProductionPaid==paid && !p.BuildingProductionPaused,"Insufficient funds did not preserve an automatic wait");
            var miner=w.Units.First(u=>u.Owner==0 && u.Definition.Miner);var refinery=w.Buildings.First(b=>b.Owner==0 && b.Type==BuildingType.Refinery);
            var dock=w.NearestFree(refinery.CellLeft+refinery.Definition.Width+1,refinery.CellTop+refinery.Definition.Height/2,domain:miner.Definition.Domain,amphibious:miner.Definition.Amphibious)!.Value;
            miner.X=dock.X*Rules.Scale+Rules.Scale/2;miner.Y=dock.Y*Rules.Scale+Rules.Scale/2;miner.DisabledUntil=0;miner.Order=UnitOrder.Harvest;miner.Returning=true;miner.Cargo=125;miner.CargoBales=5;
            Step(w);miner.DisabledUntil=int.MaxValue;Require(miner.Cargo==0 && p.Credits==137-paid,"Prepared miner load did not unload through normal economy");
            Step(w);Require(p.BuildingProductionProgress>progress && p.BuildingProductionPaid>paid,"Incoming mining credits did not resume automatically");
            Step(w,new GameCommand(){Type=CommandType.PauseBuilding});progress=p.BuildingProductionProgress;paid=p.BuildingProductionPaid;var cash=p.Credits;
            for(var i=0;i<3;i++)Step(w);
            Require(p.BuildingProductionPaused && p.BuildingProductionProgress==progress && p.Credits==cash,"Manual pause spent or advanced");
            Step(w,new GameCommand(){Type=CommandType.ResumeBuilding});Require(!p.BuildingProductionPaused && p.BuildingProductionProgress>progress,"Left/resume command did not continue");
            Step(w,new GameCommand(){Type=CommandType.CancelBuilding});Require(p.BuildingProduction is null && p.BuildingProductionPaid==0 && !p.BuildingProductionPaused && p.Credits==137,"Cancel did not return only actual spending");
        });
        Test("unpaid queue followers refund zero; active cancellation and sale refund paid once",()=>
        {
            var w=Fixture();var b=w.SpawnBuilding(0,BuildingType.Barracks,40,40);var p=w.Players[0];p.Credits=1000;
            Step(w,new GameCommand(){Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Gi},new GameCommand(){Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Engineer});
            for(var i=0;i<3;i++)Step(w);Step(w,new GameCommand(){Type=CommandType.PauseQueue,Actors=[b.Id]});var paid=b.ProductionPaid;var cash=p.Credits;
            Require(b.Queue.Count==2 && paid>0 && cash+paid==1000,"Followers were prepaid");
            Step(w,new GameCommand(){Type=CommandType.CancelQueue,Actors=[b.Id]});Require(b.Queue.Count==1 && b.ProductionPaid==paid && b.ProductionPaused && p.Credits==cash,"Unpaid tail refunded or reset the active head");
            Step(w,new GameCommand(){Type=CommandType.CancelQueue,Actors=[b.Id],Target=1});Require(b.Queue.Count==0 && b.ProductionPaid==0 && !b.ProductionPaused && p.Credits==1000,"Active cancellation ledger/reset mismatch");
            Step(w,new GameCommand(){Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Gi});Step(w);
            Step(w,new GameCommand(){Type=CommandType.Sell,Actors=[b.Id]});Require(p.Credits==1000+b.Definition.Cost/2 && b.ProductionPaid==0 && b.Queue.Count==0,"Sale refunded unpaid followers or lost actual paid credits");
        });
        Test("independent producers progress together; pausing one leaves the other running",()=>
        {
            var w=Fixture();var a=w.SpawnBuilding(0,BuildingType.Barracks,40,40);var b=w.SpawnBuilding(0,BuildingType.WarFactory,48,40);var p=w.Players[0];p.Credits=1000;
            Step(w,new GameCommand(){Type=CommandType.Train,Actors=[a.Id],Unit=UnitType.Gi},new GameCommand(){Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Grizzly});
            Require(a.ProductionProgress==1 && b.ProductionProgress==1 && p.Credits+a.ProductionPaid+b.ProductionPaid==1000,"Producer queues serialized or cash ledger diverged");
            Step(w,new GameCommand(){Type=CommandType.PauseQueue,Actors=[a.Id]});var progress=a.ProductionProgress;for(var i=0;i<3;i++)Step(w);
            Require(a.ProductionProgress==progress && b.ProductionProgress>progress && p.Credits+a.ProductionPaid+b.ProductionPaid==1000,"One pause blocked independent production");
            Step(w,new GameCommand(){Type=CommandType.ResumeQueue,Actors=[a.Id]});Require(a.ProductionProgress>progress,"Unit queue did not resume");
        });
        Test("Soviet preparation reaches ready at exactly 600 paid credits",()=>
        {
            var w=Fixture();var p=w.Players[1];p.Credits=600;var d=Rules.Building(BuildingType.PowerPlant,p.Setup.Country);
            Step(w,new GameCommand(){Player=1,Type=CommandType.QueueBuilding,Building=BuildingType.PowerPlant});
            for(var i=0;i<d.BuildTicks+3 && !p.BuildingReady;i++)Step(w);
            Require(p.BuildingReady && p.BuildingProductionPaid==600 && p.Credits==0,"Foundation/country price or paid-ready stage mismatch");
            Step(w,new GameCommand(){Player=1,Type=CommandType.CancelBuilding});Require(p.Credits==600 && p.BuildingProductionPaid==0,"Ready cancellation did not refund paid cost exactly");
        });
        Test("capture returns the old owner's paid head; captured foundation produces its own prototype",()=>
        {
            var w=Fixture();var b=w.SpawnBuilding(1,BuildingType.Barracks,40,40);var oldCash=w.Players[1].Credits;
            Step(w,new GameCommand(){Player=1,Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Conscript});for(var i=0;i<4;i++)Step(w);
            Step(w,new GameCommand(){Player=1,Type=CommandType.PauseQueue,Actors=[b.Id]});Require(b.ProductionPaid>0 && b.ProductionPaid<100,"Capture fixture did not reach partial paid head");
            var engineer=w.SpawnUnit(0,UnitType.Engineer,39,40);for(var i=0;i<5;i++)Step(w);
            Step(w,new GameCommand(){Type=CommandType.Attack,Actors=[engineer.Id],Target=b.Id});for(var i=0;i<20 && b.Owner!=0;i++)Step(w);
            Require(b.Owner==0 && b.FoundationCountry==Country.Russia && b.Queue.Count==0 && b.ProductionPaid==0 && !b.ProductionPaused && w.Players[1].Credits==oldCash,"Capture duplicated/lost spending or reset foundation country");
            var before=w.Units.Max(u=>u.Id);w.Players[0].Credits=100;
            Step(w,new GameCommand(){Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Conscript});for(var i=0;i<25 && b.Queue.Count>0;i++)Step(w);
            Require(w.Units.Any(u=>u.Id>before && u.Owner==0 && u.Type==UnitType.Conscript && u.OriginCountry==Country.Russia) && w.Players[0].Credits==0,"Captured producer charged or created the owner's alternate prototype");
        });
        Test("blocked paid Soviet infantry refunds once; a released original and its clone spend once",()=>
        {
            var w=Fixture();var b=w.SpawnBuilding(1,BuildingType.Barracks,40,40);var vat=w.SpawnBuilding(1,BuildingType.CloningVats,50,40);w.SpawnBuilding(1,BuildingType.NuclearReactor,55,40);
            var mouth=w.ClassicProductionExitPortals(b).Single();var wall=w.SpawnBuilding(1,BuildingType.Wall,mouth.X,mouth.Y);var before=w.Units.Max(u=>u.Id);w.Players[1].Credits=100;
            Step(w,new GameCommand(){Player=1,Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Conscript});for(var i=0;i<25 && b.Queue.Count>0;i++)Step(w);
            Require(b.Queue.Count==0 && b.ProductionPaid==0 && w.Players[1].Credits==100 && !w.Units.Any(u=>u.Id>before) && vat.PendingClones.Count==0,"Blocked job refund/clone mismatch");
            Step(w);Require(w.Players[1].Credits==100,"Blocked job refunded twice");wall.Health=0;Step(w);
            Step(w,new GameCommand(){Player=1,Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Conscript});for(var i=0;i<30 && w.Units.Count(u=>u.Id>before && u.Type==UnitType.Conscript)<2;i++)Step(w);
            Require(w.Units.Count(u=>u.Id>before && u.Type==UnitType.Conscript)==2 && w.Players[1].Credits==0,"Free clone was charged or unpaid original escaped");
        });
        Test("already-paid structural refund precedes a full blocked clone backlog",()=>
        {
            var w=Fixture();var b=w.SpawnBuilding(1,BuildingType.Barracks,40,40);var vat=w.SpawnBuilding(1,BuildingType.CloningVats,58,40);w.SpawnBuilding(1,BuildingType.NuclearReactor,55,60);
            var cx=vat.CellLeft+vat.Definition.Width+1;var cy=vat.CellTop+vat.Definition.Height/2;
            for(var y=cy-15;y<=cy+15;y++)for(var x=cx-15;x<=cx+15;x++)if(w.Map.Inside(x,y))w.Map.Ground[w.Map.Index(x,y)]=Terrain.Water;
            for(var i=0;i<64;i++)vat.PendingClones.Add(new(UnitType.Conscript,Country.Russia,1));
            var d=Rules.Unit(UnitType.Conscript,b.FoundationCountry);var cash=w.Players[1].Credits;
            w.Players[1].Credits-=d.Cost;b.Queue.Add(d.Type);b.ProductionProgress=d.BuildTicks;b.ProductionPaid=d.Cost;
            var mouth=w.ClassicProductionExitPortals(b).Single();w.SpawnBuilding(1,BuildingType.Wall,mouth.X,mouth.Y);Step(w);
            Require(vat.PendingClones.Count==64 && b.Queue.Count==0 && b.ProductionPaid==0 && w.Players[1].Credits==cash,"Full clone backlog prevented a no-spawn paid refund");
            Step(w);Require(w.Players[1].Credits==cash,"Ready blocked job refunded twice");
        });
        Test("paid/manual-pause fields affect deterministic state and defeat clears them",()=>
        {
            var w=Fixture();var b=w.SpawnBuilding(0,BuildingType.Barracks,40,40);var p=w.Players[0];
            var hash=w.StateHash();p.BuildingProductionPaid=1;Require(hash!=w.StateHash(),"Player paid missing from hash");p.BuildingProductionPaid=0;
            hash=w.StateHash();p.BuildingProductionPaused=true;Require(hash!=w.StateHash(),"Player pause missing from hash");p.BuildingProductionPaused=false;
            hash=w.StateHash();b.ProductionPaid=1;Require(hash!=w.StateHash(),"Producer paid missing from hash");b.ProductionPaid=0;
            hash=w.StateHash();b.ProductionPaused=true;Require(hash!=w.StateHash(),"Producer pause missing from hash");b.ProductionPaused=false;
            Step(w,new GameCommand(){Type=CommandType.QueueBuilding,Building=BuildingType.PowerPlant},new GameCommand(){Type=CommandType.Train,Actors=[b.Id],Unit=UnitType.Gi});
            Step(w,new GameCommand(){Type=CommandType.PauseBuilding},new GameCommand(){Type=CommandType.PauseQueue,Actors=[b.Id]});Step(w,new GameCommand(){Type=CommandType.Surrender});
            Require(p.Defeated && p.BuildingProduction is null && p.BuildingProductionPaid==0 && !p.BuildingProductionPaused && b.Queue.Count==0 && b.ProductionPaid==0 && !b.ProductionPaused,"Defeat retained paid production state");
        });
        return new(){{"passed",errors.Count==0},{"checks",checks},{"errors",errors},{"steps",steps},{"fixtureOnly",true}};
    }

    Godot.Collections.Dictionary productionFundingUiReport=new();
    public Godot.Collections.Dictionary GetProductionFundingUiAudit()=>productionFundingUiReport;
    // Real low-credit sidebar interaction: commands step only through Main._Process.
    public async void RunProductionFundingUiChecks()
    {
        if(productionFundingUiReport.TryGetValue("running",out var running) && running.AsBool())return;
        productionFundingUiReport=new(){{"running",true},{"passed",false},{"fixtureOnly",false},{"startingCredits",12},{"directEntitySpawns",0}};
        var savedSpeed=clientPreferences.GameSpeed;var savedScale=Engine.TimeScale;var savedFast=fast;
        var savedEnemy=fullPlaythroughControlsEnemy;var savedPaused=fullPlaythroughPaused;var clock=Stopwatch.StartNew();World? world=null;
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        async Task Wait(Func<bool> condition,string phase)
        {
            productionFundingUiReport["phase"]=phase;
            while(!condition()){if(clock.Elapsed.TotalSeconds>90)throw new TimeoutException(phase);await PointerParityFrame();}
        }
        void Click(Control button,MouseButton mouse)
        {
            var point=button.GetGlobalRect().GetCenter();if(softwareBattlePointer)softwarePointer=ClampBattlePointer(point);forwardingBattlePointer=true;
            try
            {
                using var motion=new InputEventMouseMotion{Position=point,GlobalPosition=point};GetViewport().PushInput(motion,true);
                using var down=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=mouse,Pressed=true};
                using var up=new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=mouse};GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);
            }
            finally{forwardingBattlePointer=false;}
        }
        try
        {
            Require(!headless,"Sidebar funding check requires native viewport");ReturnToDeployment();await PointerParityFrame();
            var info=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath)).Find("mp08t2")!;
            var settings=new MatchSettings(20261018,[new("资金按钮验证",Country.Russia,0),new("静止对手",Country.America,1)],12,info.Width,info.Height){MapId=info.Id,ClassicStart=true,StartingUnits=0,Crates=false};
            practiceDifficulties=[1,1];fullPlaythroughControlsEnemy=true;fullPlaythroughPaused=false;fast=false;clientPreferences.GameSpeed=6;Engine.TimeScale=3;StartPractice(Country.Russia,settings);
            await Wait(()=>localWorld is not null,"prepare-match");world=localWorld!;terrainInspection=false;
            var mcv=world.Units.Single(u=>u.Owner==0 && u.Type==UnitType.Mcv);Issue(new GameCommand(){Type=CommandType.Deploy,Actors=[mcv.Id]});
            await Wait(()=>world.HasBuilding(0,BuildingType.ConstructionYard),"mcv-deployed");
            productTab="build";lastProductKey="";RefreshClassicProducts();await PointerParityFrame();await PointerParityFrame();
            var entry=productButtons.Single(p=>p.Building==BuildingType.PowerPlant);var button=entry.Button;var player=world.Players[0];
            Require(!button.Disabled && player.Credits==12,"Low-funds product button was disabled or funds changed");
            Click(button,MouseButton.Left);await Wait(()=>player.BuildingProduction==BuildingType.PowerPlant && player.BuildingProductionPaid>0 && player.Credits<5,"low-funds-button-queued");
            var progress=player.BuildingProductionProgress;var paid=player.BuildingProductionPaid;var at=world.Tick;
            await Wait(()=>world.Tick>=at+5,"automatic-cash-wait");Require(player.BuildingProductionProgress==progress && !player.BuildingProductionPaused,"Cash wait spent free progress or became manual pause");
            Click(button,MouseButton.Right);await Wait(()=>player.BuildingProductionPaused,"right-paused");
            Require(player.BuildingProductionProgress==progress && player.BuildingProductionPaid==paid,"Pause changed ledger");
            Click(button,MouseButton.Left);await Wait(()=>!player.BuildingProductionPaused && player.BuildingProduction is not null,"left-resumed");
            Click(button,MouseButton.Right);await Wait(()=>player.BuildingProductionPaused,"right-paused-again");
            Click(button,MouseButton.Right);await Wait(()=>player.BuildingProduction is null,"second-right-cancelled");
            Require(player.Credits==12 && player.BuildingProductionPaid==0 && !player.BuildingProductionPaused && world.RejectedCommands==0,"Sidebar cancellation refunded incorrect paid amount or rejected commands");
            productionFundingUiReport["passed"]=true;productionFundingUiReport["paidBeforeRefund"]=paid;productionFundingUiReport["finalCredits"]=player.Credits;productionFundingUiReport["acceptedCommands"]=world.AcceptedCommands;productionFundingUiReport["rejectedCommands"]=world.RejectedCommands;
            await PointerParityFrame();await PointerParityAfterDraw();
            var folder=ProjectSettings.GlobalizePath("res://Reports/production-funding-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
            var png=folder+"/sidebar-funding.png";Require(GetViewport().GetTexture().GetImage().SavePng(png)==Error.Ok,"Funding screenshot save failed");productionFundingUiReport["png"]=png;
        }
        catch(Exception e){productionFundingUiReport["error"]=e.ToString();}
        finally
        {
            productionFundingUiReport["running"]=false;productionFundingUiReport["elapsedSeconds"]=clock.Elapsed.TotalSeconds;
            clientPreferences.GameSpeed=savedSpeed;Engine.TimeScale=savedScale;fast=savedFast;fullPlaythroughControlsEnemy=savedEnemy;fullPlaythroughPaused=savedPaused;
        }
    }
}
