using System.Diagnostics;
using System.IO.Pipes;
using System.Globalization;
using System.Text;

namespace CLI;

sealed class PerfMetricsPipeServer : IDisposable
{
    const string PipeNameEnvironmentVariable = "PERFCONVERTER_METRICS_PIPE";

    readonly NamedPipeServerStream _server;
    readonly CancellationTokenSource _cancellationTokenSource = new();
    readonly Task _backgroundTask;

    PerfMetricsPipeServer(string pipeName, PerfMonitorViewModel viewModel)
    {
        _server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _backgroundTask = Task.Run(() => RunAsync(viewModel, _cancellationTokenSource.Token));
    }

    public static PerfMetricsPipeServer Create(ProcessStartInfo processInfo, PerfMonitorViewModel viewModel)
    {
        var pipeName = $"perfconverter-{Guid.NewGuid():N}";
        processInfo.Environment[PipeNameEnvironmentVariable] = pipeName;
        return new PerfMetricsPipeServer(pipeName, viewModel);
    }

    async Task RunAsync(PerfMonitorViewModel viewModel, CancellationToken cancellationToken)
    {
        try
        {
            await _server.WaitForConnectionAsync(cancellationToken);
            using var reader = new StreamReader(_server);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                var snapshot = ParseSnapshot(line);
                if (snapshot is null)
                    continue;

                viewModel.UpdateEventCount(snapshot.TotalEvents, DateTime.UtcNow);
                viewModel.CurrentRate = (int)Math.Round(snapshot.CurrentRate);
                viewModel.LastCurrentRateUpdateUtc = DateTime.UtcNow;
                viewModel.RecordTotalRate(snapshot.CurrentRate);
                viewModel.FirstTraceTimestampNs = snapshot.FirstTraceTimestampNs;
                viewModel.LastTraceTimestampNs = snapshot.LastTraceTimestampNs;

                var observedAtUtc = DateTime.UtcNow;
                viewModel.UpdatePipelineMetrics(snapshot.PipelineStages, snapshot.PipelineQueues, observedAtUtc);

                foreach (var file in snapshot.Files)
                {
                    viewModel.FileStatuses.AddOrUpdate(
                        file.FileName,
                        key => new FileStatus
                        {
                            FileName = key,
                            Status = file.Status,
                            BufferedCount = file.BufferedEntries,
                            ClosedAt = file.Status == "CLOSED" ? DateTime.UtcNow : null,
                            LastActivity = DateTime.UtcNow,
                            LastUpdated = DateTime.UtcNow
                        },
                        (_, existing) =>
                        {
                            existing.Status = file.Status;
                            existing.BufferedCount = file.BufferedEntries;
                            existing.LastActivity = DateTime.UtcNow;
                            existing.LastUpdated = DateTime.UtcNow;
                            if (file.Status == "CLOSED")
                            {
                                existing.ClosedAt = DateTime.UtcNow;
                            }

                            return existing;
                        });

                    if (viewModel.FileStatuses.TryGetValue(file.FileName, out var fileStatus))
                    {
                        fileStatus.RecordFlushedCount(file.FlushedEntries, observedAtUtc);
                        if (file.Status == "CLOSED")
                        {
                            fileStatus.ResetCurrentRate();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _server.Dispose();
        try
        {
            _backgroundTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        _cancellationTokenSource.Dispose();
    }

    static PerfMetricsSnapshot? ParseSnapshot(string line)
    {
        var topLevelParts = line.Split('|', 7);
        if (topLevelParts.Length < 2)
            return null;

        if (!long.TryParse(topLevelParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalEvents))
            return null;

        if (!double.TryParse(topLevelParts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var currentRate))
            return null;

        long? firstTraceTimestampNs = null;
        long? lastTraceTimestampNs = null;
        var filesPart = string.Empty;
        var pipelineStagesPart = string.Empty;
        var pipelineQueuesPart = string.Empty;

        if (topLevelParts.Length >= 5)
        {
            if (long.TryParse(topLevelParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var firstTimestamp))
            {
                firstTraceTimestampNs = firstTimestamp;
            }

            if (long.TryParse(topLevelParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastTimestamp))
            {
                lastTraceTimestampNs = lastTimestamp;
            }

            filesPart = topLevelParts[4];
            if (topLevelParts.Length >= 6)
            {
                pipelineStagesPart = topLevelParts[5];
            }

            if (topLevelParts.Length >= 7)
            {
                pipelineQueuesPart = topLevelParts[6];
            }
        }
        else if (topLevelParts.Length >= 3)
        {
            filesPart = topLevelParts[2];
        }

        var files = Array.Empty<FileMetricsSnapshot>();
        if (!string.IsNullOrEmpty(filesPart))
        {
            var fileParts = filesPart.Split(';', StringSplitOptions.RemoveEmptyEntries);
            files = new FileMetricsSnapshot[fileParts.Length];
            var count = 0;

            foreach (var filePart in fileParts)
            {
                var fields = filePart.Split(',', 4);
                if (fields.Length != 4)
                    continue;

                var fileName = Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
                if (!long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bufferedEntries))
                    continue;
                if (!long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flushedEntries))
                    continue;

                files[count++] = new FileMetricsSnapshot(fileName, bufferedEntries, flushedEntries, fields[3]);
            }

            if (count != files.Length)
            {
                Array.Resize(ref files, count);
            }
        }

        return new PerfMetricsSnapshot(
            totalEvents,
            currentRate,
            firstTraceTimestampNs,
            lastTraceTimestampNs,
            files,
            ParsePipelineStages(pipelineStagesPart),
            ParsePipelineQueues(pipelineQueuesPart));
    }

    static PerfMonitorViewModel.PipelineStageMetricsSnapshot[] ParsePipelineStages(string value)
    {
        if (string.IsNullOrEmpty(value))
            return [];

        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var stages = new PerfMonitorViewModel.PipelineStageMetricsSnapshot[parts.Length];
        var count = 0;

        foreach (var part in parts)
        {
            var fields = part.Split(',', 3);
            if (fields.Length != 3)
                continue;

            var stage = Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
            if (!long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stageCount))
                continue;
            if (!double.TryParse(fields[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var elapsedMs))
                continue;

            stages[count++] = new PerfMonitorViewModel.PipelineStageMetricsSnapshot(stage, stageCount, elapsedMs);
        }

        if (count != stages.Length)
        {
            Array.Resize(ref stages, count);
        }

        return stages;
    }

    static PerfMonitorViewModel.PipelineQueueMetricsSnapshot[] ParsePipelineQueues(string value)
    {
        if (string.IsNullOrEmpty(value))
            return [];

        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var queues = new PerfMonitorViewModel.PipelineQueueMetricsSnapshot[parts.Length];
        var count = 0;

        foreach (var part in parts)
        {
            var fields = part.Split(',', 3);
            if (fields.Length != 3)
                continue;

            var queue = Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth))
                continue;
            if (!int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var capacity))
                continue;

            queues[count++] = new PerfMonitorViewModel.PipelineQueueMetricsSnapshot(queue, depth, capacity);
        }

        if (count != queues.Length)
        {
            Array.Resize(ref queues, count);
        }

        return queues;
    }

    sealed record PerfMetricsSnapshot(
        long TotalEvents,
        double CurrentRate,
        long? FirstTraceTimestampNs,
        long? LastTraceTimestampNs,
        FileMetricsSnapshot[] Files,
        PerfMonitorViewModel.PipelineStageMetricsSnapshot[] PipelineStages,
        PerfMonitorViewModel.PipelineQueueMetricsSnapshot[] PipelineQueues);
    sealed record FileMetricsSnapshot(string FileName, long BufferedEntries, long FlushedEntries, string Status);
}
