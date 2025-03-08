namespace PerfConverter;

public interface IFrameHandler
{
    void HandleFrameStart(string symbol, ulong timestamp, (ulong, ulong) processThreadId);
    void HandleFrameEnd(
        ulong timestamp,
        (ulong, ulong) processThreadId,
        ulong instructions,
        ulong cycles,
        ulong footprint,
        ulong startTimestamp,
        ulong endTimestamp);
    void HandleFrameFull(
        string symbol,
        ulong timestamp,
        (ulong, ulong) processThreadId,
        ulong instructions,
        ulong cycles,
        ulong footprint,
        ulong endTimestamp,
        ulong startTimestamp,
        ulong endFullTimestamp);
}