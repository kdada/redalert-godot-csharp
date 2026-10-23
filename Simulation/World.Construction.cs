namespace RedAlert.Simulation;

public sealed partial class World
{
    public bool RequirementsMet(int player,IReadOnlyList<PrerequisiteDef> requirements)
    {
        if(player<0 || player>=Players.Length)return false;
        foreach(var requirement in requirements)
        {
            var found=Buildings.Any(building=>building.Alive && building.Complete && building.Owner==player
                && (building.Type==requirement.Building || requirement.Building==BuildingType.PowerPlant && requirement.Faction is null && building.Type==BuildingType.NuclearReactor)
                && (requirement.Faction is null || Rules.Side(building.FoundationCountry)==requirement.Faction));
            if(!found)return false;
        }
        return true;
    }

    public bool CanSeeUnit(int player,UnitType type)
    {
        if(player<0 || player>=Players.Length || !Enum.IsDefined(type))return false;
        var state=Players[player];var definition=Rules.Unit(type,state.Setup.Country);
        return definition.Buildable && Map.AllowsRuleObject(definition.RulesId) && Rules.NationallyAllowed(definition,state.Setup.Country)
            && (!definition.RequiresStolenAlliedTech || state.StolenAlliedTech)
            && (!definition.RequiresStolenSovietTech || state.StolenSovietTech);
    }

    bool ClassicUnitTechnologyAvailable(int player,UnitType type,Building producer)
    {
        if(Finished || player<0 || player>=Players.Length || Players[player].Defeated || !Enum.IsDefined(type)
            || !producer.Alive || !producer.Complete || producer.Owner!=player || !CanSeeUnit(player,type))return false;
        var definition=Rules.Unit(type,producer.FoundationCountry);
        if(producer.Definition.Produces!=definition.Category || producer.Type!=Rules.ProducerForUnit(type,producer.FoundationCountry)
            || definition.Faction is { } faction && faction!=Rules.Side(producer.FoundationCountry)
            || !RequirementsMet(player,definition.Prerequisites??[]))return false;
        if(definition.MaxInstances>0 && Units.Count(unit=>unit.Alive && unit.Owner==player && unit.Type==type)
            +Buildings.Where(building=>building.Alive && building.Owner==player).Sum(building=>building.Queue.Count(queued=>queued==type))>=definition.MaxInstances)return false;
        return true;
    }

    // Availability deliberately ignores credits, queue length and entity cap.
    public bool CanProduceUnit(int player,UnitType type)=>Buildings.Any(producer=>ClassicUnitTechnologyAvailable(player,type,producer));

    public bool CanTrainUnit(int player,UnitType type,Building producer)=>ClassicUnitTechnologyAvailable(player,type,producer)
        && EntityCount<Rules.MaxEntities && producer.Queue.Count<12
        && (!Rules.Unit(type,producer.FoundationCountry).AirportBound || HasClassicAircraftSlot(producer));

    public bool CanProduceBuilding(int player,BuildingType type)
    {
        if(Finished || player<0 || player>=Players.Length || Players[player].Defeated || !Enum.IsDefined(type))return false;
        var state=Players[player];var definition=Rules.Building(type,state.Setup.Country);
        if(!definition.Buildable || !Map.AllowsRuleObject(definition.RulesId) || type==BuildingType.NavalYard && Map.OriginalDefinition is {ShipyardsAllowed:false} || !Rules.Allowed(definition,state.Setup.Country) || !RequirementsMet(player,definition.Prerequisites??[]))return false;
        if(!Settings.SuperWeapons && (type is BuildingType.Chronosphere or BuildingType.WeatherControl or BuildingType.IronCurtain or BuildingType.NuclearMissile))return false;
        if(definition.MaxInstances>0 && Buildings.Count(building=>building.Alive && building.Owner==player && building.Type==type)>=definition.MaxInstances)return false;
        return true;
    }

    public bool CanQueueBuilding(int player,BuildingType type)=>CanProduceBuilding(player,type)
        && Players[player].BuildingProduction is null;

    // The Lead's domain-aware map integration replaces this single footprint hook
    // for WaterBound shipyards; all callers share the same authoritative decision.
    public bool CanUseBuildingFootprint(BuildingDef definition,int x,int y)=>Map.FootprintFree(definition,x,y);

    public bool CanPlaceBuilding(int player,BuildingType type,int x,int y)
    {
        if(!CanProduceBuilding(player,type) || EntityCount>=Rules.MaxEntities)return false;
        var state=Players[player];if(state.BuildingProduction!=type || !state.BuildingReady || state.BuildingProductionPaused)return false;
        var definition=Rules.Building(type,state.Setup.Country);
        if(!CanUseBuildingFootprint(definition,x,y))return false;
        for(var dy=0;dy<definition.Height;dy++)for(var dx=0;dx<definition.Width;dx++)if(!Explored(player,x+dx,y+dy))return false;
        if(BuildingAreaHasUnits(x,y,definition.Width,definition.Height))return false;
        return Buildings.Any(building=>building.Alive && building.Complete
            && (building.Owner==player || Settings.BuildNearAlly && Allied(player,building.Owner))
            && Math.Abs(building.CellX-x)+Math.Abs(building.CellY-y)<=10);
    }

    bool OrderQueueBuilding(GameCommand command,PlayerState player)
    {
        if(!CanQueueBuilding(player.Id,command.Building))return false;
        ClearClassicBuildingProduction(player);
        player.BuildingProduction=command.Building;return true;
    }

    bool OrderCancelBuilding(PlayerState player)
    {
        if(player.BuildingProduction is null)return false;
        AddClassicCredits(player.Id,player.BuildingProductionPaid);
        ClearClassicBuildingProduction(player);return true;
    }

    static void ClearClassicBuildingProduction(PlayerState player)
    {
        player.BuildingProduction=null;player.BuildingProductionProgress=0;
        player.BuildingProductionPaid=0;player.BuildingProductionPaused=false;
    }

    static void ResetClassicProducerHead(Building producer)
    {
        producer.ProductionProgress=0;producer.ProductionPaid=0;producer.ProductionPaused=false;
    }

    static void ClearClassicProducerQueue(Building producer)
    {
        producer.Queue.Clear();ResetClassicProducerHead(producer);producer.PendingClones.Clear();
    }

    void RefundClassicProducerQueue(Building producer,int payer)
    {
        AddClassicCredits(payer,producer.ProductionPaid);ClearClassicProducerQueue(producer);
    }

    // Pay the cumulative integer share for this next tick before granting progress.
    // Insufficient cash is an automatic wait, separate from the manual pause flag.
    static bool AdvanceClassicPaidProduction(PlayerState player,int cost,int ticks,ref int progress,ref int paid)
    {
        var duration=Math.Max(1,ticks);var next=Math.Min(duration,progress+1);
        var required=(int)(((long)cost*next+duration-1)/duration);
        var due=Math.Max(0,required-paid);
        if(player.Credits<due)return false;
        player.Credits-=due;paid+=due;progress=next;return true;
    }

    bool OrderClassicProductionPause(GameCommand command,PlayerState player,bool building,bool paused)
    {
        if(building)
        {
            if(command.Actors.Length!=0 || player.BuildingProduction is null)return false;
            player.BuildingProductionPaused=paused;return true;
        }
        if(command.Actors.Length!=1 || Find(command.Actors[0]) is not Building producer || producer.Owner!=player.Id
            || !producer.Complete || producer.Queue.Count==0)return false;
        producer.ProductionPaused=paused;return true;
    }

    void UpdateBuildingProduction()
    {
        foreach(var player in Players)
        {
            if(player.Defeated || player.BuildingProduction is not { } type || player.BuildingProductionPaused
                || player.BuildingReady || !CanProduceBuilding(player.Id,type))continue;
            if(!player.Powered && Tick%2!=0)continue;
            var definition=Rules.Building(type,player.Setup.Country);
            AdvanceClassicPaidProduction(player,definition.Cost,definition.BuildTicks,
                ref player.BuildingProductionProgress,ref player.BuildingProductionPaid);
        }
    }
}
