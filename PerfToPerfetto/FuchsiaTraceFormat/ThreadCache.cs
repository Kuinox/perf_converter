namespace Temp.Schema.FuchsiaTraceFormat;

public sealed class ThreadCache()
{
    readonly LruCache<(ulong pid, ulong tid), byte> _lru = new(256 - (int)RESERVED);
    const ulong RESERVED = 1;

    public CacheRef GetRef(Stream w, (ulong pid, ulong tid) pidTid)
    {
        if (_lru.TryGet(pidTid, out var idx))
        {
            return CacheRef.From(idx + RESERVED);
        }
        else
        {
            byte newIdx = _lru.Count < _lru.Capacity ? (byte)_lru.Count : _lru.PopLru().Value;
            _lru.Put(pidTid, newIdx);

            var outIdx = newIdx + RESERVED;
            WriteThreadRecord(w, outIdx, pidTid);
            return CacheRef.From(outIdx);
        }
    }

    static void WriteThreadRecord(Stream w, ulong idx, (ulong pid, ulong tid) pidTid)
    {
        const ulong rsize = 3UL, rtype = 3UL;
        Impl.WriteU64(w, rtype | rsize << 4 | idx << 16);
        Impl.WriteU64(w, pidTid.pid);
        Impl.WriteU64(w, pidTid.tid);
    }
}
