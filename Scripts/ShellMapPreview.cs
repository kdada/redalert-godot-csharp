using Godot;

namespace RedAlert;

public partial class ShellMapPreview : Control
{
    uint seed=20261018,requestedSeed;
    int players=4;
    Task<MenuMapPreviewData>? pending;
    MenuMapPreviewData? preview;
    ImageTexture? terrainTexture;
    bool failed;

    public uint Seed
    {
        get=>seed;
        set
        {
            if(seed==value)return;
            seed=value;
            if(IsInsideTree())RequestPreview();
        }
    }
    public int Players
    {
        get=>players;
        set{if(players==value)return;players=value;if(IsInsideTree())QueueRedraw();}
    }
    public bool PreviewReady=>terrainTexture is not null && preview?.Seed==Seed;

    public override void _EnterTree()=>RequestPreview();
    public override void _Ready(){TextureFilter=TextureFilterEnum.Nearest;Resized+=QueueRedraw;QueueRedraw();}
    public override void _ExitTree()
    {
        // No worker holds this node or posts deferred callbacks. Leaving the tree drops the
        // only subscription; the shared pure result can finish safely for the next page.
        SetProcess(false);pending=null;preview=null;ReleaseTexture();
    }
    void RequestPreview()
    {
        requestedSeed=Seed;pending=MenuMapPreviewCache.GetAsync(requestedSeed);
        failed=false;preview=null;ReleaseTexture();SetProcess(true);QueueRedraw();
    }
    void ReleaseTexture(){terrainTexture?.Dispose();terrainTexture=null;}
    public override void _Process(double delta)
    {
        // Poll only while a request is outstanding. All Image/GPU and Control access stays
        // on the main thread, including an already-completed cache hit.
        var work=pending;
        if(work is null || !work.IsCompleted)return;
        if(!IsInsideTree() || IsQueuedForDeletion() || requestedSeed!=Seed)return;
        pending=null;SetProcess(false);
        if(work.IsFaulted || work.IsCanceled)
        {
            _=work.Exception;failed=true;QueueRedraw();return;
        }
        var data=work.GetAwaiter().GetResult();
        if(data.Seed!=Seed)return;
        try
        {
            using var image=Image.CreateFromData(data.Width,data.Height,false,Image.Format.Rgba8,data.CopyRgbaPixels());
            terrainTexture=ImageTexture.CreateFromImage(image);preview=data;
        }
        catch(Exception exception)
        {
            ReleaseTexture();failed=true;GD.PushWarning("Map preview texture could not be created: "+exception.Message);
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        var rect=new Rect2(Vector2.Zero,Size);DrawRect(rect,new Color("090d08"));
        if(terrainTexture is not null && preview is {} data && data.Seed==Seed)
        {
            DrawTextureRect(terrainTexture,rect,false);
            var count=Math.Min(data.SpawnMarkers.Count,Math.Clamp(Players,2,8));
            for(var i=0;i<count;i++)
            {
                var marker=data.SpawnMarkers[i];var point=new Vector2(marker.X,marker.Y)*Size;
                DrawCircle(point,2.5f,new Color("ffff00"));
                DrawString(ThemeDB.FallbackFont,point+new Vector2(3,1),(i+1).ToString(),fontSize:10,modulate:Colors.White);
            }
        }
        else
        {
            // Immediate bounded-cost placeholder; no map construction or per-cell drawing.
            var color=new Color("3c3d15");var center=Size/2;
            DrawLine(new Vector2(7,center.Y),new Vector2(Math.Max(7,Size.X-7),center.Y),color);
            DrawLine(new Vector2(center.X,7),new Vector2(center.X,Math.Max(7,Size.Y-7)),color);
            DrawArc(center,Math.Max(1,Math.Min(Size.X,Size.Y)*.28f),0,Mathf.Tau,24,color);
            var label=failed?"预览不可用":"地图准备中";
            var font=ThemeDB.FallbackFont;var width=font.GetStringSize(label,fontSize:11).X;
            DrawString(font,new Vector2(Math.Max(2,(Size.X-width)/2),Size.Y-9),label,fontSize:11,modulate:new Color("b9b370"));
        }
        DrawRect(rect,new Color("8c8c8c"),false,1);
    }
}
