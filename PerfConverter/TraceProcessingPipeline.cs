using System.Threading.Channels;
using PerfConverter.Persistence;
using PerfConverter.Persistence.Plank;

namespace PerfConverter;

public sealed class TraceProcessingPipeline : IDisposable
{
    const int DefaultCapacity = 512;

    readonly ParquetPersistenceLifetime _persistenceLifetime;
    readonly TraceProcessor _traceProcessor;
    readonly Channel<PendingTraceSample> _pendingSamples;
    readonly Channel<ResolvedTraceSample> _resolvedSamples;
    readonly Task[] _resolverTasks;
    readonly Task _commitTask;
    readonly object _exceptionGate = new();
    Exception? _exception;
    ulong _nextSequence;
    bool _disposed;

    TraceProcessingPipeline(ParquetPersistenceLifetime persistenceLifetime, int capacity, int resolverCount)
    {
        _persistenceLifetime = persistenceLifetime;
        _traceProcessor = new TraceProcessor(_persistenceLifetime.CreateTraceBatcher);
        _pendingSamples = Channel.CreateBounded<PendingTraceSample>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });
        _resolvedSamples = Channel.CreateBounded<ResolvedTraceSample>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        _resolverTasks = new Task[resolverCount];
        for (var i = 0; i < _resolverTasks.Length; i++)
            _resolverTasks[i] = Task.Run(ResolveLoop);

        _commitTask = Task.Run(CommitLoop);
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

        _pendingSamples.Writer.WriteAsync(pending).AsTask().GetAwaiter().GetResult();
        ThrowIfFaulted();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _pendingSamples.Writer.TryComplete();

        try
        {
            Task.WaitAll(_resolverTasks);
            _resolvedSamples.Writer.TryComplete();
            _commitTask.GetAwaiter().GetResult();
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

    async Task ResolveLoop()
    {
        try
        {
            await foreach (var pending in _pendingSamples.Reader.ReadAllAsync())
            {
                var ipLocationId = _persistenceLifetime.GetOrAddSourceLocation(pending.Ip);
                var addressLocationId = pending.Address is null
                    ? 0
                    : _persistenceLifetime.GetOrAddSourceLocation(pending.Address);

                await _resolvedSamples.Writer.WriteAsync(new ResolvedTraceSample(
                    pending.Sequence,
                    pending.Sample,
                    pending.Ip,
                    pending.Address,
                    ipLocationId,
                    addressLocationId));
            }
        }
        catch (Exception ex)
        {
            CaptureException(ex);
            _pendingSamples.Writer.TryComplete(ex);
            _resolvedSamples.Writer.TryComplete(ex);
        }
    }

    async Task CommitLoop()
    {
        var nextSequence = 0UL;
        var pendingBySequence = new SortedDictionary<ulong, ResolvedTraceSample>();

        try
        {
            await foreach (var resolved in _resolvedSamples.Reader.ReadAllAsync())
            {
                pendingBySequence.Add(resolved.Sequence, resolved);

                while (pendingBySequence.Remove(nextSequence, out var ready))
                {
                    _traceProcessor.ProcessData(
                        ready.Sample,
                        ready.Ip,
                        ready.Address,
                        ready.IpLocationId,
                        ready.AddressLocationId,
                        null,
                        0);
                    nextSequence++;
                }
            }
        }
        catch (Exception ex)
        {
            CaptureException(ex);
            _pendingSamples.Writer.TryComplete(ex);
            _resolvedSamples.Writer.TryComplete(ex);
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
