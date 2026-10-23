namespace RedAlert.Simulation;

public sealed partial class World
{
    bool Apply(GameCommand c)
    {
        if(Finished || c.Player<0 || c.Player>=Players.Length || Players[c.Player].Defeated || !Enum.IsDefined(c.Type) || c.Actors is null || c.Actors.Length>Rules.MaxEntities)return false;
        var p=Players[c.Player];
        switch(c.Type)
        {
            case CommandType.Surrender:
                DefeatPlayer(p);return true;
            case CommandType.Build:
                return OrderBuild(c,p);
            case CommandType.QueueBuilding:
                return OrderQueueBuilding(c,p);
            case CommandType.CancelBuilding:
                return OrderCancelBuilding(p);
            case CommandType.Train:
                return OrderTrain(c,p);
            case CommandType.PauseBuilding:return OrderClassicProductionPause(c,p,true,true);
            case CommandType.ResumeBuilding:return OrderClassicProductionPause(c,p,true,false);
            case CommandType.PauseQueue:return OrderClassicProductionPause(c,p,false,true);
            case CommandType.ResumeQueue:return OrderClassicProductionPause(c,p,false,false);
            case CommandType.Deploy:return OrderDeploy(c,p);
            case CommandType.EnterTransport:return OrderEnterTransport(c,p);
            case CommandType.Unload:return OrderUnload(c,p);
            case CommandType.Special:return OrderSpecial(c,p);
            case CommandType.SuperWeapon:return OrderSuperWeapon(c,p);
            case CommandType.AttackGround:return OrderTerrainAttack(c,p);
            case CommandType.Support:
                if(p.Setup.Country!=Country.America || p.SupportReadyTick<0 || Tick<p.SupportReadyTick || !p.Powered || !Map.Walkable(c.X,c.Y) || !Visible(p.Id,c.X,c.Y)
                    || !Buildings.Any(b=>b.Owner==p.Id && b.Definition.AirCapacity>0 && ClassicBuildingOperational(b)) || EntityCount+8>Rules.MaxEntities)return false;
                for(var i=0;i<8;i++){var cell=NearestFree(c.X+(i%3)-1,c.Y+(i/3),true);if(cell is not null)SpawnUnit(p.Id,UnitType.Gi,cell.Value.X,cell.Value.Y);}
                p.SupportReadyTick=Tick+1200;return true;
            case CommandType.CancelQueue:
            case CommandType.Rally:
            case CommandType.Sell:
            case CommandType.Repair:
                return OrderBuildingAction(c,p);
            case CommandType.Move:
            case CommandType.AttackMove:
            case CommandType.Attack:
            case CommandType.Stop:
                return OrderUnits(c,p);
            default:return false;
        }
    }
    bool OrderBuild(GameCommand c,PlayerState p)
    {
        if(!CanPlaceBuilding(p.Id,c.Building,c.X,c.Y))return false;
        var b=SpawnBuilding(p.Id,c.Building,c.X,c.Y,false);
        b.RemainingBuildTicks=Rules.BuildingPlacementTicks;b.BuildDurationTicks=Rules.BuildingPlacementTicks;
        ClearClassicBuildingProduction(p);
        return true;
    }
    bool OrderTrain(GameCommand c,PlayerState p)
    {
        if(!Enum.IsDefined(c.Unit) || c.Actors.Length!=1 || Find(c.Actors[0]) is not Building b || b.Owner!=p.Id || !b.Complete || b.Queue.Count>=12)return false;
        var d=Rules.Unit(c.Unit,b.FoundationCountry);
        if(!CanTrainUnit(p.Id,c.Unit,b) || d.AirportBound && !HasClassicAircraftSlot(b))return false;
        b.Queue.Add(c.Unit);return true;
    }
    bool OrderBuildingAction(GameCommand c,PlayerState p)
    {
        if(c.Actors.Length!=1 || Find(c.Actors[0]) is not Building b || b.Owner!=p.Id)return false;
        switch(c.Type)
        {
            case CommandType.Sell:
                AddClassicCredits(p.Id,b.Definition.Cost/2);
                RefundClassicProducerQueue(b,p.Id);b.Health=0;return true;
            case CommandType.Repair:b.Repairing=!b.Repairing;return true;
            case CommandType.Rally:
                if(!Map.IsPlayable(c.X,c.Y))return false;
                if(b.Definition.Produces==ProductionCategory.Naval && !Map.Navigable(c.X,c.Y,MovementDomain.Water))return false;
                if(b.Definition.Produces is ProductionCategory.Infantry or ProductionCategory.Vehicle && !Map.Walkable(c.X,c.Y))return false;
                b.RallyX=c.X;b.RallyY=c.Y;return true;
            case CommandType.CancelQueue:
                if(b.Queue.Count==0)return false;
                // Target is an optional one-based queue slot; zero preserves the
                // existing cancel-tail command used by menus and older callers.
                var index=c.Target==0?b.Queue.Count-1:c.Target-1;
                if(index<0 || index>=b.Queue.Count)return false;
                if(index==0){AddClassicCredits(p.Id,b.ProductionPaid);ResetClassicProducerHead(b);}
                b.Queue.RemoveAt(index);return true;
            default:return false;
        }
    }
    bool OrderUnits(GameCommand c,PlayerState p)
    {
        if(c.Actors.Length==0)return false;
        // Orders are atomic with respect to ownership; forged mixed selections cannot command enemy actors.
        var selected=new List<Unit>();
        foreach(var id in c.Actors.Distinct().Order())
        {
            var actor=Find(id);
            if(actor is null)continue; // Units may have died while a valid order was in flight.
            if(actor is not Unit u || u.Owner!=p.Id)return false;
            selected.Add(u);
        }
        if(selected.Count==0)return false;
        if(c.Type is CommandType.Move or CommandType.AttackMove && !Map.IsPlayable(c.X,c.Y))return false;
        Entity? target=null;
        if(c.Type==CommandType.Attack)
        {
            target=Find(c.Target);
            if(target is null || !Visible(p.Id,target.CellX,target.CellY))return false;
            if(Allied(p.Id,target.Owner) && !selected.All(u=>u.Type==UnitType.Engineer))return false;
            selected.RemoveAll(u=>!CanTarget(u,target));
            if(selected.Count==0)return false;
        }
        var reserved=new HashSet<int>();
        foreach(var u in selected)
        {
            if(u.TransportId!=0 || u.ParasiteHostId!=0)continue;
            u.TargetId=0;u.SpecialOrder=false;u.PendingTransportId=0;
            if(u.Type==UnitType.Gi && c.Type is CommandType.Move or CommandType.AttackMove)u.Deployed=false;
            if(u.Definition.AirportBound && u.Ammo>0)u.Returning=false;
            if(c.Type==CommandType.Stop){u.Order=UnitOrder.Idle;u.Path.Clear();u.Cargo=Math.Max(0,u.Cargo);continue;}
            if(c.Type==CommandType.Attack && target is not null)
            {
                u.TargetId=target.Id;SetGoal(u,target.CellX,target.CellY,UnitOrder.Attack);continue;
            }
            if(u.Definition.Miner && Map.Ore[Map.Index(c.X,c.Y)]>0)
            {u.Returning=false;SetGoal(u,c.X,c.Y,UnitOrder.Harvest);continue;}
            var goal=NearestFree(c.X,c.Y,false,reserved,u.Definition.Domain,u.Definition.Amphibious);
            if(goal is null)continue;
            reserved.Add(Map.Index(goal.Value.X,goal.Value.Y));
            SetGoal(u,goal.Value.X,goal.Value.Y,c.Type==CommandType.AttackMove?UnitOrder.AttackMove:UnitOrder.Move);
        }
        return true;
    }
}
