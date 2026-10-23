using Godot;

namespace RedAlert;

public sealed partial class ClientPreferences
{
    int resolutionWidth=1440,resolutionHeight=900,visualDetails=2,musicVolume=3,soundVolume=7,voiceVolume=7;
    public int ResolutionWidth {get=>resolutionWidth;set=>resolutionWidth=Math.Clamp(value,640,2560);}
    public int ResolutionHeight {get=>resolutionHeight;set=>resolutionHeight=Math.Clamp(value,480,1600);}
    public int VisualDetails {get=>visualDetails;set=>visualDetails=Math.Clamp(value,0,2);}
    int difficulty=1,gameSpeed=3,pointerSpeed=10;
    float pointerSensitivity=1;
    public int PointerSpeed {get=>pointerSpeed;set=>pointerSpeed=Math.Clamp(value,1,20);}
    public float PointerSensitivity {get=>pointerSensitivity;set=>pointerSensitivity=float.IsFinite(value)?Math.Clamp(value,.5f,2):1;}
    public int AIDifficulty {get=>difficulty;set=>difficulty=Math.Clamp(value,0,2);}
    public int GameSpeed {get=>gameSpeed;set=>gameSpeed=Math.Clamp(value,0,6);}
    public bool Tooltips {get;set;}=true;
    public bool TargetLines {get;set;}=true;
    public bool SeeHiddenObjects {get;set;}
    public int MusicVolume {get=>musicVolume;set=>musicVolume=Math.Clamp(value,0,10);}
    public int SoundVolume {get=>soundVolume;set=>soundVolume=Math.Clamp(value,0,10);}
    public int VoiceVolume {get=>voiceVolume;set=>voiceVolume=Math.Clamp(value,0,10);}
    public Key AttackKey {get;set;}=Key.A;
    public Key StopKey {get;set;}=Key.S;
    static void ReadShell(ConfigFile config,ClientPreferences p)
    {
        p.ResolutionWidth=ReadInt(config,"display","resolution_width",p.ResolutionWidth);p.ResolutionHeight=ReadInt(config,"display","resolution_height",p.ResolutionHeight);
        p.VisualDetails=ReadInt(config,"display","visual_details",p.VisualDetails);p.AIDifficulty=ReadInt(config,"game","difficulty",p.AIDifficulty);p.GameSpeed=ReadInt(config,"game","speed",p.GameSpeed);
        p.PointerSpeed=ReadInt(config,"input","pointer_speed",p.PointerSpeed);p.PointerSensitivity=ReadFloat(config,"input","pointer_sensitivity",p.PointerSensitivity);
        p.Tooltips=ReadBool(config,"interface","tooltips",p.Tooltips);p.TargetLines=ReadBool(config,"interface","target_lines",p.TargetLines);p.SeeHiddenObjects=ReadBool(config,"interface","see_hidden_objects",p.SeeHiddenObjects);
        p.MusicVolume=ReadInt(config,"audio","music",p.MusicVolume);p.SoundVolume=ReadInt(config,"audio","sound",p.SoundVolume);p.VoiceVolume=ReadInt(config,"audio","voice",p.VoiceVolume);
        p.AttackKey=(Key)ReadInt(config,"keyboard","attack",(int)p.AttackKey);p.StopKey=(Key)ReadInt(config,"keyboard","stop",(int)p.StopKey);
        if(!Enum.IsDefined(p.AttackKey))p.AttackKey=Key.A;if(!Enum.IsDefined(p.StopKey))p.StopKey=Key.S;
    }
    void WriteShell(ConfigFile config)
    {
        config.SetValue("display","resolution_width",ResolutionWidth);config.SetValue("display","resolution_height",ResolutionHeight);config.SetValue("display","visual_details",VisualDetails);config.SetValue("game","difficulty",AIDifficulty);config.SetValue("game","speed",GameSpeed);
        config.SetValue("input","pointer_speed",PointerSpeed);config.SetValue("input","pointer_sensitivity",PointerSensitivity);
        config.SetValue("interface","tooltips",Tooltips);config.SetValue("interface","target_lines",TargetLines);config.SetValue("interface","see_hidden_objects",SeeHiddenObjects);
        config.SetValue("audio","music",MusicVolume);config.SetValue("audio","sound",SoundVolume);config.SetValue("audio","voice",VoiceVolume);
        config.SetValue("keyboard","attack",(long)AttackKey);config.SetValue("keyboard","stop",(long)StopKey);
    }
    public void ApplyAudio()
    {
        ProjectSettings.SetSetting("gui/timers/tooltip_delay_sec",Tooltips ? 0.5 : 100000.0);
        foreach(var (name,value) in new[]{("Music",MusicVolume),("Effects",SoundVolume),("Voice",VoiceVolume)})
        {
            var bus=AudioServer.GetBusIndex(name);if(bus<0){AudioServer.AddBus();bus=AudioServer.BusCount-1;AudioServer.SetBusName(bus,name);}
            AudioServer.SetBusMute(bus,value==0);AudioServer.SetBusVolumeDb(bus,value==0?-80:20*MathF.Log10(value/10f));
        }
    }
    public void RestoreShellDefaults()
    {
        var defaults=new ClientPreferences();ResolutionWidth=defaults.ResolutionWidth;ResolutionHeight=defaults.ResolutionHeight;VisualDetails=defaults.VisualDetails;
        AIDifficulty=1;GameSpeed=3;PointerSpeed=10;PointerSensitivity=1;Tooltips=true;TargetLines=true;SeeHiddenObjects=false;MusicVolume=3;SoundVolume=7;VoiceVolume=7;AttackKey=Key.A;StopKey=Key.S;
    }
}
