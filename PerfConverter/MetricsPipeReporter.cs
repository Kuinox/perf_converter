using System.IO.Pipes;
using System.Globalization;
using System.Text;

namespace PerfConverter;

sealed class MetricsPipeReporter : IDisposable
{
    const string PipeNameEnvironmentVariable = "PERFCONVERTER_METRICS_PIPE";

    readonly CancellationTokenSource _cancellationTokenSource = new();
    readonly Task _backgroundTask;

    MetricsPipeReporter(string pipeName)
    {
        _backgroundTask = Task.Run(() => RunAsync(pipeName, _cancellationTokenSource.Token));
    }

    public static MetricsPipeReporter? TryStart()
    {
        var pipeName = Environment.GetEnvironmentVariable(PipeNameEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(pipeName))
            return null;

        return new MetricsPipeReporter(pipeName);
    }

    async Task RunAsync(string pipeName, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000, cancellationToken);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

            await writer.WriteLineAsync(SerializeSnapshot(PerfConverterMetrics.GetSnapshot()));

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await writer.WriteLineAsync(SerializeSnapshot(PerfConverterMetrics.GetSnapshot()));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Metrics pipe reporter failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        try
        {
            _backgroundTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        _cancellationTokenSource.Dispose();
    }

    static string SerializeSnapshot(PerfConverterMetrics.MetricsSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append(snapshot.TotalEvents.ToString(CultureInfo.InvariantCulture));
        builder.Append('|');
        builder.Append(snapshot.CurrentRate.ToString("F3", CultureInfo.InvariantCulture));
        builder.Append('|');

        for (var i = 0; i < snapshot.Files.Length; i++)
        {
            var file = snapshot.Files[i];
            if (i > 0)
            {
                builder.Append(';');
            }

            builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(file.FileName)));
            builder.Append(',');
            builder.Append(file.BufferedEntries.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(file.FlushedEntries.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(file.Status);
        }

        return builder.ToString();
    }
}
