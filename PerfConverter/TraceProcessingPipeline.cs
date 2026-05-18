using System.Collections.Concurrent;
using System.Diagnostics;
using PerfConverter.Persistence;
using PerfConverter.Persistence.Plank;

namespace PerfConverter;

public sealed class TraceProcessingPipeline : IDisposable
{
    const int DefaultCapacity = 512;

    readonly ParquetPersistenceLifetime _persistenceLifetime;
    readonly TraceProcessor _traceProcessor;
    readonly BlockingCollection<PendingTraceSample> _pendingSamples;
    readonly BlockingCollection<ResolvedTraceSample> _resolvedSamples;
    readonly Thread[] _resolverThreads;
    readonly Thread _commitThread;
    readonly object _exceptionGate = new();
    readonly int _capacity;
    Exception? _exception;
    int _pendingDepth;
    int _resolvedDepth;
    ulong _nextSequence;
    bool _disposed;

    TraceProcessingPipeline(ParquetPersistenceLifetime persistenceLifetime, int capacity, int resolverCount)
    {
        _persistenceLifetime = persistenceLifetime;
        _capacity = capacity;
        _traceProcessor = new TraceProcessor(_persistenceLifetime.CreateTraceBatcher);
        _pendingSamples = new BlockingCollection<PendingTraceSample>(capacity);
        _resolvedSamples = new BlockingCollection<ResolvedTraceSample>(capacity);

        _resolverThreads = new Thread[resolverCount];
        for (var i = 0; i < _resolverThreads.Length; i++)
        {
            _resolverThreads[i] = new Thread(ResolveLoop)
            {
                IsBackground = true,
                Name = $"PerfConverterResolver{i}"
            };
            _resolverThreads[i].Start();
        }

        _commitThread = new Thread(CommitLoop)
        {
            IsBackground = true,
            Name = "PerfConverterCommit"
        };
        _commitThread.Start();
    }

    public static TraceProcessingPipeline Create()
    {
        var capacity = GetPositiveEnvironmentValue("PERFCONVERTER_TRACE_PIPELINE_CAPACITY", DefaultCapacity);
        var resolverCount = GetPositiveEnvironmentValue(
            "PERFCONVERTER_SYMBOL_RESOLUTION_WORKERS",
            Math.Min(capacity, Math.Max(1, Environment.ProcessorCount)));

        return new TraceProcessingPipeline(
            PersistenceFactory.CreatePersistence(),
            capacity,
            Math.Min(capacity, resolverCount));
    }

    public void Enqueue(OwnedPerfSample sample, ResolvedLocation ip, ResolvedLocation? address)
    {
        ThrowIfFaulted();

        var pending = new PendingTraceSample(
            Sequence: _nextSequence++,
            Sample: sample,
            Ip: ip,
            Address: address);

        var start = Stopwatch.GetTimestamp();
        _pendingSamples.Add(pending);
        PerfConverterMetrics.PipelineStageElapsed("enqueue.pending", Stopwatch.GetTimestamp() - start);
        PerfConverterMetrics.PipelineQueueDepth("pending", Interlocked.Increment(ref _pendingDepth), _capacity);
        ThrowIfFaulted();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _pendingSamples.CompleteAdding();

        try
        {
            foreach (var resolverThread in _resolverThreads)
                resolverThread.Join();

            _resolvedSamples.CompleteAdding();
            _commitThread.Join();
        }
        catch (Exception ex)
        {
            CaptureException(ex);
        }
        finally
        {
            _persistenceLifetime.Dispose();
            _disposed = true;
        }

        ThrowIfFaulted();
    }

    void ResolveLoop()
    {
        try
        {
            foreach (var pending in _pendingSamples.GetConsumingEnumerable())
            {
                PerfConverterMetrics.PipelineQueueDepth("pending", Interlocked.Decrement(ref _pendingDepth), _capacity);

                var start = Stopwatch.GetTimestamp();
                var ipLocationId = _persistenceLifetime.GetOrAddSourceLocation(pending.Ip);
                PerfConverterMetrics.PipelineStageElapsed("source.ip", Stopwatch.GetTimestamp() - start);

                var addressLocationId = 0UL;
                if (pending.Address is not null)
                {
                    start = Stopwatch.GetTimestamp();
                    addressLocationId = _persistenceLifetime.GetOrAddSourceLocation(pending.Address);
                    PerfConverterMetrics.PipelineStageElapsed("source.addr", Stopwatch.GetTimestamp() - start);
                }

                start = Stopwatch.GetTimestamp();
                _resolvedSamples.Add(new ResolvedTraceSample(
                    pending.Sequence,
                    pending.Sample,
                    pending.Ip,
                    pending.Address,
                    ipLocationId,
                    addressLocationId));
                PerfConverterMetrics.PipelineStageElapsed("enqueue.resolved", Stopwatch.GetTimestamp() - start);
                PerfConverterMetrics.PipelineQueueDepth("resolved", Interlocked.Increment(ref _resolvedDepth), _capacity);
            }
        }
        catch (Exception ex)
        {
            CaptureException(ex);
            _pendingSamples.CompleteAdding();
            _resolvedSamples.CompleteAdding();
        }
    }

    void CommitLoop()
    {
        var nextSequence = 0UL;
        var pendingBySequence = new SortedDictionary<ulong, ResolvedTraceSample>();

        try
        {
            foreach (var resolved in _resolvedSamples.GetConsumingEnumerable())
            {
                PerfConverterMetrics.PipelineQueueDepth("resolved", Interlocked.Decrement(ref _resolvedDepth), _capacity);
                pendingBySequence.Add(resolved.Sequence, resolved);

                while (pendingBySequence.Remove(nextSequence, out var ready))
                {
                    var start = Stopwatch.GetTimestamp();
                    _traceProcessor.ProcessData(
                        ready.Sample,
                        ready.Ip,
                        ready.Address,
                        ready.IpLocationId,
                        ready.AddressLocationId,
                        null,
                        0);
                    PerfConverterMetrics.PipelineStageElapsed("commit.trace", Stopwatch.GetTimestamp() - start);
                    nextSequence++;
                }
            }
        }
        catch (Exception ex)
        {
            CaptureException(ex);
            _pendingSamples.CompleteAdding();
            _resolvedSamples.CompleteAdding();
        }
    }

    void CaptureException(Exception exception)
    {
        lock (_exceptionGate)
            _exception ??= exception;
    }

    void ThrowIfFaulted()
    {
        lock (_exceptionGate)
        {
            if (_exception is not null)
                throw new InvalidOperationException("Trace processing pipeline failed.", _exception);
        }
    }

    static int GetPositiveEnvironmentValue(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : defaultValue;
    }

    readonly record struct PendingTraceSample(
        ulong Sequence,
        OwnedPerfSample Sample,
        ResolvedLocation Ip,
        ResolvedLocation? Address);

    readonly record struct ResolvedTraceSample(
        ulong Sequence,
        OwnedPerfSample Sample,
        ResolvedLocation Ip,
        ResolvedLocation? Address,
        ulong IpLocationId,
        ulong AddressLocationId);
}
