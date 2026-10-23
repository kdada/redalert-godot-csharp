using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Battlefield
{
    World? oreMemoryWorld;
    int[] rememberedOre=[];
    byte[] rememberedResourceKinds=[];
    int[] rememberedResourceValues=[];
    readonly Dictionary<int,(int Amount,int Kind,int Density,string Sprite)> drawnResourceCells=[];
    int oreMemoryViewer=-2;
    bool oreMemorySpectating;
    int miningBudget;
    Label? resourceQuantityTooltip;
    int resourceTooltipX,resourceTooltipY;
    public int OreTilesDrawn {get;private set;}
    public int OreSpriteTilesDrawn {get;private set;}
    public int OreFallbackTilesDrawn {get;private set;}
    public int MiningEffectCount {get;private set;}
    public int MiningDrawCommands {get;private set;}

    public int GemTilesDrawn {get;private set;}
    public int KnownResourceCellsDrawn=>drawnResourceCells.Count;
    void EnsureOreMemory(World world)
    {
        if(ReferenceEquals(oreMemoryWorld,world) && rememberedOre.Length==world.Map.Ore.Length &&
           oreMemoryViewer==Controller.LocalPlayer && oreMemorySpectating==Controller.IsSpectating)return;
        oreMemoryWorld=world;oreMemoryViewer=Controller.LocalPlayer;oreMemorySpectating=Controller.IsSpectating;
        rememberedOre=new int[world.Map.Ore.Length];Array.Fill(rememberedOre,-1);
        rememberedResourceKinds=new byte[world.Map.Ore.Length];rememberedResourceValues=new int[world.Map.Ore.Length];
    }
    void BeginMiningFrame(World world)
    {
        OreTilesDrawn=0;OreSpriteTilesDrawn=0;OreFallbackTilesDrawn=0;GemTilesDrawn=0;MiningEffectCount=0;MiningDrawCommands=0;
        drawnResourceCells.Clear();miningBudget=Controller.VisualDetail>=2?256:Controller.VisualDetail==1?128:64;EnsureOreMemory(world);
        if(resourceQuantityTooltip is {} tooltip && GodotObject.IsInstanceValid(tooltip) && tooltip.IsInsideTree())
        {
            tooltip.Text=ResourceDescriptionAtCell(resourceTooltipX,resourceTooltipY);
            // An empty Label leaves the native tooltip's padded frame visible.
            // Hide only its actual PopupPanel; never hide the game/main Window.
            if(tooltip.Text.Length==0 && tooltip.GetWindow() is PopupPanel popup)popup.Hide();
        }
    }
    public static int ResourceDensityForAmount(int amount,int value)=>amount<=0?-1:(int)Math.Clamp(((long)amount-1)/(Math.Max(1,value)*4L),0,11);
    bool KnownResource(World world,int x,int y,out int amount,out int kind,out int value,out bool live)
    {
        amount=kind=value=0;live=false;
        if(!world.Map.IsPlayable(x,y) || !Controller.ViewExplored(x,y))return false;
        EnsureOreMemory(world);var index=world.Map.Index(x,y);live=Controller.ViewVisible(x,y);
        if(live)
        {
            // This is the sole authoritative-amount read. Fogged cells use only
            // the last value the local viewer actually observed on screen/hover.
            amount=world.Map.Ore[index];var source=world.Map.OriginalCellAt(x,y);
            kind=source is {ResourceKind:>0}?source.ResourceKind:amount>0?1:rememberedResourceKinds[index];
            value=source is {ResourceValue:>0}?source.ResourceValue:kind==2?50:25;
            rememberedOre[index]=amount;rememberedResourceKinds[index]=(byte)kind;rememberedResourceValues[index]=value;
        }
        else
        {
            if(rememberedOre[index]<0 || rememberedResourceKinds[index]==0)return false;
            amount=rememberedOre[index];kind=rememberedResourceKinds[index];value=rememberedResourceValues[index];
        }
        return kind>0;
    }
    public string ResourceDescriptionAtCell(int x,int y)
    {
        var world=Controller.Battle;
        if(world is null || !KnownResource(world,x,y,out var amount,out var kind,out var value,out var live))return "";
        var title=kind==2?"水晶":"矿石";var record=live?"":"\n上次侦察记录，当前余量未知";
        return amount<=0?$"{title}已耗尽\n剩余 0 资金{record}":$"{title} · 剩余 {amount:N0} 资金\n约 {(amount+(long)value-1)/value:N0} 包 · 每包 {value} 资金{record}";
    }
    public string ResourceDescriptionAtPoint(Vector2 point)
    {var cell=UnprojectGround(point);return ResourceDescriptionAtCell((int)MathF.Floor(cell.X),(int)MathF.Floor(cell.Y));}
    public override string _GetTooltip(Vector2 atPosition)
    {
        var cell=UnprojectGround(atPosition);resourceTooltipX=(int)MathF.Floor(cell.X);resourceTooltipY=(int)MathF.Floor(cell.Y);
        return ResourceDescriptionAtCell(resourceTooltipX,resourceTooltipY);
    }
    public override GodotObject _MakeCustomTooltip(string forText)
    {
        resourceQuantityTooltip=new Label{Name="ResourceQuantityTooltip",Text=forText,ThemeTypeVariation="TooltipLabel",MouseFilter=MouseFilterEnum.Ignore};
        return resourceQuantityTooltip;
    }
    public Godot.Collections.Dictionary GetResourceTooltipAudit()
    {
        var shown=resourceQuantityTooltip is {} label && GodotObject.IsInstanceValid(label) && label.IsVisibleInTree();
        return new(){["visible"]=shown,["text"]=shown?resourceQuantityTooltip!.Text:"",["cellX"]=resourceTooltipX,["cellY"]=resourceTooltipY};
    }
    public Godot.Collections.Dictionary GetResourceCellAudit(int x,int y)
    {
        var world=Controller.Battle;
        if(world is null || !KnownResource(world,x,y,out var amount,out var kind,out var value,out var live))return new(){["known"]=false,["visible"]=false};
        var density=ResourceDensityForAmount(amount,value);var overlay=world.Map.OriginalCellAt(x,y)?.OverlayName??"";var noise=BattleMap.Noise(x,y,world.Settings.Seed);
        var drawn=drawnResourceCells.TryGetValue(world.Map.Index(x,y),out var image);
        return new(){["known"]=true,["visible"]=live,["resourceKind"]=kind,["amount"]=amount,["valuePerBale"]=value,["density"]=density,["depleted"]=amount<=0,["tooltip"]=ResourceDescriptionAtCell(x,y),
            ["sprite"]=Controller.Graphics.OriginalResourceSpriteName(overlay,kind,noise),["drawn"]=drawn,["drawnAmount"]=drawn?image.Amount:-1,["drawnDensity"]=drawn?image.Density:-1};
    }
    void DrawOreTile(World world,int x,int y,Vector2 point,uint noise,bool visible,Color light)
    {
        if(!KnownResource(world,x,y,out var amount,out var kind,out var value,out _) || amount<=0)return;
        var density=ResourceDensityForAmount(amount,value);var overlay=world.Map.OriginalCellAt(x,y)?.OverlayName??"";
        var sprite=Controller.Graphics.OriginalResourceSpriteName(overlay,kind,noise);
        if(Controller.Graphics.DrawResourcePatch(this,overlay,kind,noise,density,point,Zoom,light))OreSpriteTilesDrawn++;
        else
        {
            OreFallbackTilesDrawn++;
            var dark=new Color(kind==2?"584399":"9e7130")*light;var bright=new Color(kind==2?"b89bfa":"ead16b")*light;
            for(var i=0;i<3+density/3;i++)
            {
                var dx=(int)(BattleMap.Noise(x+i*7,y,noise)%29)-14;var dy=(int)(BattleMap.Noise(x,y+i*11,noise)%11)-5;var p=point+new Vector2(dx,dy)*Zoom;
                DrawRect(new Rect2(p-new Vector2(3,1)*Zoom,new Vector2(6,4)*Zoom),dark);
                DrawRect(new Rect2(p-new Vector2(2,1)*Zoom,new Vector2(4,2)*Zoom),bright);
            }
        }
        drawnResourceCells[world.Map.Index(x,y)]=(amount,kind,density,sprite);OreTilesDrawn++;if(kind==2)GemTilesDrawn++;
    }
    bool SpendMiningDraw(int count=1)
    {
        if(miningBudget<count)return false;
        miningBudget-=count;MiningDrawCommands+=count;return true;
    }
    void MiningLine(Vector2 a,Vector2 b,Color color,float width)
    {
        if(SpendMiningDraw())DrawLine(a,b,color,Math.Max(1,width*Zoom));
    }
    void MiningNugget(Vector2 point,Color color)
    {
        if(SpendMiningDraw())DrawRect(new Rect2(point-new Vector2(1,1)*Zoom,new Vector2(2,2)*Zoom),color);
    }
    bool ExtractingOre(World world,Unit unit)
    {
        return unit.Alive && unit.Definition.Miner && unit.Order==UnitOrder.Harvest && !unit.Returning && unit.CargoBales<World.HarvesterStorageBales(unit.Type) &&
            unit.PathIndex>=unit.Path.Count && unit.GoalX==unit.CellX && unit.GoalY==unit.CellY &&
            world.Map.IsPlayable(unit.CellX,unit.CellY) && world.Map.Ore[world.Map.Index(unit.CellX,unit.CellY)]>0 &&
            Controller.ViewVisible(unit.CellX,unit.CellY) && world.HasBuilding(unit.Owner,BuildingType.Refinery);
    }
    void DrawMiningActivity(Unit unit,Vector2 point)
    {
        var world=Controller.Battle;if(world is null || miningBudget<6 || !ExtractingOre(world,unit))return;
        if(point.X < -60 || point.Y < -60 || point.X>Size.X+60 || point.Y>Size.Y+60)return;
        MiningEffectCount++;
        var facing=new Vector2(unit.BodyFacingX-unit.BodyFacingY,(unit.BodyFacingX+unit.BodyFacingY)*.5f).Normalized();
        if(facing==Vector2.Zero)facing=Vector2.Right;
        // The actual five-tick extraction timer drives the scoop. A moving or
        // returning harvester never enters this draw path, even with old cargo.
        var phase=Math.Clamp(unit.HarvestTimer/5f,0,1);
        var hinge=point+(facing*5-new Vector2(0,10))*Zoom;
        var tip=point+(facing*18+new Vector2(0,3-MathF.Sin(phase*MathF.PI)*3))*Zoom;
        var elbow=hinge.Lerp(tip,.55f)-new Vector2(0,3+phase*3)*Zoom;
        MiningLine(hinge,elbow,new Color("403b2c"),3);
        MiningLine(elbow,tip,new Color("403b2c"),3);
        MiningLine(hinge,elbow,new Color("b1a77d"),1);
        MiningLine(elbow,tip,new Color("d4bd6c"),1);
        MiningLine(tip-new Vector2(3,0)*Zoom,tip+new Vector2(3,0)*Zoom,new Color("f0d979"),2);
        var hopper=point-new Vector2(0,11)*Zoom;
        if(Controller.VisualDetail>0)
        for(var i=0;i<(Controller.VisualDetail>=2?3:1);i++)
        {
            var t=((world.Tick+i*3+unit.Id)%10)/10f;
            var p=tip.Lerp(hopper,t)-new Vector2(0,MathF.Sin(t*MathF.PI)*7)*Zoom;
            MiningNugget(p,new Color("f2cf63"));
        }
        if(Controller.VisualDetail>=2 && unit.HarvestTimer>=2 && SpendMiningDraw())
            Controller.Graphics.DrawCombatSprite(this,ArtLibrary.CombatSprite.Dust,tip,.26f*Zoom,phase,.55f);
        if(SpendMiningDraw())DrawRect(new Rect2(hopper+new Vector2(-9,-7)*Zoom,new Vector2(18,4)*Zoom),new Color(0,0,0,.7f));
        var loaded=Math.Clamp((unit.CargoBales*5+World.HarvesterStorageBales(unit.Type)-1)/World.HarvesterStorageBales(unit.Type),0,5);
        for(var i=0;i<loaded;i++)MiningNugget(hopper+new Vector2(-7+i*3,-5)*Zoom,new Color("e3c258"));
    }
}
