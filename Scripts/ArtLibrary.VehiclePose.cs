using Godot;
using RedAlert.Simulation;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    // Original VXL/HVA exposed faces, rotated in 3D before projection. The flat
    // atlases remain the flat path; no screen-space shear touches hull height.
    const int PoseTextureLimit=192,PoseQueueLimit=256,PoseModelLimit=12;
    const long PoseTextureByteLimit=16*1024*1024,PoseModelByteLimit=24*1024*1024;
    const string PoseModelFolder="res://Assets/Generated/VehiclePoses/";
    readonly record struct PoseFace(Vector3 A,Vector3 B,Vector3 C,Vector3 D,Vector3 Normal,byte Color);
    sealed record PosePart(byte Role,PoseFace[] Faces);
    sealed class PoseModel
    {
        public required string Name;
        public required PosePart[] Parts;
        public required byte[] Palette;
        public float Scale,Elevation,Azimuth,ContactZ;
        public long Bytes;
        public LinkedListNode<string>? Node;
    }
    readonly record struct PoseKey(string Model,int Body,int Turret,int Gx,int Gy,int Rise,int Owner);
    sealed class PoseImage
    {
        public required ImageTexture Texture;
        public Vector2 Anchor,Size;
        public long Bytes;
        public LinkedListNode<PoseKey>? Node;
    }
    readonly Dictionary<string,PoseModel> poseModels=new(StringComparer.Ordinal);
    readonly LinkedList<string> poseModelLru=[];
    readonly Dictionary<PoseKey,PoseImage> poseImages=[];
    readonly LinkedList<PoseKey> poseImageLru=[];
    readonly Queue<(ImageTexture Texture,long Bytes,ulong Frame)> retiredPoseImages=[];
    readonly Queue<PoseKey> poseQueue=[];
    readonly HashSet<PoseKey> poseQueued=[];
    readonly HashSet<string> missingPoseModels=new(StringComparer.Ordinal);
    readonly Dictionary<string,string> poseModelAliases=new(StringComparer.Ordinal);
    long poseModelBytes,poseTextureBytes,poseRetiredBytes;
    ulong poseFrame=ulong.MaxValue;
    int poseFrameRenders;
    double poseFrameMilliseconds;
    public int VehiclePoseCacheCount=>poseImages.Count;
    public long VehiclePoseCacheBytes=>poseTextureBytes+poseModelBytes+poseRetiredBytes;
    public int VehiclePoseRenders {get;private set;}
    public int VehiclePoseCacheHits {get;private set;}
    public int VehiclePoseFallbacks {get;private set;}
    public int VehiclePoseMinimumOpaquePixels {get;private set;}=int.MaxValue;
    public int VehiclePoseMinimumOpaqueHeight {get;private set;}=int.MaxValue;
    public double VehiclePoseMaximumRenderMilliseconds {get;private set;}
    public int VehiclePosePendingCount=>poseQueue.Count;

    string CanonicalVehiclePoseName(string name)
    {
        if(name.Length==0)return "";
        if(poseModelAliases.TryGetValue(name,out var canonical))return canonical;
        // Expansion voxel IDs can alias retained atlases: gtnk->grizzly,
        // htnk->rhino, fv->ifv. Pose binaries use manifest.name, exactly as
        // the existing atlas loader resolves the same source manifest.
        var manifest=OriginalMapObjectVehicleManifest(name)??ExpandedVehicleManifest(name)??$"res://Assets/Generated/Vehicles/{name}.json";
        canonical=name;
        if(GFile.FileExists(manifest))
        {
            using var document=JsonDocument.Parse(GFile.GetFileAsString(manifest));
            canonical=document.RootElement.GetProperty("name").GetString()??name;
        }
        if(poseModelAliases.Count<128)poseModelAliases.Add(name,canonical);
        return canonical;
    }
    string VehiclePoseModelName(Unit unit,World world)
    {
        if(LoadExpansionMetadata() && expansionUnits.TryGetValue(unit.Definition.RulesId,out var visual))
            return visual.Kind=="voxel"?CanonicalVehiclePoseName(ExpansionVehicleVariant(visual,unit,world)):"";
        var name=unit.Type switch
        {
            UnitType.Grizzly=>"grizzly",UnitType.Rhino=>"rhino",UnitType.ChronoMiner=>"chrono-miner",
            UnitType.WarMiner=>"war-miner",UnitType.TeslaTank=>"tesla-tank",UnitType.Ifv=>"ifv",
            UnitType.FlakTrack=>"flak-track",UnitType.PrismTank=>"prism-tank",UnitType.Apocalypse=>"apocalypse",_=>""
        };
        return CanonicalVehiclePoseName(name);
    }
    public bool DrawVehicleSurfacePose(CanvasItem canvas,Unit unit,Vector2 point,float zoom,World world,Vector2 gradient,int drawOwner=-1)
    {
        if(unit.Definition.Domain!=MovementDomain.Land || !float.IsFinite(gradient.X) || !float.IsFinite(gradient.Y) || gradient.LengthSquared()<.0001f)
            return DrawVehicle(canvas,unit,point,zoom,world,drawOwner);
        var name=VehiclePoseModelName(unit,world);
        if(name.Length==0 || missingPoseModels.Contains(name))return DrawVehicle(canvas,unit,point,zoom,world,drawOwner);
        var owner=drawOwner>=0?drawOwner:unit.Owner;
        var key=new PoseKey(name,Direction(unit.BodyFacingX,unit.BodyFacingY,32),Direction(unit.FacingX,unit.FacingY,32),
            Math.Clamp((int)MathF.Round(gradient.X*8),-32,32),Math.Clamp((int)MathF.Round(gradient.Y*8),-32,32),
            world.Map.OriginalDefinition is not null?15:8,owner);
        var frame=Engine.GetProcessFrames();
        if(frame!=poseFrame)
        {
            poseFrame=frame;poseFrameRenders=0;poseFrameMilliseconds=0;
            while(retiredPoseImages.TryPeek(out var retired) && retired.Frame<=frame)
            {retiredPoseImages.Dequeue();retired.Texture.Dispose();poseRetiredBytes-=retired.Bytes;}
        }
        if(poseImages.TryGetValue(key,out var image)){VehiclePoseCacheHits++;TouchPose(key,image);}
        else
        {
            if(poseQueue.Count<PoseQueueLimit && poseQueued.Add(key))poseQueue.Enqueue(key);
            // FIFO prevents continuously moving front-of-list vehicles from
            // starving other vehicles. At most two misses / 6ms start budget.
            while(poseQueue.Count>0 && poseFrameRenders<2 && poseFrameMilliseconds<6)
            {
                var request=poseQueue.Dequeue();poseQueued.Remove(request);
                if(poseImages.ContainsKey(request) || missingPoseModels.Contains(request.Model))continue;
                var timer=Stopwatch.StartNew();
                try
                {
                    var model=LoadVehiclePoseModel(request.Model);
                    if(model is not null)
                    {
                        var rendered=RenderVehiclePose(model,request);
                        while(poseImages.Count>=PoseTextureLimit || poseTextureBytes+rendered.Bytes>PoseTextureByteLimit)EvictPoseImage();
                        rendered.Node=poseImageLru.AddLast(request);poseImages.Add(request,rendered);poseTextureBytes+=rendered.Bytes;
                        VehiclePoseRenders++;
                    }
                }
                catch(Exception error) when(error is IOException or InvalidDataException or ArgumentException or OverflowException)
                {missingPoseModels.Add(request.Model);GD.PushWarning($"Vehicle pose model {request.Model}: {error.Message}");}
                timer.Stop();poseFrameRenders++;poseFrameMilliseconds+=timer.Elapsed.TotalMilliseconds;
                VehiclePoseMaximumRenderMilliseconds=Math.Max(VehiclePoseMaximumRenderMilliseconds,timer.Elapsed.TotalMilliseconds);
            }
            poseImages.TryGetValue(key,out image);
            if(image is null)
            {
                // Keep a nearby already rendered 3D support pose while its new
                // quantized support plane is queued; only the initial miss uses
                // the original upright atlas, never a singular affine image.
                var nearest=int.MaxValue;
                foreach(var entry in poseImages)
                {
                    var k=entry.Key;if(k.Model!=key.Model || k.Body!=key.Body || k.Turret!=key.Turret || k.Owner!=key.Owner || k.Rise!=key.Rise)continue;
                    var distance=Math.Abs(k.Gx-key.Gx)+Math.Abs(k.Gy-key.Gy);
                    if(distance<nearest){nearest=distance;image=entry.Value;}
                }
            }
        }
        if(image is null){VehiclePoseFallbacks++;return DrawVehicle(canvas,unit,point,zoom,world,drawOwner);}
        canvas.DrawTextureRect(image.Texture,new Rect2(point-image.Anchor*zoom,image.Size*zoom),false);return true;
    }
    void TouchPose(PoseKey key,PoseImage image)
    {if(image.Node is {} node){poseImageLru.Remove(node);poseImageLru.AddLast(node);}}
    void EvictPoseImage()
    {
        var first=poseImageLru.First??throw new InvalidDataException("Vehicle pose exceeds texture budget");
        var image=poseImages[first.Value];poseTextureBytes-=image.Bytes;
        // Canvas draw commands retain texture RIDs until the render frame is
        // consumed. Retire two frames later, bounded by two new poses/frame.
        retiredPoseImages.Enqueue((image.Texture,image.Bytes,poseFrame+2));poseRetiredBytes+=image.Bytes;
        poseImages.Remove(first.Value);poseImageLru.RemoveFirst();
    }
    PoseModel? LoadVehiclePoseModel(string name)
    {
        if(poseModels.TryGetValue(name,out var existing))
        {if(existing.Node is {} node){poseModelLru.Remove(node);poseModelLru.AddLast(node);}return existing;}
        var path=PoseModelFolder+name+".vpm.gz";
        if(!GFile.FileExists(path)){missingPoseModels.Add(name);return null;}
        var compressed=GFile.GetFileAsBytes(path);
        if(compressed.Length>8*1024*1024)throw new InvalidDataException("Compressed vehicle pose model exceeds 8MiB");
        using var input=new MemoryStream(compressed,false);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var reader=new BinaryReader(gzip);
        if(!reader.ReadBytes(8).SequenceEqual(new byte[]{82,65,86,80,49,0,0,0}))throw new InvalidDataException("Invalid vehicle pose model signature");
        var count=reader.ReadInt32();if(count<1 || count>8)throw new InvalidDataException("Invalid vehicle pose parts");
        float F(){var f=reader.ReadSingle();if(!float.IsFinite(f))throw new InvalidDataException("Nonfinite vehicle model");return f;}
        Vector3 V()=>new(F(),F(),F());
        var scale=F();var elevation=F();var azimuth=F();
        if(scale<=0 || scale>64 || elevation<10 || elevation>80)throw new InvalidDataException("Invalid vehicle projection");
        var palette=reader.ReadBytes(768);if(palette.Length!=768)throw new InvalidDataException("Truncated vehicle palette");
        var parts=new PosePart[count];long bytes=768;var minimumZ=float.PositiveInfinity;
        for(var p=0;p<count;p++)
        {
            var role=reader.ReadByte();var faces=reader.ReadInt32();
            if(role>2 || faces<0 || faces>100_000 || bytes+faces*64L>PoseModelByteLimit)throw new InvalidDataException("Vehicle face budget exceeded");
            var data=new PoseFace[faces];bytes+=faces*64L;
            for(var i=0;i<faces;i++)
            {
                var a=V();var b=V();var c=V();var d=V();var normal=V();var index=reader.ReadByte();
                data[i]=new(a,b,c,d,normal,index);
                if(role==0)minimumZ=MathF.Min(minimumZ,MathF.Min(MathF.Min(a.Z,b.Z),MathF.Min(c.Z,d.Z)));
            }
            parts[p]=new(role,data);
        }
        if(!float.IsFinite(minimumZ))throw new InvalidDataException("Vehicle model has no body");
        while(poseModels.Count>=PoseModelLimit || poseModelBytes+bytes>PoseModelByteLimit)
        {
            var first=poseModelLru.First??throw new InvalidDataException("Vehicle model exceeds cache budget");
            poseModelBytes-=poseModels[first.Value].Bytes;poseModels.Remove(first.Value);poseModelLru.RemoveFirst();
        }
        var model=new PoseModel{Name=name,Parts=parts,Palette=palette,Scale=scale,Elevation=elevation,Azimuth=azimuth,ContactZ=minimumZ,Bytes=bytes};
        model.Node=poseModelLru.AddLast(name);poseModels.Add(name,model);poseModelBytes+=bytes;return model;
    }
    readonly record struct ProjectedFace(Vector3 A,Vector3 B,Vector3 C,Vector3 D,byte R,byte G,byte Bc);
    PoseImage RenderVehiclePose(PoseModel model,PoseKey key)
    {
        var elevation=model.Elevation*MathF.PI/180;var sinE=MathF.Sin(elevation);var cosE=MathF.Cos(elevation);
        // Map ground vectors project as (30,15),(-30,15). Match this
        // projection exactly, including the VXL renderer's reflected Y and
        // elevation; a height unit is Rise screen pixels, not a voxel unit.
        var gx=key.Gx/8f;var gy=key.Gy/8f;
        var dzdx=(gx-gy)*key.Rise/(60*cosE);var dzdy=-(gx+gy)*key.Rise/(60*cosE);
        var normal=new Vector3(-dzdx,-dzdy,1).Normalized();
        var axis=new Vector3(-normal.Y,normal.X,0);var sine=axis.Length();if(sine>.000001f)axis/=sine;
        Vector3 Tilt(Vector3 v)=>sine<.000001f?v:v*normal.Z+axis.Cross(v)*sine+axis*axis.Dot(v)*(1-normal.Z);
        var pivot=new Vector3(0,0,model.ContactZ);var camera=new Vector3(0,-cosE,sinE);var light=new Vector3(-.6f,-.8f,1.2f).Normalized();
        var projected=new List<ProjectedFace>();var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
        foreach(var part in model.Parts)
        {
            var angle=(model.Azimuth+(part.Role==0?key.Body:key.Turret)*360f/32)*MathF.PI/180;var co=MathF.Cos(angle);var si=MathF.Sin(angle);
            Vector3 Yaw(Vector3 v)=>new(co*v.X-si*v.Y,si*v.X+co*v.Y,v.Z);
            Vector3 Project(Vector3 v)
            {
                var p=Tilt(Yaw(v)-pivot)+pivot;
                var result=new Vector3(p.X*model.Scale,(-sinE*p.Y-cosE*p.Z)*model.Scale,-cosE*p.Y+sinE*p.Z);
                min=new Vector2(MathF.Min(min.X,result.X),MathF.Min(min.Y,result.Y));max=new Vector2(MathF.Max(max.X,result.X),MathF.Max(max.Y,result.Y));return result;
            }
            foreach(var f in part.Faces)
            {
                var n=Tilt(Yaw(f.Normal));if(n.Dot(camera)<=0)continue;
                var shade=.38f+.62f*MathF.Max(0,n.Dot(light));var offset=f.Color*3;
                var r=(byte)Math.Clamp((int)(model.Palette[offset]*shade+.5f),0,255);
                var g=(byte)Math.Clamp((int)(model.Palette[offset+1]*shade+.5f),0,255);
                var b=(byte)Math.Clamp((int)(model.Palette[offset+2]*shade+.5f),0,255);
                if(key.Owner>=0 && f.Color>=16 && f.Color<32)
                {
                    var tint=Battlefield.PlayerColors[key.Owner%8];var brightness=Math.Max(r,Math.Max(g,b))/252f;
                    r=(byte)Math.Clamp((int)(tint.R*255*brightness),0,255);g=(byte)Math.Clamp((int)(tint.G*255*brightness),0,255);b=(byte)Math.Clamp((int)(tint.B*255*brightness),0,255);
                }
                projected.Add(new(Project(f.A),Project(f.B),Project(f.C),Project(f.D),r,g,b));
            }
        }
        if(projected.Count==0)throw new InvalidDataException("Vehicle support pose has no visible faces");
        var origin=new Vector2(MathF.Floor(min.X)-2,MathF.Floor(min.Y)-2);
        var width=(int)MathF.Ceiling(max.X-origin.X)+3;var height=(int)MathF.Ceiling(max.Y-origin.Y)+3;
        if(width<2 || height<2 || width>192 || height>192)throw new InvalidDataException($"Vehicle support canvas {width}x{height} exceeds bounds");
        var pixels=new byte[width*height*4];var depth=new float[width*height];Array.Fill(depth,float.NegativeInfinity);
        foreach(var face in projected)
        {
            var a=face.A-new Vector3(origin.X,origin.Y,0);var b=face.B-new Vector3(origin.X,origin.Y,0);var c=face.C-new Vector3(origin.X,origin.Y,0);var d=face.D-new Vector3(origin.X,origin.Y,0);
            Triangle(a,b,c,face);Triangle(a,c,d,face);
        }
        var covered=0;var firstRow=height;var lastRow=-1;
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)if(pixels[(y*width+x)*4+3]!=0){covered++;firstRow=Math.Min(firstRow,y);lastRow=Math.Max(lastRow,y);}
        if(covered<8 || lastRow-firstRow+1<2)throw new InvalidDataException("Vehicle support pose collapsed or produced empty coverage");
        VehiclePoseMinimumOpaquePixels=Math.Min(VehiclePoseMinimumOpaquePixels,covered);VehiclePoseMinimumOpaqueHeight=Math.Min(VehiclePoseMinimumOpaqueHeight,lastRow-firstRow+1);
        using var image=Image.CreateFromData(width,height,false,Image.Format.Rgba8,pixels);var texture=ImageTexture.CreateFromImage(image);TexturesCreated++;
        return new PoseImage{Texture=texture,Anchor=-origin,Size=new Vector2(width,height),Bytes=width*height*8L};
        void Triangle(Vector3 a,Vector3 b,Vector3 c,ProjectedFace face)
        {
            static float Edge(Vector3 p,Vector3 q,float x,float y)=>(x-p.X)*(q.Y-p.Y)-(y-p.Y)*(q.X-p.X);
            var area=Edge(a,b,c.X,c.Y);if(MathF.Abs(area)<.000001f)return;
            var x0=Math.Clamp((int)MathF.Floor(MathF.Min(a.X,MathF.Min(b.X,c.X))),0,width-1);var x1=Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X,MathF.Max(b.X,c.X))),0,width-1);
            var y0=Math.Clamp((int)MathF.Floor(MathF.Min(a.Y,MathF.Min(b.Y,c.Y))),0,height-1);var y1=Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y,MathF.Max(b.Y,c.Y))),0,height-1);
            for(var y=y0;y<=y1;y++)for(var x=x0;x<=x1;x++)
            {
                var u=Edge(b,c,x+.5f,y+.5f)/area;var v=Edge(c,a,x+.5f,y+.5f)/area;var w=1-u-v;if(u<-.00001f || v<-.00001f || w<-.00001f)continue;
                var z=u*a.Z+v*b.Z+w*c.Z;var i=y*width+x;if(z<=depth[i])continue;depth[i]=z;
                var p=i*4;pixels[p]=face.R;pixels[p+1]=face.G;pixels[p+2]=face.Bc;pixels[p+3]=255;
            }
        }
    }
    void DisposeVehiclePoses()
    {
        foreach(var image in poseImages.Values)image.Texture.Dispose();
        foreach(var retired in retiredPoseImages)retired.Texture.Dispose();retiredPoseImages.Clear();
        poseImages.Clear();poseImageLru.Clear();poseModels.Clear();poseModelLru.Clear();poseQueue.Clear();poseQueued.Clear();missingPoseModels.Clear();poseModelAliases.Clear();poseTextureBytes=0;poseModelBytes=0;poseRetiredBytes=0;
    }
}
