using Godot;

namespace RedAlert;

public partial class ClassicMenuCanvas : Control
{
    public Texture2D? Background {get;set;}
    public Texture2D? Header {get;set;}
    public Control? Stage {get;private set;}
    public static readonly Vector2 CanvasSize=new(800,600);
    public static readonly Rect2 HeaderBounds=new(632,0,168,198);
    public static readonly Rect2 TitleBackdropBounds=new(658,3,116,29);
    public static Rect2 RailHousingBounds(float y)=>new(641,y,150,43);
    public static Rect2 RailFaceBounds(float y)=>new(653,y+9,126,25);
    public Rect2 CanvasBounds=>Stage is null?new Rect2():new Rect2(Stage.Position,CanvasSize*Stage.Scale);
    public Rect2 ToCanvasBounds(Rect2 nativeBounds)=>Stage is null?new Rect2():new Rect2(Stage.Position+nativeBounds.Position*Stage.Scale,nativeBounds.Size*Stage.Scale);
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;Resized+=LayoutStage;LayoutStage();}
    public void SetStage(Control stage){Stage=stage;AddChild(stage);stage.Size=CanvasSize;LayoutStage();}
    public void PlayEntry()
    {
        if(Stage is null)return;
        var order=0;
        foreach(var child in Stage.GetChildren())
        {
            if(child is ClassicMenuButton button)button.PlayEntry(Math.Min(order++,5)*.014f);
            else if(child is ClassicMenuFrame frame)frame.PlayEntry();
        }
    }
    void LayoutStage()
    {
        if(Stage is not null)
        {
            var scale=Math.Max(.35f,Math.Min(Size.X/CanvasSize.X,Size.Y/CanvasSize.Y));
            Stage.Scale=Vector2.One*scale;Stage.Position=(Size-CanvasSize*scale)/2;
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),Colors.Black);
        if(Stage is null)return;
        if(Background is not null)DrawTextureRect(Background,CanvasBounds,false);
        if(Header is not null)DrawTextureRect(Header,ToCanvasBounds(HeaderBounds),false);
    }
}

public partial class ClassicMenuFrame : Control
{
    public bool Recessed {get;set;}
    float rollout=1;
    public override void _Ready(){MouseFilter=MouseFilterEnum.Ignore;Resized+=QueueRedraw;SetProcess(false);}
    public void PlayEntry(){rollout=0;SetProcess(true);QueueRedraw();}
    public override void _Process(double delta)
    {
        rollout=Math.Min(1,rollout+(float)delta/.16f);QueueRedraw();
        if(rollout>=1)SetProcess(false);
    }
    public override void _Draw()
    {
        var progress=1-(1-rollout)*(1-rollout)*(1-rollout);
        var width=Math.Max(1,Size.X*progress);var rect=new Rect2(0,0,width,Size.Y);
        DrawRect(rect,new Color(.015f,0,0,.76f));DrawRect(rect,new Color("a00000"),false,1);
        if(!Recessed && width>4 && Size.Y>4)DrawRect(new Rect2(2,2,width-4,Size.Y-4),new Color("3c0000"),false,1);
        if(rollout<1)DrawLine(new Vector2(width-1,1),new Vector2(width-1,Size.Y-1),new Color("ffff00"),1);
    }
}
