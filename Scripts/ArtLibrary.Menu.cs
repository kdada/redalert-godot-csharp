using Godot;
using GFile=Godot.FileAccess;

namespace RedAlert;

public sealed partial class ArtLibrary
{
    readonly Dictionary<string,ImageTexture> menuImages=[];
    readonly Dictionary<int,AtlasTexture> menuButtonFrames=[];
    readonly Dictionary<int,AtlasTexture> menuRailButtonFrames=[];
    public Texture2D? MenuImage(string name)
    {
        if(menuImages.TryGetValue(name,out var cached))return cached;
        if(name=="main-header")
        {
            // Recover the unaltered native console bezel. The old generated header had
            // stretched it to 246px and painted black beyond the recessed title well.
            const string reference="res://Assets/Generated/ShellChrome/main-reference-surface.png";
            if(!GFile.FileExists(reference))return null;
            using var original=LoadImage(reference);
            using var header=original.GetRegion(new Rect2I(472,0,168,195));
            header.FillRect(new Rect2I(26,3,116,29),new Color("030203"));
            var repaired=ImageTexture.CreateFromImage(header);menuImages.Add(name,repaired);TexturesCreated++;return repaired;
        }
        var path=name switch
        {
            "background"=>"res://Assets/Generated/ShellChrome/main-surface.png",
            "dialog-background"=>"res://Assets/Generated/ShellChrome/dialog-surface.png",
            "housing"=>"res://Assets/Generated/ShellChrome/button-housing.png",
            "button-atlas"=>"res://Assets/Generated/Menu/Shp/mnbttn/atlas.png",
            "poster"=>"res://Assets/Generated/AuthenticShell/ra2ts_l/firstframe.png",
            "setup-header"=>"res://Assets/Generated/ShellChrome/setup-header.png",
            _=>"res://Assets/Generated/Menu/"+name+".png"
        };
        if(!GFile.FileExists(path))return null;
        using var image=LoadImage(path);var texture=ImageTexture.CreateFromImage(image);menuImages.Add(name,texture);TexturesCreated++;return texture;
    }
    // Original mnbttn.shp supplies three 126x25 frames. Input-state assignment is
    // the existing UI convention; retained facts do not establish animation timing.
    public Texture2D? MenuButtonFrame(int state)
    {
        if(menuButtonFrames.TryGetValue(state,out var cached))return cached;
        var atlas=MenuImage("button-atlas");if(atlas is null)return null;
        var region=state switch{1=>new Rect2(126,0,126,25),2=>new Rect2(0,25,126,25),_=>new Rect2(0,0,126,25)};
        var texture=new AtlasTexture{Atlas=atlas,Region=region};menuButtonFrames[state]=texture;return texture;
    }
    public Texture2D? MenuRailButtonFrame(int state)
    {
        if(menuRailButtonFrames.TryGetValue(state,out var cached))return cached;
        var atlas=MenuImage("button-atlas");if(atlas is null)return null;
        var origin=state switch{1=>new Vector2(126,0),2=>new Vector2(0,25),_=>Vector2.Zero};
        // The screenshot-derived housing already has the original recessed black rim.
        // Use only the SHP's red face, avoiding a second bright red rectangular border.
        var texture=new AtlasTexture{Atlas=atlas,Region=new Rect2(origin+new Vector2(3,3),new Vector2(120,19))};
        menuRailButtonFrames[state]=texture;return texture;
    }
    void DisposeMenuImages()
    {
        foreach(var frame in menuRailButtonFrames.Values)frame.Dispose();menuRailButtonFrames.Clear();
        foreach(var frame in menuButtonFrames.Values)frame.Dispose();menuButtonFrames.Clear();
        foreach(var image in menuImages.Values)image.Dispose();menuImages.Clear();
    }
}
