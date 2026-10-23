using System.Text.Json;

namespace RedAlert.Simulation;

public struct StableHash
{
    ulong value;
    bool initialized;
    public readonly ulong Value => initialized?value:14695981039346656037UL;
    public void Add(int n)=>Add(unchecked((uint)n));
    public void Add(bool b)=>Add(b?1:0);
    public void Add(uint n)
    {
        if(!initialized){value=14695981039346656037UL;initialized=true;}
        for(var i=0;i<4;i++){value=unchecked((value^(byte)n)*1099511628211UL);n>>=8;}
    }
    public void Add(string? text)
    {
        if(text is null){Add(-1);return;}
        Add(text.Length);foreach(var c in text)Add((int)c);
    }
}

public sealed class ReplayFile
{
    public int Protocol {get;init;}=Rules.Protocol;
    public string Content {get;init;}=World.ContentFingerprint();
    public required MatchSettings Settings {get;init;}
    public required List<GameCommand> Commands {get;init;}
    public int FinalTick {get;init;}
    public string FinalHash {get;init;}="";
    public static JsonSerializerOptions JsonOptions {get;}=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    void Validate()
    {
        if(Protocol!=Rules.Protocol || Content!=World.ContentFingerprint())throw new InvalidDataException("录像版本或规则不匹配");
        if(Settings is null || FinalTick is <0 or >10_000_000 || Commands is null || Commands.Count>1_000_000
            || Commands.Any(c=>c is null || c.Tick<0 || c.Tick>=FinalTick)
            || FinalHash is null || FinalHash.Length!=16 || FinalHash.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("录像范围或状态校验无效");
        Settings.Validate();
    }
    public void Save(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,JsonSerializer.Serialize(this,JsonOptions));
    }
    public static ReplayFile Load(string path)
    {
        if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException("录像文件过大");
        var replay=JsonSerializer.Deserialize<ReplayFile>(File.ReadAllText(path),JsonOptions)??throw new InvalidDataException("无效录像");
        replay.Validate();return replay;
    }
    public World Play()
    {
        Validate();
        var world=new World(Settings);var groups=Commands.GroupBy(c=>c.Tick).ToDictionary(g=>g.Key,g=>g.ToArray());
        while(world.Tick<FinalTick){groups.TryGetValue(world.Tick,out var orders);world.Step(orders);}
        var actual=world.StateHash().ToString("x16");
        if(!string.Equals(actual,FinalHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException($"录像校验失败：{actual} != {FinalHash}");
        return world;
    }
}
