using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    partial void AppendClassicPowerProducts();
    partial void RefreshClassicPowerProducts();
    // Keep the four native tabs. Movement domain is deliberately independent:
    // Rocketeers remain infantry; factory-built Kirovs remain vehicles.
    static bool ClassicUnitBelongsToTab(UnitType type,string tab)=>tab switch
    {
        "infantry"=>Rules.Production(type)==ProductionCategory.Infantry,
        "vehicle"=>Rules.Production(type)!=ProductionCategory.Infantry,
        _=>false
    };

    static string ClassicCategoryName(ProductionCategory category)=>category switch
    {
        ProductionCategory.Infantry=>"步兵",
        ProductionCategory.Aircraft=>"飞机",
        ProductionCategory.Naval=>"舰船",
        _=>"机械"
    };

    static string ClassicRequirementText(IEnumerable<PrerequisiteDef> requirements,Country country)
        =>string.Join("、",requirements.Select(requirement=>
        {
            var origin=requirement.Faction switch{Faction.Allied=>Country.America,Faction.Soviet=>Country.Russia,_=>country};
            var name=Rules.Building(requirement.Building,origin).Name;
            return requirement.Faction is { } faction?$"{(faction==Faction.Allied?"盟军":"苏军")}{name}":name;
        }).Distinct());

    void RefreshClassicProducts()
    {
        if(Battle is not { } world || products is null || LocalPlayer<0)return;
        var player=world.Players[LocalPlayer];var country=player.Setup.Country;
        var completed=string.Join(",",world.Buildings.Where(b=>b.Alive && b.Owner==LocalPlayer && b.Complete)
            .Select(b=>$"{(int)b.Type}:{Rules.Side(b.FoundationCountry)}").Distinct().OrderBy(value=>value,StringComparer.Ordinal));
        var stolen=$"{player.StolenAlliedTech}/{player.StolenSovietTech}";
        var key=$"classic/{productTab}/{country}/{completed}/{stolen}/{player.Defeated}/{world.Finished}";
        if(lastProductKey==key){RefreshClassicProductProgress();return;}
        lastProductKey=key;
        foreach(var child in products.GetChildren()){products.RemoveChild(child);child.QueueFree();}
        productButtons.Clear();
        foreach(var tab in productTabs)tab.Value.SetPressedNoSignal(tab.Key==productTab);
        productPage=0;if(productionScroll is not null)productionScroll.ScrollVertical=0;
        if(productTab is "build" or "defense")
        {
            foreach(var entry in Rules.Buildings)
            {
                var def=Rules.Building(entry.Type,country);
                if(!def.Buildable || def.Type==BuildingType.ConstructionYard || !Rules.Allowed(def,country) || def.IsDefense!=(productTab=="defense"))continue;
                var type=def.Type;
                var button=new CameoButton{Name="ClassicBuilding"+type,Title=def.Name,Price=def.Cost,Soviet=sovietUi,
                    Picture=Graphics.Cameo(def.Cameo),CustomMinimumSize=new Vector2(90,72),
                    SizeFlagsHorizontal=SizeFlags.ShrinkBegin,FocusMode=FocusModeEnum.None};
                button.Pressed+=()=>SelectBuildingProduct(type);
                button.GuiInput+=input=>
                {
                    if(input is not InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Right} || player.BuildingProduction!=type)return;
                    Issue(new GameCommand{Type=player.BuildingReady || player.BuildingProductionPaused?CommandType.CancelBuilding:CommandType.PauseBuilding});button.AcceptEvent();
                };
                var requirements=ClassicRequirementText(Rules.BuildingRequirements(type,country),country);
                button.TooltipText=$"{def.Name}  /  $ {def.Cost:N0}\n需求：{requirements}\n准备 {def.BuildTicks/(float)Rules.TicksPerSecond:0.#}s · 随进度扣款，缺资自动等待\n右键暂停 · 左键继续 · 再次右键取消并退已付款；就绪后点击选址\n占地 {def.Width}×{def.Height} · 电力 {def.Power:+0;-0;0}";
                products.AddChild(button);productButtons.Add((button,null,type));
            }
            if(productTab=="defense")AppendClassicPowerProducts();
        }
        else
        {
            foreach(var entry in Rules.Units.Where(d=>ClassicUnitBelongsToTab(d.Type,productTab)).OrderBy(d=>Rules.Production(d.Type)))
            {
                var def=Rules.Unit(entry.Type,country);
                if(!world.CanSeeUnit(LocalPlayer,def.Type))continue;
                if(!Rules.Allowed(def,country) && !world.CanProduceUnit(LocalPlayer,def.Type))continue;
                var type=def.Type;var producer=Rules.ProducerForUnit(type,country);
                var button=new CameoButton{Name="ClassicUnit"+type,Title=def.Name,Price=def.Cost,Soviet=sovietUi,
                    Picture=Graphics.Cameo(def.Cameo),CustomMinimumSize=new Vector2(90,72),
                    SizeFlagsHorizontal=SizeFlags.ShrinkBegin,FocusMode=FocusModeEnum.None};
                button.Pressed+=()=>TrainClassicProduct(type);
                button.GuiInput+=input=>
                {
                    if(input is not InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Right})return;
                    CancelClassicUnitQueue(type);button.AcceptEvent();
                };
                var requirements=ClassicRequirementText(Rules.UnitRequirements(type,country),country);
                button.TooltipText=$"{def.Name}  /  $ {def.Cost:N0}\n{ClassicCategoryName(Rules.Production(type))} · 生产：{Rules.Building(producer,country).Name}\n需求：{requirements}\n生产 {def.BuildTicks/(float)Rules.TicksPerSecond:0.#}s · 耐久 {def.Health} · 射程 {def.Range}\n随进度扣款，缺资自动等待\n在产项：右键暂停 · 左键继续 · 再次右键取消并退已付款";
                products.AddChild(button);productButtons.Add((button,type,null));
            }
        }
        RefreshClassicProductProgress();RefreshSidebarScroll();
    }

    void RefreshClassicProductProgress()
    {
        if(Battle is not { } world || battlefield is null || LocalPlayer<0)return;
        var player=world.Players[LocalPlayer];var country=player.Setup.Country;
        var factories=world.Buildings.Where(b=>b.Alive && b.Owner==LocalPlayer && b.Complete).ToArray();
        foreach(var product in productButtons)
        {
            var progress=-1f;var count=0;var active=false;
            if(product.Unit is { } unit)
            {
                var def=Rules.Unit(unit,country);count=factories.Sum(b=>b.Queue.Count(t=>t==unit));
                var producer=factories.Where(b=>b.Queue.Count>0 && b.Queue[0]==unit)
                    .OrderByDescending(b=>battlefield.Selection.Contains(b.Id)).ThenByDescending(b=>b.ProductionPaused)
                    .ThenByDescending(b=>b.ProductionProgress).ThenBy(b=>b.Id).FirstOrDefault();
                active=producer is not null;
                if(producer is not null)progress=Math.Clamp(producer.ProductionProgress/(float)Math.Max(1,Rules.Unit(unit,producer.FoundationCountry).BuildTicks),0,1);
                product.Button.Title=def.Name+(producer?.ProductionPaused==true?" · 暂停":"");
                product.Button.Disabled=IsSpectating || (count==0 && !world.CanProduceUnit(LocalPlayer,unit));
            }
            else if(product.Building is { } type)
            {
                var def=Rules.Building(type,country);active=player.BuildingProduction==type;
                if(active){progress=Math.Clamp(player.BuildingProductionProgress/(float)Math.Max(1,def.BuildTicks),0,1);count=1;}
                product.Button.Title=def.Name+(active && player.BuildingProductionPaused?" · 暂停":active && player.BuildingReady?" · 就绪":"");
                product.Button.Disabled=IsSpectating || (!active && !world.CanQueueBuilding(LocalPlayer,type));
                if(battlefield.Placement==type && (!active || !player.BuildingReady || player.BuildingProductionPaused))battlefield.Placement=null;
            }
            product.Button.SetProduction(progress,count,active);
        }
        if(productTab=="defense")RefreshClassicPowerProducts();
    }

    void TrainClassicProduct(UnitType type)
    {
        if(Battle is not { } world || battlefield is null || LocalPlayer<0 || IsSpectating)return;
        var country=world.Players[LocalPlayer].Setup.Country;var def=Rules.Unit(type,country);
        var producerType=Rules.ProducerForUnit(type,country);
        var paused=world.Buildings.Where(b=>b.Alive && b.Owner==LocalPlayer && b.Complete && b.ProductionPaused && b.Queue.Count>0 && b.Queue[0]==type)
            .OrderByDescending(b=>battlefield.Selection.Contains(b.Id)).ThenByDescending(b=>b.ProductionProgress).ThenBy(b=>b.Id).FirstOrDefault();
        if(paused is not null){Issue(new GameCommand{Type=CommandType.ResumeQueue,Actors=[paused.Id]});return;}
        if(!world.CanProduceUnit(LocalPlayer,type))
        {Notify($"{def.Name}：需要 {ClassicRequirementText(Rules.UnitRequirements(type,country),country)}，并建成{Rules.Building(producerType,country).Name}");return;}
        var candidates=world.Buildings.Where(b=>b.Alive && b.Owner==LocalPlayer && b.Complete && b.Type==producerType && world.CanTrainUnit(LocalPlayer,type,b))
            .OrderByDescending(b=>battlefield.Selection.Contains(b.Id)).ThenBy(b=>b.Queue.Count).ThenBy(b=>b.Id).ToArray();
        if(candidates.Length==0){Notify($"{Rules.Building(producerType,country).Name}队列或容量已满");return;}
        Issue(new GameCommand{Type=CommandType.Train,Actors=[candidates[0].Id],Unit=type});
        Notify($"请求生产 {def.Name}");
    }

    void CancelClassicUnitQueue(UnitType type)
    {
        if(Battle is not { } world || LocalPlayer<0 || IsSpectating)return;
        var candidates=world.Buildings.Where(b=>b.Alive && b.Owner==LocalPlayer && b.Complete && b.Queue.Contains(type)).ToArray();
        var active=candidates.Where(b=>b.Queue[0]==type)
            .OrderByDescending(b=>battlefield?.Selection.Contains(b.Id)==true).ThenByDescending(b=>b.ProductionPaused)
            .ThenByDescending(b=>b.ProductionProgress).ThenBy(b=>b.Id).FirstOrDefault();
        if(active is not null)
        {
            Issue(new GameCommand{Type=active.ProductionPaused?CommandType.CancelQueue:CommandType.PauseQueue,
                Actors=[active.Id],Target=active.ProductionPaused?1:0});return;
        }
        var producer=candidates.OrderByDescending(b=>battlefield?.Selection.Contains(b.Id)==true).ThenByDescending(b=>b.Id).FirstOrDefault();
        if(producer is null){Notify("当前没有该单位的排队项");return;}
        Issue(new GameCommand{Type=CommandType.CancelQueue,Actors=[producer.Id],Target=producer.Queue.LastIndexOf(type)+1});
    }

    public Godot.Collections.Dictionary GetClassicProductionAudit()
    {
        var counts=new Godot.Collections.Dictionary();
        foreach(var category in Enum.GetValues<ProductionCategory>())counts[category.ToString()]=productButtons.Count(p=>p.Unit is { } type && Rules.Production(type)==category);
        return new Godot.Collections.Dictionary{{"tab",productTab},{"categories",counts},{"buildings",productButtons.Count(p=>p.Building is not null)},
            {"country",Battle is { } world && LocalPlayer>=0?world.Players[LocalPlayer].Setup.Country.ToString():""},
            {"catalogCount",productButtons.Count},{"enabledCount",productButtons.Count(p=>!p.Button.Disabled)}};
    }
}
