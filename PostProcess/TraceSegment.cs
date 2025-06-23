using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence.ParquetDotNet;
using Temp.Core;

namespace PostProcess;

internal class TraceSegment : IAsyncDisposable
{
    private readonly Batcher<TraceEntry> _batcher;
    readonly Batcher<StackRange> _rangeBatcher;
    private readonly int _segmentId;

    private TraceSegment(Batcher<TraceEntry> traceBatcher, Batcher<StackRange> rangeBatcher, int segmentId)
    {
        _batcher = traceBatcher;
        _rangeBatcher = rangeBatcher;
        _segmentId = segmentId;
    }

    public static async Task<TraceSegment> CreateAsync(string outputDir, int segmentId)
    {
        var segmentDirectory = Path.Combine(outputDir, $"segment_{segmentId}");
        Directory.CreateDirectory(segmentDirectory);

        var traceOutputFile = Path.Combine(segmentDirectory, "traces.parquet");
        var stackOutputFile = Path.Combine(segmentDirectory, "stack_range.parquet");
        var tracePersistence = await ParquetTracePersistence.Create(traceOutputFile, Configuration.CompressionMethod);
        var stackRangePersistence = await ParquetStackRangePersistence.Create(stackOutputFile, Configuration.CompressionMethod);
        var traceBatcher = Batcher<TraceEntry>.Create(tracePersistence, Configuration.BatchSize, BatchingMode.OnFull);
        var stackRangeBatcher = Batcher<StackRange>.Create(stackRangePersistence, Configuration.BatchSize, BatchingMode.OnFull);
        return new TraceSegment(traceBatcher, stackRangeBatcher, segmentId);
    }

    readonly Stack<long> _stackStarts = [];
    public void Process(TraceEntry trace)
    {
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
        {
            _stackStarts.Push((long)trace.Id);
            //Console.WriteLine($"segment:{_segmentId} ip:0x{trace.IpAddress:x} sym:{(string.IsNullOrEmpty(trace.IpSym) ? "N/A" : trace.IpSym)} dso:{(string.IsNullOrEmpty(trace.IpDso) ? "N/A" : trace.IpDso)}{(trace.HaveAddress ? $" addr:0x{trace.AddressAddress:x} asym:{(string.IsNullOrEmpty(trace.AddressSym) ? "N/A" : trace.AddressSym)} adso:{(string.IsNullOrEmpty(trace.AddressDso) ? "N/A" : trace.AddressDso)}" : "")}");
        }
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
        {
            var startTrace = _stackStarts.Count == 0 ? -1 : _stackStarts.Pop();
            var stackRange = new StackRange()
            {
                StartTrace = startTrace,
                EndTrace = (long)trace.Id
            };
            _rangeBatcher.Persist(stackRange);
        }
        _batcher.Persist(trace);
    }

    public async ValueTask DisposeAsync()
    {
        var trace = _batcher.DisposeAsync();
        var range = _rangeBatcher.DisposeAsync();

        await trace;
        await range;

    }
}
