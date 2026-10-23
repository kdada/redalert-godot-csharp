using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using RedAlert.Simulation;

namespace RedAlert;

// Real local ENet peers, polled without a scene-tree multiplayer layer or any window.
public static class HeadlessNetworkChecks
{
    public static object Run()
    {
        var results=new List<object>();
        void Test(string name,Action action)
        {
            var watch=Stopwatch.StartNew();action();results.Add(new{name,passed=true,milliseconds=watch.Elapsed.TotalMilliseconds});Godot.GD.Print("PASS NETWORK "+name);
        }
        Test("lobby departure compacts slots; Unicode names and oversized canonical batches",()=>
        {
            var port=FreePort();using var host=new NetworkSession();using var leaving=new NetworkSession();using var survivor=new NetworkSession();
            host.Host(port,new string('H',31)+"😀",Country.America);
            leaving.Join("127.0.0.1",port,"A",Country.Russia);
            Spin("first lobby peer",()=>host.Members.Count==2 && leaving.MyPlayer==1,()=>Poll(host,leaving));
            survivor.Join("127.0.0.1",port,"B",Country.France);
            Spin("second lobby peer",()=>host.Members.Count==3 && survivor.MyPlayer==2,()=>Poll(host,leaving,survivor));
            leaving.Dispose();
            Spin("lobby departure",()=>host.Members.Count==2,()=>Poll(host,survivor));
            Check(host.StartMatch(),"start rejected");
            Spin("remaining peers ready",()=>host.Running&&survivor.Running,()=>Poll(host,survivor));
            Check(survivor.MyPlayer==1 && survivor.World!.Players.Length==2,"sparse slot survived into settings");
            var actor=survivor.World!.Units.First(u=>u.Owner==1).Id;
            var actors=Enumerable.Repeat(actor,Rules.MaxEntities).ToArray();
            for(var i=0;i<64;i++)survivor.Submit(new GameCommand{Type=CommandType.Stop,Actors=actors});
            Spin("large input burst",()=>host.ReceivedCommands==64,()=>Poll(host,survivor),10000);
            Advance(host,survivor,200);
            Check(host.ReplayCommands.Count==64 && host.ReplayCommands.Select(c=>c.Tick).Distinct().Count()>1,"large canonical batch was not bounded across frames");
            Check(host.ReplayCommands.Select(c=>c.Sequence).SequenceEqual(Enumerable.Range(1,64)),"input order changed during batch spill");
            Check(host.RejectedPackets==0 && survivor.RejectedPackets==0,"valid input rejected");
            SameAndReplay(host,survivor);
        });
        Test("disconnect during loading releases readiness and canonically surrenders",()=>
        {
            var port=FreePort();using var host=new NetworkSession();using var leaving=new NetworkSession();using var survivor=new NetworkSession();
            host.Host(port,"Host",Country.America);leaving.Join("127.0.0.1",port,"A",Country.Russia);
            Spin("first loading peer",()=>host.Members.Count==2 && leaving.MyPlayer==1,()=>Poll(host,leaving));
            survivor.Join("127.0.0.1",port,"B",Country.Britain);
            Spin("second loading peer",()=>host.Members.Count==3 && survivor.MyPlayer==2,()=>Poll(host,leaving,survivor));
            Check(host.StartMatch(),"loading start rejected");
            // Deliberately stop polling A before it can receive start/send ready.
            Spin("only survivor ready",()=>host.Members.Single(m=>m.Player.Name=="B").Ready,()=>Poll(host,survivor));
            Check(!host.Running,"readiness did not wait for A");leaving.Dispose();
            Spin("loading barrier released",()=>host.Running&&survivor.Running,()=>Poll(host,survivor));
            Advance(host,survivor,200);
            Check(host.World!.Players[1].Defeated && !host.World.Units.Any(u=>u.Owner==1) && !host.World.Buildings.Any(b=>b.Owner==1),"loading departure left an active player");
            Check(host.ReplayCommands.Any(c=>c.Player==1 && c.Type==CommandType.Surrender),"surrender absent from canonical replay");
            SameAndReplay(host,survivor);
        });
        Test("unreachable initial server reaches a bounded connection failure",()=>
        {
            using var client=new NetworkSession();client.Join("127.0.0.1",FreePort(),"NoServer",Country.America);
            Spin("initial connection failure",()=>client.Error.Length>0,()=>client.Poll(),12500);
            Check(!client.Running && !client.Started,"failed join created a match");
        });
        return new{passed=true,totalTests=results.Count,tests=results,content=World.ContentFingerprint()};
    }
    static void SameAndReplay(NetworkSession host,NetworkSession client)
    {
        Check(host.World!.StateHash()==client.World!.StateHash(),"peer states differ");
        Check(host.Replay().Play().StateHash()==host.World.StateHash(),"server replay differs");
        Check(client.Replay().Play().StateHash()==client.World.StateHash(),"client replay differs");
    }
    static void Advance(NetworkSession host,NetworkSession client,int tick)
    {
        Spin("canonical advance",()=>host.World!.Tick==tick&&client.World!.Tick==tick,()=>
        {
            Poll(host,client);if(host.World!.Tick<tick)host.StepServer();
        },10000);
    }
    static void Poll(params NetworkSession[] peers)
    {
        foreach(var peer in peers){peer.Poll();if(peer.Error.Length>0)throw new InvalidOperationException(peer.Error);}
    }
    static void Spin(string label,Func<bool> finished,Action pump,int milliseconds=5000)
    {
        var watch=Stopwatch.StartNew();
        while(!finished())
        {
            pump();if(watch.ElapsedMilliseconds>milliseconds)throw new TimeoutException(label);Thread.Sleep(1);
        }
    }
    static void Check(bool result,string message){if(!result)throw new InvalidOperationException(message);}
    static int FreePort()
    {
        using var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);socket.Bind(new IPEndPoint(IPAddress.Loopback,0));return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
