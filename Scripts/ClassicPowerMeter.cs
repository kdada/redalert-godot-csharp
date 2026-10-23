using Godot;

namespace RedAlert;

// Presentation only: the original side2 gutter remains visible around this gauge.
public partial class ClassicPowerMeter : Control
{
    int generated;
    int used;

    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Pass;
        TextureFilter=TextureFilterEnum.Nearest;
        Resized+=QueueRedraw;
    }

    public void SetPower(int generation,int demand)
    {
        generation=Math.Max(0,generation);demand=Math.Max(0,demand);
        TooltipText=$"电力产出 {generation} / 需求 {demand}\n{(generation<demand?"电力不足":"电力储备")} {Math.Abs(generation-demand)}\n色条：产出；横线：需求";
        if(generated==generation && used==demand)return;
        generated=generation;used=demand;QueueRedraw();
    }

    public override void _Draw()
    {
        if(Size.Y<6 || Size.X<6)return;
        var low=used>generated;
        var ink=new Color(low?"ef493a":used>generated*.9f && used>0?"dfbb4c":"65c543");
        var extent=(float)Math.Max(200,Math.Ceiling(Math.Max(generated,used)*1.25/100)*100);
        var bottom=Size.Y-3;var height=Math.Max(1,bottom-3);
        var generationTop=bottom-height*generated/extent;
        // side2's dark groove is native x=11..15; at 1.5 scale this fills
        // only its narrow interior, leaving the original metal rails untouched.
        DrawRect(new Rect2(3,3,6,height),new Color("11160d"));
        for(var y=bottom-3;y>=3;y-=6)
        {
            var cell=new Rect2(3,y,6,3);
            DrawRect(cell,generated>0 && y+3>generationTop?ink:new Color(low?"48221c":"24301b"));
        }
        for(var y=bottom;y>=3;y-=15)DrawLine(new Vector2(0,y),new Vector2(3,y),new Color(low?"bd6653":"8a9873"),1);
        var demandY=Math.Clamp(bottom-height*used/extent,3,bottom);
        var marker=new Color(low?"ff8b66":"e5d996");
        DrawLine(new Vector2(0,demandY),new Vector2(12,demandY),marker,1.5f);
        DrawColoredPolygon([new Vector2(0,demandY-3),new Vector2(3,demandY),new Vector2(0,demandY+3)],marker);
    }
}
