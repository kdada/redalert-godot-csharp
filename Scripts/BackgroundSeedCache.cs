using System.Threading;

namespace RedAlert;

/// <summary>
/// Pure managed, bounded LRU task cache for immutable seed-derived data. Factories run on
/// worker threads, with at most two active factories by default. Never pass a factory
/// that accesses Godot objects. Consumers must not mutate a shared result (in particular,
/// do not cache live World or BattleMap instances here).
/// </summary>
public sealed class BackgroundSeedCache<T>
{
    sealed record Entry(uint Seed,Task<T> Work);
    readonly object sync=new();
    readonly Dictionary<uint,LinkedListNode<Entry>> entries=[];
    readonly LinkedList<Entry> recent=new();
    readonly SemaphoreSlim workers;
    readonly Func<uint,T> generate;
    public int Capacity {get;}
    public int CachedSeedCount {get{lock(sync)return entries.Count;}}

    public BackgroundSeedCache(Func<uint,T> generate,int capacity=6,int concurrency=2)
    {
        ArgumentNullException.ThrowIfNull(generate);
        if(capacity is <1 or >8)throw new ArgumentOutOfRangeException(nameof(capacity));
        if(concurrency<1)throw new ArgumentOutOfRangeException(nameof(concurrency));
        this.generate=generate;Capacity=capacity;workers=new SemaphoreSlim(concurrency,concurrency);
    }

    /// <summary>
    /// Coalesces resident requests and retains at most Capacity tasks across page lifetimes.
    /// Eviction does not cancel work already awaited by another consumer. Faulted entries
    /// are retried on the next request. No completion callback accesses a scene/control.
    /// </summary>
    public Task<T> GetAsync(uint seed)
    {
        lock(sync)
        {
            if(entries.TryGetValue(seed,out var cached))
            {
                recent.Remove(cached);
                if(!cached.Value.Work.IsFaulted && !cached.Value.Work.IsCanceled)
                {recent.AddFirst(cached);return cached.Value.Work;}
                entries.Remove(seed);
            }
            var work=Task.Run(async ()=>
            {
                await workers.WaitAsync().ConfigureAwait(false);
                try{return generate(seed);}
                finally{workers.Release();}
            });
            // An evicted request may have no remaining UI consumer; still observe failures.
            _=work.ContinueWith(fault=>{_ = fault.Exception;},CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            entries.Add(seed,recent.AddFirst(new Entry(seed,work)));
            if(entries.Count>Capacity)
            {
                var oldest=recent.Last!;recent.RemoveLast();entries.Remove(oldest.Value.Seed);
            }
            return work;
        }
    }
}
