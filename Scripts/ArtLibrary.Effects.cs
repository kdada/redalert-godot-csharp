using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    public enum CombatSprite { Muzzle, RifleImpact, Explosion, Smoke, Dust, ElectricImpact, Bite, SniperFlash }
    static readonly int[] CombatFrameCounts=[6,8,12,12,8,8,6,6];
    const int CombatCell=64,CombatColumns=12;
    const string CombatAtlasKey="$independent-combat-effects";

    // One texture for every independent effect. Register it in the existing sprites
    // cache so ArtLibrary.Dispose() releases it without requiring a new lifetime hook.
    SpriteAtlas? CombatAtlas()
    {
        if(sprites.TryGetValue(CombatAtlasKey,out var atlas))return atlas;
        if(unavailable.Contains(CombatAtlasKey))return null;
        const string path="res://Assets/Generated/Effects/atlas.png";
        if(!Godot.FileAccess.FileExists(path)){unavailable.Add(CombatAtlasKey);return null;}
        var pixels=ReadPixels(path);
        if(pixels.Width!=CombatCell*CombatColumns || pixels.Height!=CombatCell*CombatFrameCounts.Length)
        {unavailable.Add(CombatAtlasKey);return null;}
        var frames=new Frame[CombatColumns*CombatFrameCounts.Length];
        for(var row=0;row<CombatFrameCounts.Length;row++)
        for(var column=0;column<CombatColumns;column++)
        {
            var region=new Rect2(column*CombatCell,row*CombatCell,CombatCell,CombatCell);
            frames[row*CombatColumns+column]=new Frame(new Rect2(0,0,CombatCell,CombatCell),region,column<CombatFrameCounts[row],null);
        }
        atlas=new SpriteAtlas{Width=CombatCell,Height=CombatCell,Frames=frames,Pixels=pixels};
        sprites.Add(CombatAtlasKey,atlas);return atlas;
    }

    public bool DrawCombatSprite(CanvasItem canvas,CombatSprite kind,Vector2 center,float scale,float phase,float opacity=1)
    {
        if(scale<=0 || opacity<=0)return false;
        var atlas=CombatAtlas();if(atlas is null)return false;
        var row=(int)kind;if((uint)row>=(uint)CombatFrameCounts.Length)return false;
        var frame=Math.Clamp((int)(Math.Clamp(phase,0,.9999f)*CombatFrameCounts[row]),0,CombatFrameCounts[row]-1);
        var source=atlas.Frames[row*CombatColumns+frame].Image;
        canvas.DrawTextureRectRegion(Texture(atlas.Pixels,-1),new Rect2(center-new Vector2(32,32)*scale,new Vector2(64,64)*scale),source,new Color(1,1,1,Math.Clamp(opacity,0,1)));
        return true;
    }

    // Original art.ini [DogSequence]: FireUp=116,6,6. Reuses the retained
    // DOG/ADOG SHP pixels, original per-frame crops, shadows and team remapping.
    // A short cosmetic attack pose is overlaid; the simulation dog never moves here.
    public bool DrawCombatDog(CanvasItem canvas,Country country,int owner,Vector2 direction,Vector2 feet,float zoom,float phase)
    {
        var sprite=Sprite(Rules.Side(country)==Faction.Allied?"adog":"dog");if(sprite is null)return false;
        var sx=direction.X-direction.Y;var sy=(direction.X+direction.Y)*.5f;
        var facing=((int)MathF.Round(-(MathF.Atan2(sy,sx)+MathF.PI/2)*4/MathF.PI)+8)%8;
        var frame=116+facing*6+Math.Clamp((int)(phase*6),0,5);
        if(frame>=sprite.Frames.Length || !sprite.Frames[frame].Data)return false;
        var anchor=new Vector2(sprite.Width/2f,sprite.Height/2f+3);
        if(sprite.Frames[frame].Shadow is int shadow)DrawFrame(canvas,sprite,shadow,-1,feet,anchor,zoom);
        DrawFrame(canvas,sprite,frame,owner,feet,anchor,zoom);return true;
    }
}
