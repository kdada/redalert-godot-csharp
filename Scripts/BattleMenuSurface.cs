using Godot;

namespace RedAlert;

// Original retained pudlgbga/pudlgbgs SHP canvases, decoded with dialog.pal:
// 451x326, including the silver border and faction crest. Source fingerprints
// and byte ranges are in Assets/Generated/Menu/catalog.json, entries pudlgbga/s.
// No screenshot reconstruction, procedural steel panel or faction recoloring.
public partial class BattleMenuSurface : Control
{
    public static readonly Vector2 NativeSize=new(451,326);
    public Texture2D? Artwork {get;init;}
    public Control? Stage {get;private set;}
    public TextureRect? ArtworkControl {get;private set;}

    public void SetStage(Control stage)
    {
        Stage=stage;AddChild(stage);stage.Size=NativeSize;
        ArtworkControl=new TextureRect{Name="MatchDialogArtwork",Texture=Artwork,Size=NativeSize,
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale,
            MouseFilter=MouseFilterEnum.Ignore,TextureFilter=TextureFilterEnum.Nearest};
        stage.AddChild(ArtworkControl);LayoutStage();
    }
    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Ignore;TextureFilter=TextureFilterEnum.Nearest;
        Resized+=LayoutStage;LayoutStage();
    }
    void LayoutStage()
    {
        if(Stage is null)return;
        var scale=Math.Clamp(Math.Min((Size.X-32)/NativeSize.X,(Size.Y-32)/NativeSize.Y),.35f,Battlefield.ClassicZoom);
        Stage.Scale=Vector2.One*scale;Stage.Position=(Size-NativeSize*scale)/2;
        QueueRedraw();
    }
    public static Button NativeButton(ArtLibrary art,string name,string text,Action pressed)
    {
        var button=new Button{Name=name,Text=text,CustomMinimumSize=new Vector2(126,25),Size=new Vector2(126,25),
            FocusMode=FocusModeEnum.None,MouseDefaultCursorShape=CursorShape.PointingHand,TextureFilter=TextureFilterEnum.Nearest,
            ClipText=true,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
        foreach(var (state,index) in new[]{("normal",0),("hover",1),("pressed",2),("hover_pressed",2),("disabled",0)})
        {
            if(art.MenuButtonFrame(index) is not {} texture)continue;
            using var box=new StyleBoxTexture{Texture=texture,ModulateColor=state=="disabled"?new Color(.3f,.3f,.3f):Colors.White};
            box.SetTextureMarginAll(2);box.SetContentMarginAll(0);button.AddThemeStyleboxOverride(state,box);
        }
        using(var empty=new StyleBoxEmpty())button.AddThemeStyleboxOverride("focus",empty);
        button.AddThemeFontSizeOverride("font_size",12);
        button.AddThemeColorOverride("font_color",new Color("ffff00"));
        button.AddThemeColorOverride("font_hover_color",new Color("ffff00"));
        button.AddThemeColorOverride("font_pressed_color",new Color("5c1600"));
        button.AddThemeColorOverride("font_disabled_color",new Color("85806b"));
        button.AddThemeColorOverride("font_outline_color",new Color(0,0,0,.75f));
        button.AddThemeConstantOverride("outline_size",1);
        button.Pressed+=pressed;return button;
    }
}
