using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace CLI
{
    public class GcEventListener : IDisposable
    {
        private readonly DiagnosticsClient _client;
        private readonly EventPipeSession? _session;
        private readonly Task _eventTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly PerfMonitorViewModel _viewModel;
        private bool _disposed;
        private DateTime _gcStartTime;

        public GcEventListener(int processId, PerfMonitorViewModel viewModel)
        {
            _viewModel = viewModel;
            _client = new DiagnosticsClient(processId);
            _cancellationTokenSource = new CancellationTokenSource();

            var providers = new List<EventPipeProvider>
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime",
                    EventLevel.Informational,
                    (long)ClrTraceEventParser.Keywords.GC)
            };

            _session = _client.StartEventPipeSession(providers, false);
            _eventTask = Task.Run(ProcessEvents, _cancellationTokenSource.Token);
        }

        private void ProcessEvents()
        {
            if (_session == null) return;

            try
            {
                using var source = new EventPipeEventSource(_session.EventStream);

                source.Clr.Observe<GCStartTraceData>().Subscribe(gcData =>
                {
                    // Only track blocking GCs (Type 1 = blocking, Type 0 = concurrent)
                    if (gcData.Type == GCType.NonConcurrentGC)
                    {
                        _viewModel.GcActive = true;
                        _gcStartTime = DateTime.UtcNow;
                    }
                });

                source.Clr.Observe<GCEndTraceData>().Subscribe(gcData =>
                {
                    _viewModel.GcActive = false;
                    _viewModel.LastGcEvent = gcData.TimeStamp;

                    // Calculate GC duration and add to total
                    if (_gcStartTime != default)
                    {
                        var gcDuration = DateTime.UtcNow - _gcStartTime;
                        _viewModel.TotalGcTimeMs += gcDuration.TotalMilliseconds;
                    }

                    // Update generation counts based on the GC that just completed
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
                    // Update total memory with the total heap size
                    _viewModel.TotalMemory = (long)heapData.TotalHeapSize;
                });

                source.Process();
            }
            catch (Exception ex) when (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                Console.WriteLine($"Error processing GC events: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cancellationTokenSource.Cancel();
            _session?.Dispose();
            _cancellationTokenSource.Dispose();

            try
            {
                _eventTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions during shutdown
            }

            _disposed = true;
        }
    }
}