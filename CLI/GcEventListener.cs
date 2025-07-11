using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CLI.ViewModel;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;

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

            try
            {
                _session = _client.StartEventPipeSession(providers, false);
                _eventTask = Task.Run(ProcessEvents, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start EventPipe session: {ex.Message}");
                _session = null;
                _eventTask = Task.CompletedTask;
            }
        }

        private async Task ProcessEvents()
        {
            if (_session == null) return;

            try
            {
                using var source = new EventPipeEventSource(_session.EventStream);
                
                source.Clr.GCStart += (TraceEvent data) =>
                {
                    _viewModel.GcActive = true;
                };

                source.Clr.GCEnd += (TraceEvent data) =>
                {
                    var gcData = (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCEndTraceData)data;
                    
                    _viewModel.GcActive = false;
                    _viewModel.LastGcEvent = data.TimeStamp;
                    
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
                };

                source.Clr.GCHeapStats += (TraceEvent data) =>
                {
                    var heapData = (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCHeapStatsTraceData)data;
                    
                    // Update total memory with the total heap size
                    _viewModel.TotalMemory = (long)heapData.TotalHeapSize;
                };

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