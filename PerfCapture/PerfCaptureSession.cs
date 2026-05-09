using System.Diagnostics;

namespace PerfCapture;

public sealed class PerfCaptureSession : IAsyncDisposable
{
    readonly Process _process;
    readonly PerfCommandRunner _commandRunner;
    readonly Task<string> _standardOutputTask;
    readonly Task<string> _standardErrorTask;
    bool _stopRequested;

    internal PerfCaptureSession(
        Process process,
        PerfCapturePlan plan,
        PerfCommandRunner commandRunner)
    {
        _process = process;
        Plan = plan;
        _commandRunner = commandRunner;
        _standardOutputTask = process.StandardOutput.ReadToEndAsync();
        _standardErrorTask = process.StandardError.ReadToEndAsync();
    }

    public PerfCapturePlan Plan { get; }

    public int ProcessId => _process.Id;

    public bool HasExited => _process.HasExited;

    public Task EnableAsync(CancellationToken cancellationToken = default)
        => SendControlCommandAsync("enable", cancellationToken);

    public Task EnableAsync(string eventName, CancellationToken cancellationToken = default)
        => SendControlCommandAsync($"enable {ValidateEventName(eventName)}", cancellationToken);

    public Task DisableAsync(CancellationToken cancellationToken = default)
        => SendControlCommandAsync("disable", cancellationToken);

    public Task DisableAsync(string eventName, CancellationToken cancellationToken = default)
        => SendControlCommandAsync($"disable {ValidateEventName(eventName)}", cancellationToken);

    public Task SnapshotAsync(CancellationToken cancellationToken = default)
        => SendControlCommandAsync("snapshot", cancellationToken);

    public Task PingAsync(CancellationToken cancellationToken = default)
        => SendControlCommandAsync("ping", cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stopRequested = true;
        await SendControlCommandAsync("stop", cancellationToken).ConfigureAwait(false);
    }

    public async Task<PerfCaptureRunResult> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var recordResult = new PerfCommandResult
        {
            Command = Plan.RecordCommand,
            ExitCode = GetExitCode(),
            StandardOutput = await _standardOutputTask.ConfigureAwait(false),
            StandardError = await _standardErrorTask.ConfigureAwait(false)
        };
        var postProcessingResults = new List<PerfCommandResult>();

        if (recordResult.Succeeded)
        {
            foreach (var command in Plan.PostProcessingCommands)
            {
                var result = await _commandRunner.RunAsync(command, cancellationToken).ConfigureAwait(false);
                postProcessingResults.Add(result);
                if (!result.Succeeded)
                    break;
            }
        }

        return new PerfCaptureRunResult
        {
            Plan = Plan,
            RecordResult = recordResult,
            PostProcessingResults = postProcessingResults
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await _process.WaitForExitAsync().ConfigureAwait(false);
        }

        _process.Dispose();
    }

    async Task SendControlCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (_process.HasExited)
            throw new InvalidOperationException("Cannot send perf control command because perf has exited.");

        await _process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    int GetExitCode()
    {
        const int sigTermExitCode = 143;
        if (_stopRequested && _process.ExitCode == sigTermExitCode)
            return 0;

        return _process.ExitCode;
    }

    static string ValidateEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Perf control event name must not be empty.", nameof(eventName));
        if (eventName.Contains('\n') || eventName.Contains('\r'))
            throw new ArgumentException("Perf control event name must not contain line breaks.", nameof(eventName));

        return eventName;
    }
}
