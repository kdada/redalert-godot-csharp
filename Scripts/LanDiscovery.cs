using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using RedAlert.Simulation;

namespace RedAlert;

// A small LAN-only room browser. Discovery never changes an ENet match or accepts game commands.
public sealed class LanDiscovery : IDisposable
{
    public const int DiscoveryPort=19843;
    const string Product="RedAlert.Independent.LAN/1";
    readonly Socket socket;
    readonly bool publisher;
    readonly string roomId=Guid.NewGuid().ToString("N");
    readonly Dictionary<string,LanRoom> rooms=[];
    readonly byte[] buffer=new byte[2048];
    long nextQuery;
    public string Error {get;private set;}="";
    public IReadOnlyList<LanRoom> Rooms=>rooms.Values.OrderBy(r=>r.Name,StringComparer.Ordinal).ToArray();
    public LanDiscovery(bool publish=false)
    {
        publisher=publish;socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp){Blocking=false,EnableBroadcast=true};
        try{socket.Bind(new IPEndPoint(IPAddress.Any,publish?DiscoveryPort:0));}
        catch{socket.Dispose();throw;}
    }
    public void Poll(string roomName="",int gamePort=NetworkSession.DefaultPort,int players=0,bool started=false,string owner="")
    {
        if(Error.Length>0)return;
        var now=Environment.TickCount64;
        try
        {
            if(!publisher && now>=nextQuery)
            {
                nextQuery=now+1500;var query=JsonSerializer.SerializeToUtf8Bytes(new DiscoveryPacket{Product=Product,Kind="query"});
                socket.SendTo(query,new IPEndPoint(IPAddress.Broadcast,DiscoveryPort));
                socket.SendTo(query,new IPEndPoint(IPAddress.Loopback,DiscoveryPort));
            }
            for(var count=0;count<24 && socket.Poll(0,SelectMode.SelectRead);count++)
            {
                EndPoint from=new IPEndPoint(IPAddress.Any,0);var length=socket.ReceiveFrom(buffer,ref from);
                if(length is <2 or >1536 || from is not IPEndPoint source)continue;
                DiscoveryPacket? message;
                try{message=JsonSerializer.Deserialize<DiscoveryPacket>(buffer.AsSpan(0,length));}catch(JsonException){continue;}
                if(message is null || message.Product!=Product)continue;
                if(publisher && message.Kind=="query" && !started)
                {
                    var response=JsonSerializer.SerializeToUtf8Bytes(new DiscoveryPacket{Product=Product,Kind="room",Id=roomId,Name=Clean(roomName),Owner=Clean(owner),Port=gamePort,Players=players,Content=World.ContentFingerprint()});
                    socket.SendTo(response,source);
                }
                else if(!publisher && message.Kind=="room" && message.Content==World.ContentFingerprint() && message.Port is >=1024 and <=65535 && message.Players is >=1 and <=8)
                {
                    if(message.Id is not {Length:32})continue;
                    var address=source.Address.ToString();var key=message.Id;
                    if(rooms.TryGetValue(key,out var known) && !IPAddress.IsLoopback(IPAddress.Parse(known.Address)) && IPAddress.IsLoopback(source.Address))continue;
                    rooms[key]=new LanRoom(Clean(message.Name),address,message.Port,message.Players,now,Clean(message.Owner));
                }
            }
            foreach(var key in rooms.Where(pair=>now-pair.Value.LastSeen>5000).Select(pair=>pair.Key).ToArray())rooms.Remove(key);
        }
        catch(SocketException exception) when(exception.SocketErrorCode is SocketError.WouldBlock or SocketError.TryAgain){}
        catch(SocketException){Error="无法搜索局域网房间，请使用 IP 地址加入。";}
    }
    static string Clean(string? value)=>new((value??"").Where(c=>!char.IsControl(c)).Take(32).ToArray());
    public void Dispose()=>socket.Dispose();
    sealed class DiscoveryPacket
    {
        public string Product {get;set;}="";
        public string Kind {get;set;}="";
        public string Id {get;set;}="";
        public string Name {get;set;}="";
        public string Owner {get;set;}="";
        public string Content {get;set;}="";
        public int Port {get;set;}
        public int Players {get;set;}
    }
}
public sealed record LanRoom(string Name,string Address,int Port,int Players,long LastSeen,string Owner="");
