namespace Temp.Schema.FuchsiaTraceFormat;

public readonly record struct CacheRef(ulong Idx)
{
    public static implicit operator ulong(CacheRef r) => r.Idx;
    public static CacheRef From(ulong idx) => new(idx);
}
