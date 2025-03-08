namespace PerfConverter;

public class JsonFrameHandler : IFrameHandler
{
    private readonly StreamWriter _writer;

    public JsonFrameHandler(Stream outputStream)
    {
        _writer = new StreamWriter(outputStream);
        _writer.WriteLine("["); // Start JSON array
    }

    public void HandleFrameStart(string symbol, ulong timestamp, (ulong, ulong) processThreadId)
    {
        _writer.WriteLine($"{{\"type\":\"start\",\"symbol\":\"{symbol}\",\"timestamp\":{timestamp}," +
                        $"\"pid\":{processThreadId.Item1},\"tid\":{processThreadId.Item2}}},");
    }

    public void HandleFrameEnd(ulong timestamp, (ulong, ulong) processThreadId,
        ulong instructions, ulong cycles, ulong footprint,
        ulong startTimestamp, ulong endTimestamp)
    {
        _writer.WriteLine($"{{\"type\":\"end\",\"timestamp\":{timestamp}," +
                        $"\"pid\":{processThreadId.Item1},\"tid\":{processThreadId.Item2}," +
                        $"\"instructions\":{instructions},\"cycles\":{cycles}," +
                        $"\"footprint\":{footprint},\"start_ts\":{startTimestamp}," +
                        $"\"end_ts\":{endTimestamp}}},");
    }

    public void HandleFrameFull(string symbol, ulong timestamp, (ulong, ulong) processThreadId,
        ulong instructions, ulong cycles, ulong footprint, ulong endTimestamp,
        ulong startTimestamp, ulong endFullTimestamp)
    {
        _writer.WriteLine($"{{\"type\":\"full\",\"symbol\":\"{symbol}\",\"timestamp\":{timestamp}," +
                        $"\"pid\":{processThreadId.Item1},\"tid\":{processThreadId.Item2}," +
                        $"\"instructions\":{instructions},\"cycles\":{cycles}," +
                        $"\"footprint\":{footprint},\"end_ts\":{endTimestamp}," +
                        $"\"start_ts\":{startTimestamp},\"end_full_ts\":{endFullTimestamp}}},");
    }

    public void Finish()
    {
        _writer.WriteLine("]"); // End JSON array
        _writer.Flush();
    }
}