using Godot;
using RedAlert.Simulation;
using System.Diagnostics;
using System.Text.Json;

namespace RedAlert;

public partial class Main
{
    // Only the newly changed presentation paths; no simulation/network regression suite.
    async void RunPresentationChecks()
    {
        var checks=new List<string>();var decoded=new List<object>();var error="";
        float fixedZoom=0,visibleArtHeight=0;Vector2 dragBefore=default,dragAfter=default;
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Wait(Func<bool> condition)
        {
            var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>6)throw new TimeoutException("presentation check timed out");await Frame();}
        }
        async Task KeyPress(Key code,bool shift=false)
        {
            using var down=new InputEventKey{Keycode=code,PhysicalKeycode=code,Pressed=true,ShiftPressed=shift};
            using var up=new InputEventKey{Keycode=code,PhysicalKeycode=code,Pressed=false,ShiftPressed=shift};
            GetViewport().PushInput(down,true);GetViewport().PushInput(up,true);await Frame();await Frame();
        }
        void PushMouseButton(Vector2 position,MouseButton button,bool pressed)
        {
            using var input=new InputEventMouseButton{Position=position,GlobalPosition=position,ButtonIndex=button,Pressed=pressed};GetViewport().PushInput(input,true);
        }
        void MouseMove(Vector2 position,Vector2 relative,MouseButtonMask mask=0)
        {
            using var input=new InputEventMouseMotion{Position=position,GlobalPosition=position,Relative=relative,ButtonMask=mask};GetViewport().PushInput(input,true);
        }
        try
        {
            Require(GameAudio.ActiveFiles.Count>=20,"original audio mapping missing");
            foreach(var path in GameAudio.ActiveFiles.Values.Distinct())
            {
                Require(path.Contains("/Original/",StringComparison.Ordinal),"generated substitute remains active: "+path);
                var stream=GameAudio.LoadActiveStream(path);
                using(stream){Require(stream is not null && stream.GetLength()>0,"original native decode failed: "+path);decoded.Add(new{path,seconds=stream!.GetLength()});}
            }
            checks.Add("only original active music/SFX/voices; native decode of every distinct active clip");
            StartPractice(Country.America,MatchSettings.Practice(3));await Wait(()=>Battle is not null);await Frame();await Frame();
            Require(optionsButton is null && battleOptions is null && FindChild("OpenBattleChat",true,false) is null,"top-left toolbar remains");
            await KeyPress(Key.Enter);Require(ChatTyping && SelectedBattleChatChannel=="all" && battleChatInput!.HasFocus(),"Enter all composer");
            Require(battleChatInput!.GetGlobalRect().GetCenter().Y>battlefield!.GetGlobalRect().End.Y*.65f,"composer not below center");
            battleChatInput.Text="全体消息";await KeyPress(Key.Enter);Require(!ChatTyping,"Enter did not send");
            await KeyPress(Key.Enter,true);Require(ChatTyping && SelectedBattleChatChannel=="team","Shift+Enter team composer");
            battleChatInput.Text="队伍消息";await KeyPress(Key.Enter);RefreshBattleChat();
            Require(battleChatPanel is not PanelContainer && battleChatLog is not null && !battleChatLog.BbcodeEnabled,"chat log background/markup");
            var transcript=battleChatLog!.GetParsedText();Require(transcript.Contains("[所有人]") && transcript.Contains("[队伍]") && transcript.Contains("全体消息") && transcript.Contains("队伍消息"),"minimal transcript/channel text");
            Require(!transcript.Contains("00:") && !transcript.Contains("(0)"),"timestamps or IDs remain");
            Require(battleChatPanel!.Position.X<=20 && battleChatPanel.Position.Y<60,"message log not left/top");
            await KeyPress(Key.Enter);battleChatInput!.Text="取消草稿";await KeyPress(Key.Escape);Require(!ChatTyping && !MatchMenuOpen,"Esc did not only cancel draft");
            checks.Add("no top toolbar; bottom composer; left transparent transcript with channel/name/text; Enter/ShiftEnter/send/cancel");
            await KeyPress(Key.Escape);Require(MatchMenuOpen && surrenderMenuButton!.Visible,"Esc menu unavailable");
            var artwork=matchMenu!.FindChild("MatchDialogArtwork",true,false) as TextureRect;
            Require(artwork?.Texture is{} allied && allied.GetWidth()==451 && allied.GetHeight()==326,"original Allied dialog artwork missing");
            var alliedTexture=artwork!.Texture;
            await KeyPress(Key.Escape);Require(!MatchMenuOpen,"Esc menu close");
            fixedZoom=battlefield!.Zoom;visibleArtHeight=battlefield.VisibleArtHeight;
            Require(Math.Abs(fixedZoom-Battlefield.ClassicZoom)<.0001f && Math.Abs(fixedZoom-SidebarScale)<.0001f,"battlefield/sidebar artwork scales differ");
            var center=battlefield.GetGlobalRect().GetCenter();MouseMove(center,Vector2.Zero);PushMouseButton(center,MouseButton.WheelUp,true);PushMouseButton(center,MouseButton.WheelUp,false);
            Require(Math.Abs(battlefield.Zoom-fixedZoom)<.0001f,"active player zoom changed");
            battlefield.SetCameraCell(new Vector2(48,48));
            var radar=minimap!;var corners=new[]{Vector2.Zero,new Vector2(battlefield.Size.X,0),battlefield.Size,new Vector2(0,battlefield.Size.Y)};
            var frame=radar.ViewportPolygon();Require(frame.Length==4,"radar viewport is not four corners");
            for(var i=0;i<4;i++)Require(radar.RadarToMap(frame[i]).DistanceTo(battlefield.Unproject(corners[i]))<.001f,"radar frame mismatches actual view");
            var local=radar.MapToRadar(battlefield.CameraCell+new Vector2(1,1));var global=radar.GlobalPosition+local;
            dragBefore=battlefield.CameraCell;MouseMove(global,global-center);PushMouseButton(global,MouseButton.Left,true);Require(radar.DraggingViewport,"radar drag not captured");
            Require(battlefield.CameraCell.DistanceTo(dragBefore)<.001f,"grabbing frame jumped camera");
            var delta=new Vector2(10,6);MouseMove(global+delta,delta,MouseButtonMask.Left);dragAfter=battlefield.CameraCell;
            Require(dragAfter.DistanceTo(dragBefore)>2,"drag did not move camera");
            PushMouseButton(new Vector2(20,20),MouseButton.Left,false);Require(!radar.DraggingViewport,"outside release retained capture");
            var released=battlefield.CameraCell;MouseMove(global+delta*2,delta);Require(released.DistanceTo(battlefield.CameraCell)<.001f,"camera moved after release");
            checks.Add("shared original artwork scale and locked combat zoom; flat radar exact footprint, grab offset, continuous motion/outside release");
            Issue(new GameCommand{Type=CommandType.Surrender});await Wait(()=>IsSpectating);await Frame();
            battlefield.SetCameraCell(new Vector2(48,48));center=battlefield.GetGlobalRect().GetCenter();
            MouseMove(center,Vector2.Zero);PushMouseButton(center,MouseButton.WheelUp,true);PushMouseButton(center,MouseButton.WheelUp,false);
            Require(Math.Abs(battlefield.Zoom-fixedZoom)<.0001f,"spectator zoom must stay locked");
            await KeyPress(Key.Escape);Require(MatchMenuOpen && !surrenderMenuButton!.Visible,"spectator surrender remains visible");
            ReturnToDeployment();StartPractice(Country.Russia,MatchSettings.Practice(3));await Wait(()=>Battle is not null);await Frame();await Frame();
            await KeyPress(Key.Escape);artwork=matchMenu!.FindChild("MatchDialogArtwork",true,false) as TextureRect;
            Require(artwork?.Texture is{} soviet && soviet.GetWidth()==451 && soviet.GetHeight()==326 && !ReferenceEquals(soviet,alliedTexture),"original Soviet dialog artwork missing");
            checks.Add("all-mode locked wheel zoom and hidden spectator surrender; distinct original Allied/Soviet dialog textures");
            ReturnToDeployment();
        }
        catch(Exception exception){error=exception.ToString();GD.PushError(error);}
        var pathOut=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://"+options.GetValueOrDefault("report","Reports/presentation-alignment.json")));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(pathOut)!);
        System.IO.File.WriteAllText(pathOut,JsonSerializer.Serialize(new{passed=error.Length==0,checks,decoded,fixedZoom,visibleArtHeight,dragBefore=new[]{dragBefore.X,dragBefore.Y},dragAfter=new[]{dragAfter.X,dragAfter.Y},error,oldRegressionSuitesRun=false},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print($"PRESENTATION CHECKS {(error.Length==0?"PASS":"FAIL")}: {checks.Count} groups");GetTree().Quit(error.Length==0?0:1);
    }
}
