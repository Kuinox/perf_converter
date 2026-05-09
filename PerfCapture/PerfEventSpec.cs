namespace PerfCapture;

public abstract record PerfEventSpec
{
    public static NamedPerfEventSpec Named(PerfEventName name) => new(name);

    public static NamedPerfEventSpec CpuClock() => new("cpu-clock");
}
