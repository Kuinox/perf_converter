namespace Temp.Schema.FuchsiaTraceFormat;

public sealed class StringCache()
{
    readonly LruCache<string, ushort> _lru = new(32 * 1024 - (int)RESERVED);
    static readonly ulong RESERVED = (ulong)Enum.GetValues(typeof(InternalString)).Length;

    public CacheRef GetRef(Stream w, string s)
    {
        if (string.IsNullOrEmpty(s)) return CacheRef.From(0);

        if (_lru.TryGet(s, out var idx))
        {
            return CacheRef.From(idx + RESERVED);
        }
        else
        {
            ushort newIdx = _lru.Count < _lru.Capacity ? (ushort)_lru.Count : _lru.PopLru().Value;
            _lru.Put(s, newIdx);
            var outIdx = newIdx + RESERVED;
            WriteStringRecord(w, outIdx, s);
            return CacheRef.From(outIdx);
        }
    }

    static void WriteStringRecord(Stream w, ulong idx, string s)
    {
        const int MAX_STRING_LEN = 32000;
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        var sLen = Math.Min(bytes.Length, MAX_STRING_LEN);
        var rsize = 1UL + Impl.WordsForBytes((ulong)sLen);
        const ulong rtype = 2UL;

        Impl.WriteU64(w, rtype | rsize << 4 | idx << 16 | (ulong)sLen << 32);
        Impl.WriteString(w, bytes.AsSpan(0, sLen));
    }
}
