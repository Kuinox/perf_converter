using System.Runtime.InteropServices;

namespace Temp.Schema.FuchsiaTraceFormat;

public sealed class ThreadCache()
{
    readonly LruCache<(ulong pid, ulong tid), byte> _lru = new(256 - (int)RESERVED);
    readonly Dictionary<(ulong pid, ulong tid), List<string>?> _commHistory = new();
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
            var threadName = GetThreadName(pidTid);
            WriteThreadRecord(w, outIdx, pidTid, threadName);
            return CacheRef.From(outIdx);
        }
    }

    public void UpdateComm((ulong pid, ulong tid) pidTid, string comm)
    {
        if (string.IsNullOrEmpty(comm))
            return;

        ref var history = ref CollectionsMarshal.GetValueRefOrAddDefault(_commHistory, pidTid, out var exists);
        if (!exists)
        {
            history = [];
        }

        // Only add if it's different from the last comm
        var lastComm = history?.Count > 0 ? history[^1] : null;
        if (lastComm != comm)
        {
            history ??= [];
            history.Add(comm);
        }
    }

    public CacheRef GetRef(Stream w, (ulong pid, ulong tid) pidTid, string? comm)
    {
        // Update comm history if comm is provided and not empty
        bool commUpdated = false;
        if (!string.IsNullOrEmpty(comm))
        {
            var historyBefore = _commHistory.TryGetValue(pidTid, out var existing) ? existing?.Count ?? 0 : 0;
            UpdateComm(pidTid, comm);
            var historyAfter = _commHistory.TryGetValue(pidTid, out var updated) ? updated?.Count ?? 0 : 0;
            commUpdated = historyAfter > historyBefore;
        }

        if (_lru.TryGet(pidTid, out var idx))
        {
            // If comm was updated, rewrite the thread record with new name
            if (commUpdated)
            {
                var threadName = GetThreadName(pidTid);
                WriteThreadRecord(w, idx + RESERVED, pidTid, threadName);
            }
            return CacheRef.From(idx + RESERVED);
        }
        else
        {
            byte newIdx = _lru.Count < _lru.Capacity ? (byte)_lru.Count : _lru.PopLru().Value;
            _lru.Put(pidTid, newIdx);

            var outIdx = newIdx + RESERVED;
            var threadName = GetThreadName(pidTid);
            WriteThreadRecord(w, outIdx, pidTid, threadName);
            return CacheRef.From(outIdx);
        }
    }

    private string GetThreadName((ulong pid, ulong tid) pidTid)
    {
        var history = _commHistory.TryGetValue(pidTid, out var h) ? h : null;
        if (history?.Count > 0)
        {
            return string.Join(" => ", history);
        }
        return $"{pidTid.tid}"; // Fallback to just tid if no comm history
    }

    static void WriteThreadRecord(Stream w, ulong idx, (ulong pid, ulong tid) pidTid, string threadName)
    {
        // Calculate thread name bytes and required size
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(threadName);
        var nameLen = nameBytes.Length;
        var nameWords = Impl.WordsForBytes((ulong)nameLen);
        
        // Thread record with extended format: header, pid, tid, name_len, name_data
        var rsize = 3UL + 1UL + nameWords; // pid + tid + name_len + name_data_words
        const ulong rtype = 3UL;
        
        Impl.WriteU64(w, rtype | rsize << 4 | idx << 16 | (ulong)nameLen << 32);
        Impl.WriteU64(w, pidTid.pid);
        Impl.WriteU64(w, pidTid.tid);
        
        // Write the thread name as string data
        Impl.WriteString(w, nameBytes);
    }
}
