namespace RedAlert.Simulation;

public sealed partial class World
{
    void UpdatePower()
    {
        foreach(var player in Players){player.GeneratedPower=0;player.UsedPower=0;}
        foreach(var building in Buildings)
        {
            if(!ClassicBuildingOperational(building) || building.Owner<0 || building.Owner>=Players.Length)continue;
            var player=Players[building.Owner];var power=building.Definition.Power;
            if(power>=0)
            {
                if(player.PowerBlackoutUntil<=Tick)
                    player.GeneratedPower+=(int)((long)power*Math.Min(building.Health,building.MaxHealth)/building.MaxHealth);
            }
            else player.UsedPower-=power;
        }
    }

    void UpdateBuildings()
    {
        UpdateBuildingProduction();
        UpdateClassicPendingUnits();
        foreach(var building in Buildings)
        {
            if(!building.Alive || Players[building.Owner].Defeated)continue;
            var player=Players[building.Owner];
            if(!building.Complete)
            {
                if(building.ErasedById!=0 && Find(building.ErasedById) is not null || !player.Powered && Tick%2!=0)continue;
                var progress=building.TotalBuildTicks-building.RemainingBuildTicks;
                var growth=building.MaxHealth-Math.Max(1,building.MaxHealth/4);
                var healthDelta=(progress+1)*growth/building.TotalBuildTicks-progress*growth/building.TotalBuildTicks;
                building.RemainingBuildTicks--;building.Health=Math.Min(building.MaxHealth,building.Health+healthDelta);
                if(building.Complete)NotifyClassicBuildingCompleted(building);
                continue;
            }
            if(ClassicBuildingOperational(building))
            {
                if(building.Repairing && building.Health<building.MaxHealth && Tick%ClassicEconomyRules.RepairTicks==0)
                {
                    var amount=Math.Min(ClassicEconomyRules.RepairStep,building.MaxHealth-building.Health);
                    var cost=ClassicRepairCost(building.Definition.Cost,building.MaxHealth,amount);
                    if(player.Credits>=cost){player.Credits-=cost;building.Health+=amount;}
                }
                UpdateClassicProducer(building);
            }
            UpdateClassicBuildingCombat(building);
        }
    }

    void UpdateClassicProducer(Building producer)
    {
        if(producer.Queue.Count==0 || producer.ProductionPaused || !Enum.IsDefined(producer.Queue[0]))return;
        var definition=Rules.Unit(producer.Queue[0],producer.FoundationCountry);var player=Players[producer.Owner];
        // A ready paid NAHAND structural-block refund creates no unit/clone;
        // output capacity must not prevent returning this already-spent money.
        if(producer.ProductionProgress>=definition.BuildTicks && producer.ProductionPaid>=definition.Cost
            && definition.Category==ProductionCategory.Infantry)
        {
            var completed=ClassicProductionExit(producer,definition);
            if(completed.Cell is null && ClassicRefundBlockedBarracks(producer,definition,completed.Block))
            {
                AddClassicCredits(producer.Owner,producer.ProductionPaid);
                producer.Queue.RemoveAt(0);ResetClassicProducerHead(producer);return;
            }
        }
        if(EntityCount>=Rules.MaxEntities)return;
        // Queue admission counts the queued unit itself against BuildLimit and
        // airport slots. Never call CanTrainUnit/CanProduceUnit for an existing job.
        if(!ClassicProductionTechnology(producer,definition) || player.PowerBlackoutUntil>Tick)return;
        if(!player.Powered && (producer.Type==BuildingType.NavalYard || Tick%2!=0))return;
        if(definition.Category==ProductionCategory.Infantry && !ClassicCloneBacklogAvailable(producer.Owner))return;
        if(definition.Category==ProductionCategory.Aircraft && !HasClassicAircraftSlot(producer,false))return;
        if(!AdvanceClassicPaidProduction(player,definition.Cost,definition.BuildTicks,
            ref producer.ProductionProgress,ref producer.ProductionPaid))return;
        if(producer.ProductionProgress<definition.BuildTicks)return;
        var result=definition.Domain==MovementDomain.Air
            ?new ProductionExitResult(FindProductionExit(producer,definition),ProductionExitBlock.None)
            :ClassicProductionExit(producer,definition);
        if(result.Cell is not {} exit)
        {
            // Official cumulative vanilla 1.005 fix: a Soviet barracks blocked
            // by walls/buildings refunds this job and creates no original/clone.
            // Unit congestion, vehicles, terrain and other producers wait instead.
            if(ClassicRefundBlockedBarracks(producer,definition,result.Block))
            {
                AddClassicCredits(producer.Owner,producer.ProductionPaid);
                producer.Queue.RemoveAt(0);ResetClassicProducerHead(producer);
            }
            return;
        }
        var unit=SpawnUnit(producer.Owner,definition.Type,exit.X,exit.Y,producer.FoundationCountry);
        InitializeClassicProduct(unit,producer,definition);
        producer.Queue.RemoveAt(0);ResetClassicProducerHead(producer);
        if(definition.Category==ProductionCategory.Infantry)QueueClassicClones(unit,producer.FoundationCountry);
    }

    bool ClassicProductionTechnology(Building producer,UnitDef definition)
    {
        var player=Players[producer.Owner];
        if(!definition.Buildable || !Rules.NationallyAllowed(definition,player.Setup.Country)
            || producer.Type!=Rules.ProducerForUnit(definition.Type,producer.FoundationCountry) || producer.Definition.Produces!=definition.Category
            || definition.Faction is { } faction && faction!=Rules.Side(producer.FoundationCountry)
            || definition.RequiresStolenAlliedTech && !player.StolenAlliedTech || definition.RequiresStolenSovietTech && !player.StolenSovietTech)return false;
        foreach(var requirement in definition.Prerequisites??[])
            if(!Buildings.Any(building=>building.Owner==producer.Owner && ClassicBuildingOperational(building)
                && (building.Type==requirement.Building || requirement.Building==BuildingType.PowerPlant && requirement.Faction is null && building.Type==BuildingType.NuclearReactor)
                && (requirement.Faction is null || Rules.Side(building.FoundationCountry)==requirement.Faction)))return false;
        return true;
    }

    void InitializeClassicProduct(Unit unit,Building producer,UnitDef definition)
    {
        unit.Ammo=definition.Ammo;
        if(ClassicCanGainVeterancy(unit) && (definition.Category==ProductionCategory.Infantry && Players[unit.Owner].VeteranInfantry
            || definition.Category==ProductionCategory.Vehicle && Players[unit.Owner].VeteranVehicles))unit.Kills=Math.Max(unit.Kills,3);
        if(definition.Category==ProductionCategory.Aircraft)
        {
            unit.Order=UnitOrder.Idle;
            if(definition.AirportBound)
            {
                unit.AircraftDockId=producer.Id;
                var pad=ClassicAircraftDockPosition(producer,ClassicAircraftDockSlot(unit,producer))
                    ?? throw new InvalidOperationException("Aircraft production has no reserved airport dock");
                unit.X=pad.X;unit.Y=pad.Y;unit.GoalX=unit.CellX;unit.GoalY=unit.CellY;unit.Altitude=0;
            }
        }
        else if(definition.Miner)unit.Order=UnitOrder.Harvest;
        else SetGoal(unit,producer.RallyX,producer.RallyY,UnitOrder.Move);
    }

    void UpdateHarvester(Unit unit)
    {
        if(!unit.Alive || unit.TransportId!=0 || !ClassicUnitCanMove(unit))return;
        var refinery=Buildings.Where(building=>building.Owner==unit.Owner && building.Type==BuildingType.Refinery && ClassicBuildingOperational(building))
            .OrderBy(building=>DistanceSquared(unit,building)).ThenBy(building=>building.Id).FirstOrDefault();
        if(refinery is null)return;
        var capacity=HarvesterStorageBales(unit.Type);
        // Existing fixtures store Cargo in credits. Preserve that meaning while
        // tracking physical bales separately, so gems consume the same hold space.
        if(unit.Cargo>0 && unit.CargoBales==0)unit.CargoBales=Math.Min(capacity,(unit.Cargo+ClassicEconomyRules.OreValue-1)/ClassicEconomyRules.OreValue);
        if(unit.CargoBales>=capacity)unit.Returning=true;
        if(unit.Returning)
        {
            // A spawn exit excludes every occupied cell, including this miner.
            // Unloading uses a stable terrain-valid dock: arriving must not move
            // that dock to a different cell merely because the miner occupies it.
            var unload=NearestFree(refinery.CellLeft+refinery.Definition.Width+1,refinery.CellTop+refinery.Definition.Height/2,
                domain:unit.Definition.Domain,amphibious:unit.Definition.Amphibious);if(unload is null)return;
            if(unit.Type==UnitType.ChronoMiner && unit.Cargo>0 && !AtClassicHarvesterDock(unit,unload.Value))
            {
                var landing=FindClassicHarvesterLanding(unit,unload.Value);
                if(landing is not null)
                {
                    if(Map.Inside(unit.CellX,unit.CellY)){var old=Map.Index(unit.CellX,unit.CellY);occupied[old]=Math.Max(0,occupied[old]-1);}
                    unit.X=landing.Value.X*Rules.Scale+Rules.Scale/2;unit.Y=landing.Value.Y*Rules.Scale+Rules.Scale/2;
                    occupied[Map.Index(unit.CellX,unit.CellY)]++;unit.Path.Clear();unit.PathIndex=0;unit.PathVersion=-1;
                }
            }
            if(AtClassicHarvesterDock(unit,unload.Value))
            {
                AddClassicCredits(unit.Owner,ClassicOreIncome(unit.Owner,unit.Cargo));
                unit.Cargo=0;unit.CargoBales=0;unit.Returning=false;unit.HarvestTimer=0;unit.PathVersion=-1;
                unit.Path.Clear();unit.PathIndex=0;unit.GoalX=unit.CellX;unit.GoalY=unit.CellY;
            }
            else SetGoal(unit,unload.Value.X,unload.Value.Y,UnitOrder.Harvest);
            return;
        }
        var here=Map.Index(unit.CellX,unit.CellY);
        if(Map.Ore[here]>0)
        {
            unit.GoalX=unit.CellX;unit.GoalY=unit.CellY;unit.Path.Clear();unit.PathIndex=0;
            if(++unit.HarvestTimer>=ClassicEconomyRules.HarvestTicks)
            {
                var value=ClassicResourceValue(unit.CellX,unit.CellY);
                var bales=Math.Min(ClassicEconomyRules.BalesPerHarvest,capacity-unit.CargoBales);
                var amount=Math.Min(bales*value,Map.Ore[here]);unit.HarvestTimer=0;
                unit.Cargo+=amount;unit.CargoBales+=(amount+value-1)/value;Map.Ore[here]-=amount;
            }
            return;
        }
        if(Map.Inside(unit.GoalX,unit.GoalY) && Map.Ore[Map.Index(unit.GoalX,unit.GoalY)]>0 && unit.PathIndex<unit.Path.Count)return;
        var best=-1;var bestDistance=int.MaxValue;
        for(var index=0;index<Map.Ore.Length;index++)
        {
            if(Map.Ore[index]<=0 || !Map.Walkable(index%Map.Width,index/Map.Width))continue;
            var dx=index%Map.Width-unit.CellX;var dy=index/Map.Width-unit.CellY;var distance=dx*dx+dy*dy;
            if(distance<bestDistance){best=index;bestDistance=distance;}
        }
        if(best>=0)SetGoal(unit,best%Map.Width,best/Map.Width,UnitOrder.Harvest);
        else if(unit.Cargo>0)unit.Returning=true;
    }
}
