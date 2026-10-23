using Godot;

namespace RedAlert;

// Canvas chrome owns no resources: all interface textures remain cached by ArtLibrary.
public partial class ClassicChrome : Control
{
    public enum SurfaceKind { Sidebar, Radar, Recess, Deployment }
    public SurfaceKind Kind { get; set; }
    public bool Soviet { get; set; }
    public Texture2D? RadarTexture { get; set; }
    public Texture2D? TopTexture { get; set; }
    public Texture2D? MiddleTexture { get; set; }
    public Texture2D? BottomTexture { get; set; }
    public Texture2D? SurfaceTexture { get; set; }

    public override void _Ready()
    {
        MouseFilter=MouseFilterEnum.Ignore;
        TextureFilter=TextureFilterEnum.Nearest;
        Resized+=QueueRedraw;
    }

    public override void _Draw()
    {
        if(Size.X<2 || Size.Y<2)return;
        var rect=new Rect2(Vector2.Zero,Size);
        var steel=new Color(Soviet?"302a2b":"2e3741");
        var light=new Color(Soviet?"8c7774":"8596a3");
        var accent=new Color(Soviet?"822c2e":"364f69");
        DrawRect(rect,new Color("101216"));
        if(Kind==SurfaceKind.Deployment)
        {
            DrawRect(rect,new Color("14181f"));
            for(var y=0;y<Size.Y;y+=4)DrawLine(new(0,y),new(Size.X,y),new Color(1,1,1,.015f));
            DrawRect(new Rect2(0,0,Size.X,9),new Color("591e24"));
            DrawRect(new Rect2(0,Size.Y-9,Size.X,9),new Color("080a0d"));
            for(var x=24;x<Size.X;x+=48)
            {
                DrawLine(new(x,10),new(x+18,10),new Color("a34542"));
                DrawLine(new(x,Size.Y-11),new(x+18,Size.Y-11),new Color("39434d"));
            }
            return;
        }
        if(Kind==SurfaceKind.Recess)
        {
            DrawRect(rect,new Color("101216"));
            Bevel(rect,new Color("070809"),light.Darkened(.45f));
            return;
        }
        DrawRect(rect,steel);
        if(Kind==SurfaceKind.Sidebar)
        {
            for(var y=0;y<Size.Y;y+=3)DrawLine(new(5,y),new(Size.X-5,y),steel.Lightened(y%2==0?.035f:.015f));
            DrawRect(new Rect2(0,0,8,Size.Y),new Color("17191c"));
            DrawRect(new Rect2(8,0,5,Size.Y),accent);
            DrawRect(new Rect2(Size.X-8,0,8,Size.Y),new Color("15171a"));
            DrawLine(new(1,0),new(1,Size.Y),light);
            DrawLine(new(13,0),new(13,Size.Y),light.Darkened(.15f));
            DrawLine(new(Size.X-9,0),new(Size.X-9,Size.Y),light.Darkened(.4f));
            for(var y=19;y<Size.Y;y+=82)
            {
                Rivet(new(5,y));Rivet(new(Size.X-4,y));
            }
            var radarHeight=RadarTexture is null?0:RadarTexture.GetHeight()*Size.X/RadarTexture.GetWidth();
            var topHeight=TopTexture is null?0:TopTexture.GetHeight()*Size.X/TopTexture.GetWidth();
            var bottomHeight=BottomTexture is null?0:BottomTexture.GetHeight()*Size.X/BottomTexture.GetWidth();
            if(MiddleTexture is not null)
            {
                var tileHeight=MiddleTexture.GetHeight()*Size.X/MiddleTexture.GetWidth();
                // Bounded by viewport height; crop the last tile instead of distorting pixels.
                for(var y=radarHeight+topHeight;tileHeight>0 && y<Size.Y-bottomHeight;y+=tileHeight)
                {
                    var h=Math.Min(tileHeight,Size.Y-bottomHeight-y);
                    DrawTextureRectRegion(MiddleTexture,new Rect2(0,y,Size.X,h),new Rect2(0,0,MiddleTexture.GetWidth(),h*MiddleTexture.GetWidth()/Size.X));
                }
            }
            if(RadarTexture is not null)DrawTextureRect(RadarTexture,new Rect2(0,0,Size.X,radarHeight),false);
            if(TopTexture is not null)DrawTextureRect(TopTexture,new Rect2(0,radarHeight,Size.X,topHeight),false);
            if(BottomTexture is not null)DrawTextureRect(BottomTexture,new Rect2(0,Size.Y-bottomHeight,Size.X,bottomHeight),false);
        }
        else
        {
            DrawRect(new Rect2(4,4,Size.X-8,Size.Y-8),new Color("090b0d"));
            Bevel(rect,light,new Color("08090b"));
            Bevel(new Rect2(3,3,Size.X-6,Size.Y-6),new Color("07090a"),light.Darkened(.45f));
            Rivet(new(7,7));Rivet(new(Size.X-7,7));
            Rivet(new(7,Size.Y-7));Rivet(new(Size.X-7,Size.Y-7));
        }
        if(SurfaceTexture is not null)DrawTextureRect(SurfaceTexture,rect,false);
    }

    void Bevel(Rect2 r,Color top,Color bottom)
    {
        DrawLine(r.Position,new(r.End.X-1,r.Position.Y),top);
        DrawLine(r.Position,new(r.Position.X,r.End.Y-1),top);
        DrawLine(new(r.Position.X,r.End.Y-1),r.End-Vector2.One,bottom);
        DrawLine(new(r.End.X-1,r.Position.Y),r.End-Vector2.One,bottom);
    }
    void Rivet(Vector2 p)
    {
        DrawRect(new Rect2(p-new Vector2(2,2),new Vector2(4,4)),new Color("111316"));
        DrawLine(p-new Vector2(1,1),p+new Vector2(1,-1),new Color("9c9c98"));
        DrawLine(p-new Vector2(1,0),p+new Vector2(1,0),new Color("454748"));
    }
}
