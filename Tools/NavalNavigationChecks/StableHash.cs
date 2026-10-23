namespace RedAlert.Simulation;

// Exact data-hash implementation from Simulation/Replay.cs. ClassicMapObjects
// declares AddToFingerprint; keeping this support type here lets the probe compile
// only map/data sources while other agents edit World/economy/combat partials.
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
