using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace CLI;

public sealed class PerfDiagnosticsListener : IDisposable
{
    readonly EventPipeSession? _session;
    readonly Task _eventTask;
    readonly CancellationTokenSource _cancellationTokenSource;
    readonly PerfMonitorViewModel _viewModel;

    bool _disposed;
    DateTime _gcStartTime;

    public PerfDiagnosticsListener(int processId, PerfMonitorViewModel viewModel)
    {
        _viewModel = viewModel;
        _cancellationTokenSource = new CancellationTokenSource();

        var client = new DiagnosticsClient(processId);
        var providers = new List<EventPipeProvider>
        {
            new(
                "Microsoft-Windows-DotNETRuntime",
                EventLevel.Informational,
                (long)ClrTraceEventParser.Keywords.GC),
            new(
                "System.Diagnostics.Metrics",
                EventLevel.Informational,
                keywords: 0x3,
                arguments: new Dictionary<string, string?>
                {
                    ["Metrics"] = "PerfConverter*",
                    ["RefreshInterval"] = "1"
                })
        };

        _session = client.StartEventPipeSession(providers, requestRundown: false);
        _eventTask = Task.Run(ProcessEvents, _cancellationTokenSource.Token);
    }

    void ProcessEvents()
    {
        if (_session == null)
            return;

        try
        {
            using var source = new EventPipeEventSource(_session.EventStream);

            source.Clr.Observe<GCStartTraceData>().Subscribe(gcData =>
            {
                if (gcData.Type == GCType.NonConcurrentGC)
                {
                    _viewModel.GcActive = true;
                    _gcStartTime = DateTime.UtcNow;
                }
                else
                {
                    _gcStartTime = default;
                }
            });

            source.Clr.Observe<GCEndTraceData>().Subscribe(gcData =>
            {
                _viewModel.GcActive = false;
                _viewModel.LastGcEvent = gcData.TimeStamp;

                if (_gcStartTime != default)
                {
                    var gcDuration = DateTime.UtcNow - _gcStartTime;
                    _viewModel.TotalGcTimeMs += gcDuration.TotalMilliseconds;
                }

                switch (gcData.Depth)
                {
                    case 0:
                        _viewModel.Gen0Count++;
                        break;
                    case 1:
                        _viewModel.Gen1Count++;
                        break;
                    case 2:
                        _viewModel.Gen2Count++;
                        break;
                }
            });

            source.Clr.Observe<GCHeapStatsTraceData>().Subscribe(heapData =>
            {
                _viewModel.TotalMemory = (long)heapData.TotalHeapSize;
            });

            source.Dynamic.All += OnDynamicEvent;
            source.Process();
        }
        catch (EndOfStreamException)
        {
            // Expected when the target process exits.
        }
        catch (Exception ex) when (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            Console.WriteLine($"Error processing diagnostics events: {ex.Message}");
        }
    }

    void OnDynamicEvent(TraceEvent traceEvent)
    {
        if (traceEvent.ProviderName != "System.Diagnostics.Metrics")
            return;

        switch (traceEvent.EventName)
        {
            case "CounterRateValuePublished":
                HandleCounterMetric(traceEvent);
                break;
            case "GaugeValuePublished":
                HandleGaugeMetric(traceEvent);
                break;
        }
    }

    void HandleCounterMetric(TraceEvent traceEvent)
    {
        var instrumentName = GetPayloadString(traceEvent, "instrumentName");
        if (string.IsNullOrEmpty(instrumentName))
            return;

        if (instrumentName == "perfconverter.events.processed")
        {
            var valueText = GetPayloadString(traceEvent, "value");
            if (long.TryParse(valueText, out var eventCount))
            {
                _viewModel.EventCount = eventCount;
            }
            return;
        }

        if (instrumentName != "perfconverter.file.entries.flushed")
            return;

        var fileName = GetTagValue(GetPayloadString(traceEvent, "tags"), "file");
        var value = GetPayloadLong(traceEvent, "value");
        if (string.IsNullOrEmpty(fileName) || value is null)
            return;

        _viewModel.FileStatuses.AddOrUpdate(
            fileName,
            key => new FileStatus
            {
                FileName = key,
                Status = "BUFFERING",
                FlushedCount = value.Value,
                LastActivity = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.FlushedCount = value.Value;
                existing.LastActivity = DateTime.UtcNow;
                if (existing.Status != "CLOSED")
                {
                    existing.Status = "BUFFERING";
                }

                return existing;
            });
    }

    void HandleGaugeMetric(TraceEvent traceEvent)
    {
        var instrumentName = GetPayloadString(traceEvent, "instrumentName");
        if (instrumentName != "perfconverter.file.status")
            return;

        var fileName = GetTagValue(GetPayloadString(traceEvent, "tags"), "file");
        var statusCode = GetPayloadInt(traceEvent, "lastValue");
        if (string.IsNullOrEmpty(fileName) || statusCode is null)
            return;

        var status = statusCode.Value switch
        {
            2 => "CLOSED",
            _ => "BUFFERING"
        };

        _viewModel.FileStatuses.AddOrUpdate(
            fileName,
            key => new FileStatus
            {
                FileName = key,
                Status = status,
                ClosedAt = status == "CLOSED" ? DateTime.UtcNow : null
            },
            (_, existing) =>
            {
                existing.Status = status;
                existing.LastUpdated = DateTime.UtcNow;
                if (status == "CLOSED")
                {
                    existing.ClosedAt = DateTime.UtcNow;
                }

                return existing;
            });
    }

    static string? GetPayloadString(TraceEvent traceEvent, string payloadName)
    {
        if (traceEvent.PayloadNames == null)
            return null;

        for (var i = 0; i < traceEvent.PayloadNames.Length; i++)
        {
            if (traceEvent.PayloadNames[i] == payloadName)
            {
                return traceEvent.PayloadValue(i)?.ToString();
            }
        }

        return null;
    }

    static long? GetPayloadLong(TraceEvent traceEvent, string payloadName)
    {
        var valueText = GetPayloadString(traceEvent, payloadName);
        return long.TryParse(valueText, out var value) ? value : null;
    }

    static int? GetPayloadInt(TraceEvent traceEvent, string payloadName)
    {
        var valueText = GetPayloadString(traceEvent, payloadName);
        return int.TryParse(valueText, out var value) ? value : null;
    }

    static string? GetTagValue(string? tags, string key)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return null;

        foreach (var part in tags.Split(','))
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            if (!part[..separatorIndex].Equals(key, StringComparison.Ordinal))
                continue;

            return part[(separatorIndex + 1)..];
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cancellationTokenSource.Cancel();
        _session?.Dispose();
        _cancellationTokenSource.Dispose();

        try
        {
            _eventTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Ignore cancellation exceptions during shutdown.
        }

        _disposed = true;
    }
}
