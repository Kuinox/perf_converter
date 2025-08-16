namespace Temp.Schema.FuchsiaTraceFormat;

public sealed class ThreadCache()
{
    readonly LruCache<(ulong pid, ulong tid), byte> _lru = new(256 - (int)RESERVED);
    readonly Dictionary<(ulong pid, ulong tid), List<string>> _commHistory = new();
    const ulong RESERVED = 1;

    public CacheRef GetRef(Stream w, (ulong pid, ulong tid) pidTid)
    {
        return GetRef(w, pidTid, null);
    }

    public CacheRef GetRef(Stream w, (ulong pid, ulong tid) pidTid, string? comm)
    {
        // Update comm history if comm is provided and not empty
        bool commUpdated = false;
        if (!string.IsNullOrEmpty(comm))
        {
            commUpdated = UpdateCommHistory(pidTid, comm);
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

    private bool UpdateCommHistory((ulong pid, ulong tid) pidTid, string comm)
    {
        if (!_commHistory.TryGetValue(pidTid, out var history))
        {
            history = new List<string>();
            _commHistory[pidTid] = history;
        }

        // Only add if it's different from the last comm
        if (history.Count == 0 || history[^1] != comm)
        {
            history.Add(comm);
            return true; // History was updated
        }
        return false; // No change
    }

    private string GetThreadName((ulong pid, ulong tid) pidTid)
    {
        if (_commHistory.TryGetValue(pidTid, out var history) && history.Count > 0)
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
