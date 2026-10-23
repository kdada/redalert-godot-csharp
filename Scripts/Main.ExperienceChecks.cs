using Godot;
using RedAlert.Simulation;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RedAlert;

public partial class Main
{
    async void RunExperienceChecks()
    {
        var checks=new List<string>();string error="";NetworkSession? server=null,guest=null,third=null;
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        async Task Frame()=>await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        async Task Wait(Func<bool> ready,Action? pump=null)
        {
            var watch=Stopwatch.StartNew();while(!ready()){pump?.Invoke();if(watch.Elapsed.TotalSeconds>6)throw new TimeoutException("focused check timed out");await Frame();}
        }
        void KeyPress(Key code,bool shift=false)
        {
            using var down=new InputEventKey{Keycode=code,PhysicalKeycode=code,ShiftPressed=shift,Pressed=true};GetViewport().PushInput(down,true);
            using var up=new InputEventKey{Keycode=code,PhysicalKeycode=code,ShiftPressed=shift,Pressed=false};GetViewport().PushInput(up,true);
        }
        try
        {
            var shared=MenuMapPreviewCache.GetAsync(20261018);Require(ReferenceEquals(shared,MenuMapPreviewCache.GetAsync(20261018)),"preview task is not shared");
            await Wait(()=>shared.IsCompleted);var payload=shared.GetAwaiter().GetResult();Require(payload.CopyRgbaPixels().Length==96*96*4,"preview pixels");
            var preview=new ShellMapPreview{Seed=20261018,Players=3,Size=new Vector2(300,200)};AddChild(preview);
            preview.Seed=20261019;preview.Seed=20261018;await Wait(()=>preview.PreviewReady);
            Require(MenuMapPreviewCache.CachedSeedCount<=MenuMapPreviewCache.Capacity,"preview cache bound");RemoveChild(preview);preview.QueueFree();checks.Add("async preview sharing, seed replacement, texture upload and disposal");
            foreach(var clip in GameAudio.ActiveFiles.Values.Distinct())
            {using var stream=GameAudio.LoadActiveStream(clip);Require(stream is not null && stream.GetLength()>0,"native original audio decode: "+clip);}
            checks.Add($"native original audio decoding ({GameAudio.ActiveFiles.Count} mapped clips; playback/listening deferred)");
            StartPractice(Country.America,MatchSettings.Practice(3));await Wait(()=>Battle is not null);await Frame();
            KeyPress(Key.Enter);await Frame();Require(ChatTyping && battleChatChannel!.Text.Contains("所有人"),"Enter must open all chat");
            Require(Math.Abs(battleChatInput!.GetGlobalRect().GetCenter().X-battlefield!.GetGlobalRect().GetCenter().X)<100,"chat composer center");
            Require(battleChatInput.GetGlobalRect().GetCenter().Y>battlefield.GetGlobalRect().End.Y*.65f,"chat composer bottom");
            KeyPress(Key.Escape);Require(!ChatTyping && !MatchMenuOpen,"chat Esc must only cancel");
            KeyPress(Key.Enter,true);Require(ChatTyping && battleChatChannel!.Text.Contains("队"),"Shift Enter must open team chat");
            battleChatInput!.Text="本轮定向检查";KeyPress(Key.Enter);Require(!ChatTyping && CurrentBattleChat!.Recent(0,5).Length==1,"Enter sends chat");
            KeyPress(Key.Escape);Require(MatchMenuOpen && surrenderMenuButton!.Visible,"central Esc menu/surrender");HideMatchMenu();
            Issue(new GameCommand{Type=CommandType.Surrender});await Wait(()=>Battle!.Players[0].Defeated);
            Require(IsSpectating && ViewVisible(95,95) && ViewExplored(95,95),"defeat global spectator");
            Require(!Battle!.Units.Any(u=>u.Owner==0) && !Battle.Buildings.Any(b=>b.Owner==0) && !Battle.Finished,"same-tick cleanup while two teams continue");
            ShowMatchMenu();Require(!surrenderMenuButton!.Visible,"spectator surrender must be hidden");HideMatchMenu();
            var current=Battle;current.Step([new GameCommand{Tick=current.Tick,Player=1,Type=CommandType.Surrender}]);
            await Wait(()=>current.Finished && MatchMenuOpen);Require(current.WinningTeam==2,"team victory result menu");
            ReturnToDeployment();checks.Add("real key events: bottom chat/all/team/send/cancel, Esc menu, defeat/spectator/team result");
            using(var socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
            {
                var port=((IPEndPoint)socket.Client.LocalEndPoint!).Port;socket.Close();
                server=new NetworkSession();guest=new NetworkSession();third=new NetworkSession();
                server.Host(port,"房主",Country.America,0);guest.Join("127.0.0.1",port,"玩家二",Country.Russia,1);third.Join("127.0.0.1",port,"玩家三",Country.France,2);
                void Pump(){server.Poll();guest.Poll();third.Poll();}
                await Wait(()=>server.Members.Count==3,Pump);Require(server.StartMatch(),"LAN start request");
                await Wait(()=>server.Running && guest.Running && third.Running,Pump);
                Require(server.World!.StateHash()==guest.World!.StateHash() && guest.World.StateHash()==third.World!.StateHash(),"async LAN initial hash");
                server.Submit(new GameCommand{Type=CommandType.Surrender});
                await Wait(()=>server.World.Players[0].Defeated && guest.World!.Players[0].Defeated && third.World!.Players[0].Defeated,()=>{Pump();server.StepServer();});
                Require(!server.World.Finished && server.Running && guest.Running,"defeated LAN participant must stay connected");
                guest.Submit(new GameCommand{Type=CommandType.Surrender});
                await Wait(()=>server.Completed && guest.Completed && third.Completed,()=>{Pump();server.StepServer();});
                Require(server.World.Finished && server.World.WinningTeam==2 && server.World.StateHash()==guest.World!.StateHash() && guest.World.StateHash()==third.World!.StateHash(),"LAN final team result/hash");
            }
            checks.Add("3-peer ENet: asynchronous loading, same initial hash, defeated session retained, final result/hash acknowledged");
        }
        catch(Exception ex){error=ex.ToString();GD.PushError(error);}
        finally{server?.Dispose();guest?.Dispose();third?.Dispose();}
        var path=options.GetValueOrDefault("report","Reports/experience-ui.json");var absolute=System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://"+path));System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolute)!);
        System.IO.File.WriteAllText(absolute,JsonSerializer.Serialize(new{passed=error.Length==0,checks,error,slowRegressionsRun=false},new JsonSerializerOptions{WriteIndented=true}));
        GD.Print($"EXPERIENCE CHECKS {(error.Length==0?"PASS":"FAIL")}: {checks.Count} groups");GetTree().Quit(error.Length==0?0:1);
    }
}
