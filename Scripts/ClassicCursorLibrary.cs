using Godot;
using System.Text.Json;

namespace RedAlert;

public sealed class ClassicCursorLibrary : IDisposable
{
    readonly Texture2D atlas;
    readonly Rect2[] frames;
    readonly Dictionary<string,Sequence> sequences=[];
    sealed record Sequence(string NativeName,int[] Frames,Vector2 Hotspot,double Duration);
    string current="";
    ulong started;
    public string Key {get;private set;}="arrow";
    public int Frame {get;private set;}
    public Vector2 Hotspot {get;private set;}
    public ClassicCursorLibrary()
    {
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Generated/Cursors/catalog.json"));
        var root=document.RootElement;frames=new Rect2[root.GetProperty("sourceFrameCount").GetInt32()];
        foreach(var frame in root.GetProperty("frames").EnumerateArray())
        {
            var r=frame.GetProperty("region");frames[frame.GetProperty("index").GetInt32()]=new Rect2(r.GetProperty("x").GetSingle(),r.GetProperty("y").GetSingle(),r.GetProperty("width").GetSingle(),r.GetProperty("height").GetSingle());
        }
        foreach(var entry in root.GetProperty("cursors").EnumerateObject())
        {
            var item=entry.Value;var h=item.GetProperty("hotspot");
            var indices=item.GetProperty("frames").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            if(indices.Length==0 || indices.Any(index=>index<0 || index>=frames.Length))throw new InvalidDataException("原版光标帧范围无效："+entry.Name);
            sequences[entry.Name]=new Sequence(item.GetProperty("nativeName").GetString()??entry.Name,indices,new Vector2(h.GetProperty("x").GetSingle(),h.GetProperty("y").GetSingle()),item.GetProperty("frameDurationSeconds").GetDouble());
        }
        using var image=Image.LoadFromFile("res://Assets/Generated/Cursors/atlas.png");atlas=ImageTexture.CreateFromImage(image);
    }
    public void Draw(CanvasItem canvas,Vector2 point,string key,float scale=1)
    {
        if(!sequences.TryGetValue(key,out var sequence)){key="arrow";sequence=sequences[key];}
        if(current!=key){current=key;started=Time.GetTicksMsec();}
        var elapsed=(Time.GetTicksMsec()-started)/1000.0;
        var index=sequence.Frames.Length==1 || sequence.Duration<=0?0:(int)(elapsed/sequence.Duration)%sequence.Frames.Length;
        Frame=sequence.Frames[index];Key=key;Hotspot=sequence.Hotspot;
        var source=frames[Frame];canvas.DrawTextureRectRegion(atlas,new Rect2(point-Hotspot*scale,source.Size*scale),source,Colors.White);
    }
    public bool HasSequence(string key)=>sequences.ContainsKey(key);
    public Godot.Collections.Dictionary Audit()=>new(){["key"]=Key,["frame"]=Frame,["hotspot"]=Hotspot,["sourceFrames"]=frames.Length,
        ["sequenceCount"]=sequences.Count,["nativeName"]=sequences.TryGetValue(Key,out var sequence)?sequence.NativeName:"",
        ["sourceGame"]="Red Alert 2 vanilla"};
    public void Dispose()=>atlas.Dispose();
}
