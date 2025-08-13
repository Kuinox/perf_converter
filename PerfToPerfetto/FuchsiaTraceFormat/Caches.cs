namespace Temp.Schema.FuchsiaTraceFormat;

public sealed class Caches(StringCache? stringCache = null, ThreadCache? threadCache = null)
{
    public StringCache StringCache { get; } = stringCache ?? new StringCache();
    public ThreadCache ThreadCache { get; } = threadCache ?? new ThreadCache();
}
