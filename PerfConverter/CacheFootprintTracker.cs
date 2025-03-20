namespace PerfConverter;

/// <summary>
/// Tracks and manages cache footprint data
/// </summary>
public class CacheFootprintTracker
{
    private const ulong CacheLineSize = 64;
    private const ulong CacheLineMask = ~(CacheLineSize - 1);
    
    public void UpdateFootprint(ThreadContext threadState, ulong startIp, ulong endIp)
    {
        var cacheLineStart = startIp & CacheLineMask;
        var cacheLineEnd = endIp & CacheLineMask;
        var currentFrame = threadState.CurrentFrame;
        
        if (currentFrame == null) return;
        
        // Insert cache lines into the footprint set
        var cacheLine = cacheLineStart;
        while (cacheLine <= cacheLineEnd)
        {
            currentFrame.Footprint.Add(cacheLine);
            cacheLine += CacheLineSize;
        }
    }
    
    public ulong CalculateFootprintSize(HashSet<ulong> footprint)
    {
        return (ulong)footprint.Count * CacheLineSize;
    }
    
    public HashSet<ulong> MergeFootprints(HashSet<ulong> a, HashSet<ulong> b)
    {
        // Always merge into the larger set for efficiency
        if (a.Count < b.Count)
        {
            (a, b) = (b, a);
        }
        
        a.UnionWith(b);
        return a;
    }
}
