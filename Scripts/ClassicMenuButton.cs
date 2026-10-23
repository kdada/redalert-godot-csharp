using Godot;

namespace RedAlert;

/// <summary>Native Button input/focus semantics with original SHP states. Rail faces stay inside their metal housing.</summary>
public partial class ClassicMenuButton : Button
{
    public bool RecessedRail {get;set;}
    public Rect2 RailHousingBounds {get;private set;}
    public Rect2 RailFaceBounds {get;private set;}
    public void SetRailBounds(Rect2 housing,Rect2 face)
    {
        RailHousingBounds=housing;RailFaceBounds=face;RecessedRail=true;ClipContents=true;
        Position=face.Position;CustomMinimumSize=face.Size;Size=face.Size;
    }
    Vector2 restingPosition;
    float entryTime=1,entryDelay,emphasis,pressDepth;
    bool hovered,held,positionKnown;
    const float EntryDuration=.18f;

    public override void _Ready()
    {
        SetProcess(false);
        MouseEntered+=()=>{hovered=true;Wake();};MouseExited+=()=>{hovered=false;Wake();};
        FocusEntered+=Wake;FocusExited+=Wake;
        ButtonDown+=()=>{held=true;Wake();};ButtonUp+=()=>{held=false;Wake();};
    }
    public void PlayEntry(float delay=0)
    {
        restingPosition=Position;positionKnown=true;
        if(RecessedRail){entryTime=1;SetProcess(false);QueueRedraw();return;}
        entryTime=0;entryDelay=delay;Position=restingPosition+new Vector2(7,0);SetProcess(true);QueueRedraw();
    }
    void Wake()
    {
        if(RecessedRail){QueueRedraw();return;}
        if(!positionKnown){restingPosition=Position;positionKnown=true;}
        SetProcess(true);QueueRedraw();
    }
    public override void _ExitTree()=>SetProcess(false);
    public override void _Process(double delta)
    {
        var step=(float)delta;entryTime+=step;
        var t=Math.Clamp((entryTime-entryDelay)/EntryDuration,0,1);
        var remaining=1-t;var rollout=remaining*remaining*remaining;
        var target=!Disabled && (hovered || HasFocus())?1f:0f;
        emphasis=Mathf.MoveToward(emphasis,target,step*12);
        var pressed=!Disabled && held?1f:0f;
        pressDepth=Mathf.MoveToward(pressDepth,pressed,step*24);
        Position=restingPosition+new Vector2(7*rollout-emphasis,pressDepth);
        QueueRedraw();
        if(t>=1 && emphasis==target && pressDepth==pressed)SetProcess(false);
    }
    public override void _Draw()
    {
        if(Disabled || RecessedRail)return;
        var t=Math.Clamp((entryTime-entryDelay)/EntryDuration,0,1);
        var intensity=Math.Max(emphasis,4*t*(1-t));
        if(intensity<=.001f)return;
        var color=new Color(1,.88f,.05f,intensity*.85f);
        DrawLine(new Vector2(3,Size.Y-3),new Vector2(Math.Max(3,(Size.X-3)*Math.Max(t,emphasis)),Size.Y-3),color,1);
    }
}
