using Godot;

namespace RedAlert;

// Native 60x48 cameo at 1.5 scale; the slot bevel belongs to side2.
// Its Texture is borrowed from ArtLibrary, never disposed here.
public partial class CameoButton : Button
{
    public Texture2D? Picture { get; set; }
    public string Title { get; set; }="";
    public int Price { get; set; }
    public bool Soviet { get; set; }
    float production=-1;
    int queued;
    bool active;

    public override void _Ready()
    {
        CustomMinimumSize=new Vector2(90,72);
        using var empty=new StyleBoxEmpty();
        foreach(var state in new[]{"normal","hover","pressed","hover_pressed","disabled","focus"})AddThemeStyleboxOverride(state,empty);
        MouseDefaultCursorShape=CursorShape.PointingHand;
        TextureFilter=TextureFilterEnum.Nearest;
        MouseEntered+=QueueRedraw;MouseExited+=QueueRedraw;ButtonDown+=QueueRedraw;ButtonUp+=QueueRedraw;
        Resized+=QueueRedraw;
    }
    public void SetProduction(float progress,int count,bool selected)
    {
        if(Math.Abs(production-progress)<.001f && queued==count && active==selected)return;
        production=progress;queued=count;active=selected;QueueRedraw();
    }
    public override void _Draw()
    {
        var image=new Rect2((Size.X-90)/2,(Size.Y-72)/2,90,72);
        if(Picture is not null)DrawTextureRect(Picture,image,false);
        else
        {
            DrawRect(image,new Color(Soviet?"493132":"344451"));
            DrawLine(image.Position+new Vector2(5,5),image.End-new Vector2(5,5),new Color(1,1,1,.08f));
            DrawLine(new(image.End.X-5,image.Position.Y+5),new(image.Position.X+5,image.End.Y-5),new Color(1,1,1,.08f));
        }
        if(production>=0)
        {
            var center=image.GetCenter();var radius=60f;
            var points=new List<Vector2>{center};
            var remaining=1-Math.Clamp(production,0,1);
            for(var i=0;i<=32;i++)
            {
                var angle=-MathF.PI/2+remaining*MathF.Tau*i/32;
                var point=center+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*radius;
                point=new Vector2(Math.Clamp(point.X,image.Position.X,image.End.X),Math.Clamp(point.Y,image.Position.Y,image.End.Y));
                if(points[^1].DistanceSquaredTo(point)>.01f)points.Add(point);
            }
            if(remaining>=.9999f)DrawRect(image,new Color(0,0,0,.55f));
            else if(remaining>.0001f && points.Count>2)DrawColoredPolygon(points.ToArray(),new Color(0,0,0,.55f));
        }
        var font=GetThemeFont("font");
        // Retained cameos already include their original caption band. Chinese
        // names and prices remain in tooltips; only missing-art fallbacks need text.
        if(Picture is null)DrawString(font,new(image.Position.X,image.End.Y-4),Title,HorizontalAlignment.Center,90,12,new Color("e5e0cb"));
        if(!Disabled && (IsHovered() || active))
        {
            DrawStringOutline(font,new(image.Position.X+3,image.Position.Y+14),$"${Price}",HorizontalAlignment.Left,84,11,2,new Color("090a0b"));
            DrawString(font,new(image.Position.X+3,image.Position.Y+14),$"${Price}",HorizontalAlignment.Left,84,11,new Color("eedaa0"));
        }
        if(queued>0)
        {
            DrawRect(new Rect2(image.End.X-23,image.Position.Y,23,19),new Color("131817"));
            DrawString(font,new(image.End.X-23,image.Position.Y+15),queued.ToString(),HorizontalAlignment.Center,23,14,new Color("f5e3a0"));
        }
        if(Disabled)DrawRect(image,new Color(0,0,0,.56f));
        if(!Disabled && IsPressed())DrawRect(image,new Color(0,0,0,.15f));
        if(active || !Disabled && IsHovered())DrawRect(image.Grow(-.75f),new Color("d4c796"),false,1.5f);
    }
}
