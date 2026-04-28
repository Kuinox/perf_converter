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

                viewModel.EventCount = snapshot.TotalEvents;
                viewModel.CurrentRate = (int)Math.Round(snapshot.CurrentRate);
                viewModel.LastCurrentRateUpdateUtc = DateTime.UtcNow;

                foreach (var file in snapshot.Files)
                {
                    viewModel.FileStatuses.AddOrUpdate(
                        file.FileName,
                        key => new FileStatus
                        {
                            FileName = key,
                            Status = file.Status,
                            BufferedCount = file.BufferedEntries,
                            FlushedCount = file.FlushedEntries,
                            ClosedAt = file.Status == "CLOSED" ? DateTime.UtcNow : null,
                            LastActivity = DateTime.UtcNow,
                            LastUpdated = DateTime.UtcNow
                        },
                        (_, existing) =>
                        {
                            existing.Status = file.Status;
                            existing.BufferedCount = file.BufferedEntries;
                            existing.FlushedCount = file.FlushedEntries;
                            existing.LastActivity = DateTime.UtcNow;
                            existing.LastUpdated = DateTime.UtcNow;
                            if (file.Status == "CLOSED")
                            {
                                existing.ClosedAt = DateTime.UtcNow;
                            }

                            return existing;
                        });
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
        var topLevelParts = line.Split('|', 3);
        if (topLevelParts.Length < 2)
            return null;

        if (!long.TryParse(topLevelParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalEvents))
            return null;

        if (!double.TryParse(topLevelParts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var currentRate))
            return null;

        var files = Array.Empty<FileMetricsSnapshot>();
        if (topLevelParts.Length == 3 && !string.IsNullOrEmpty(topLevelParts[2]))
        {
            var fileParts = topLevelParts[2].Split(';', StringSplitOptions.RemoveEmptyEntries);
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

        return new PerfMetricsSnapshot(totalEvents, currentRate, files);
    }

    sealed record PerfMetricsSnapshot(long TotalEvents, double CurrentRate, FileMetricsSnapshot[] Files);
    sealed record FileMetricsSnapshot(string FileName, long BufferedEntries, long FlushedEntries, string Status);
}
