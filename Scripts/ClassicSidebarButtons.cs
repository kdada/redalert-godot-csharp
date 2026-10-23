using Godot;

namespace RedAlert;

// Transparent hit regions on side3. No replacement panel covers the native art.
public partial class ClassicSidebarButtons : Button
{
    public bool Down { get; set; }

    public override void _Ready()
    {
        FocusMode=FocusModeEnum.None;
        MouseFilter=MouseFilterEnum.Stop;
        MouseDefaultCursorShape=CursorShape.PointingHand;
        TextureFilter=TextureFilterEnum.Nearest;
        using var empty=new StyleBoxEmpty();
        foreach(var state in new[]{"normal","hover","pressed","hover_pressed","disabled","focus"})AddThemeStyleboxOverride(state,empty);
        MouseEntered+=QueueRedraw;MouseExited+=QueueRedraw;ButtonDown+=QueueRedraw;ButtonUp+=QueueRedraw;Resized+=QueueRedraw;
    }

    public override void _Draw()
    {
        var center=Size/2;
        if(IsPressed() && !Disabled)center+=new Vector2(1.5f,1.5f);
        var direction=Down?1:-1;
        Vector2[] arrow=[center+new Vector2(-9,-direction*3.75f),center+new Vector2(9,-direction*3.75f),center+new Vector2(0,direction*5.25f)];
        DrawColoredPolygon(arrow.Select(p=>p+new Vector2(1.5f,1.5f)).ToArray(),new Color(0,0,0,.8f));
        var color=new Color(Disabled?"6c7069":IsHovered()?"f5e7a6":"c7c5a3");
        DrawColoredPolygon(arrow,color);
        if(!Disabled && IsHovered())DrawRect(new Rect2(new Vector2(1.5f,1.5f),Size-new Vector2(3,3)),new Color(.84f,.78f,.55f,.5f),false,1);
    }
}
