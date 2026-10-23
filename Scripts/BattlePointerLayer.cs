using Godot;

namespace RedAlert;

// Kept after the battle UI in tree order so its _Input runs before radar drag
// handlers. Reinjecting the localized event also updates Godot's real GUI hover
// tree, button states, tooltips and wheel targeting at the visible pointer.
public partial class BattlePointerLayer : CanvasLayer
{
    public required Main Controller {get;init;}
    BattlePointerCanvas? canvas;
    public override void _Ready()
    {
        Layer=1000;canvas=new BattlePointerCanvas{Controller=Controller,MouseFilter=Control.MouseFilterEnum.Ignore,TextureFilter=CanvasItem.TextureFilterEnum.Nearest};AddChild(canvas);
    }
    public override void _Input(InputEvent input)=>Controller.RouteBattlePointer(input);
    public override void _Process(double delta)
    {
        Visible=Controller.OriginalGamePointerVisible;
        if(Visible)canvas?.QueueRedraw();
    }
}

public partial class BattlePointerCanvas : Control
{
    public required Main Controller {get;init;}
    public override void _Draw()
    {
        if(Controller.OriginalGamePointerVisible)Controller.DrawOriginalGamePointer(this);
    }
}
