using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    readonly List<(CameoButton Button,BuildingType? Power)> classicPowerProducts=[];
    BuildingType? selectedClassicPower;
    bool classicChronoDestination;
    static int ClassicPowerReady(PlayerState player,BuildingType type)=>type switch
    {
        BuildingType.IronCurtain=>player.IronCurtainReadyTick,BuildingType.Chronosphere=>player.ChronosphereReadyTick,
        BuildingType.WeatherControl=>player.WeatherReadyTick,BuildingType.NuclearMissile=>player.NukeReadyTick,_=>player.SupportReadyTick
    };
    partial void AppendClassicPowerProducts()
    {
        classicPowerProducts.Clear();if(Battle is not { } world || products is null || LocalPlayer<0)return;
        var player=world.Players[LocalPlayer];
        void Add(BuildingType? type,string title,string cameo)
        {
            var button=new CameoButton{Name="ClassicPower"+(type?.ToString()??"Paratroopers"),Title=title,Price=0,Soviet=sovietUi,Picture=Graphics.Cameo(cameo),CustomMinimumSize=new Vector2(90,72),SizeFlagsHorizontal=SizeFlags.ShrinkBegin,FocusMode=FocusModeEnum.None};
            button.TooltipText=type==BuildingType.Chronosphere?"超时空传送：先选择部队所在区域，再选择目的地；右键取消。":title+"：充能完成后点击，再在战场选择目标。";
            button.Pressed+=()=>
            {
                selectedClassicPower=type;classicChronoDestination=type==BuildingType.Chronosphere && player.ChronosphereSelectingDestination;
                battlefield!.CancelMaintenance();battlefield.Placement=null;battlefield.AttackMode=false;battlefield.SupportMode=true;
                Notify(type==BuildingType.Chronosphere?(classicChronoDestination?"选择传送目的地":"选择要传送的部队区域"):$"选择{title}目标");
            };
            products.AddChild(button);productButtons.Add((button,null,null));classicPowerProducts.Add((button,type));
        }
        if(player.Setup.Country==Country.America && world.Buildings.Any(b=>b.Alive && b.Complete && b.Owner==LocalPlayer && b.Definition.AirCapacity>0))Add(null,"空降伞兵","aparicon");
        if(world.Settings.SuperWeapons)
        foreach(var (type,icon) in new[]{(BuildingType.IronCurtain,"ircricon"),(BuildingType.Chronosphere,"chroicon"),(BuildingType.WeatherControl,"bolticon"),(BuildingType.NuclearMissile,"nukeicon")})
            if(world.HasBuilding(LocalPlayer,type))Add(type,Rules.Building(type,player.Setup.Country).Name,icon);
    }
    partial void RefreshClassicPowerProducts()
    {
        if(Battle is not { } world || LocalPlayer<0)return;var player=world.Players[LocalPlayer];
        foreach(var item in classicPowerProducts)
        {
            if(!GodotObject.IsInstanceValid(item.Button))continue;
            var ready=item.Power is { } type?ClassicPowerReady(player,type):player.SupportReadyTick;
            var available=item.Power is { } power?world.Settings.SuperWeapons && world.Buildings.Any(b=>b.Owner==LocalPlayer && b.Type==power && world.ClassicBuildingOperational(b)):
                player.Setup.Country==Country.America && world.Buildings.Any(b=>b.Owner==LocalPlayer && b.Definition.AirCapacity>0 && world.ClassicBuildingOperational(b));
            item.Button.Disabled=IsSpectating || ready<0 || world.Tick<ready || !player.Powered || !available;
            var duration=item.Power switch{BuildingType.IronCurtain=>1500,BuildingType.Chronosphere=>2100,BuildingType.WeatherControl or BuildingType.NuclearMissile=>3000,_=>1200};
            item.Button.SetProduction(ready<0?0:Math.Clamp(1-(ready-world.Tick)/(float)duration,0,1),0,ready>=0 && world.Tick>=ready);
        }
    }
    public void IssueClassicPowerAt(int x,int y)
    {
        if(Battle is not { } world || LocalPlayer<0 || battlefield is null)return;
        if(selectedClassicPower is not { } type)
        {Issue(new GameCommand{Type=CommandType.Support,X=x,Y=y});battlefield.SupportMode=false;return;}
        var building=world.Buildings.FirstOrDefault(b=>b.Owner==LocalPlayer && b.Type==type && world.ClassicBuildingOperational(b));
        if(building is null){CancelClassicPowerTarget();return;}
        Issue(new GameCommand{Type=CommandType.SuperWeapon,Actors=[building.Id],Building=type,X=x,Y=y});
        if(type==BuildingType.Chronosphere && !classicChronoDestination){classicChronoDestination=true;Notify("选择传送目的地");}
        else {selectedClassicPower=null;classicChronoDestination=false;battlefield.SupportMode=false;}
    }
    public void CancelClassicPowerTarget()
    {
        if(selectedClassicPower==BuildingType.Chronosphere && Battle is { } world)
        {
            var source=world.Buildings.FirstOrDefault(b=>b.Owner==LocalPlayer && b.Type==BuildingType.Chronosphere && b.Alive);
            if(source is not null)Issue(new GameCommand{Type=CommandType.SuperWeapon,Actors=[source.Id],Target=-1});
        }
        selectedClassicPower=null;classicChronoDestination=false;if(battlefield is not null)battlefield.SupportMode=false;
    }
}
