namespace PerfCapture;

public sealed class PerfCaptureRunner
{
    readonly PerfCommandRunner _commandRunner;

    public PerfCaptureRunner()
        : this(new PerfCommandRunner())
    {
    }

    public PerfCaptureRunner(PerfCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<PerfCaptureRunResult> RunAsync(
        PerfCaptureSpec spec,
        CancellationToken cancellationToken = default)
    {
        ThrowIfRunAsyncCannotComplete(spec);

        var plan = PerfCapturePlanBuilder.Build(spec);
        var recordResult = await _commandRunner.RunAsync(plan.RecordCommand, cancellationToken).ConfigureAwait(false);
        var postProcessingResults = new List<PerfCommandResult>();

        if (recordResult.Succeeded)
        {
            foreach (var command in plan.PostProcessingCommands)
            {
                var result = await _commandRunner.RunAsync(command, cancellationToken).ConfigureAwait(false);
                postProcessingResults.Add(result);
                if (!result.Succeeded)
                    break;
            }
        }

        return new PerfCaptureRunResult
        {
            Plan = plan,
            RecordResult = recordResult,
            PostProcessingResults = postProcessingResults
        };
    }

    static void ThrowIfRunAsyncCannotComplete(PerfCaptureSpec spec)
    {
        if (spec is AttachCaptureSpec { Lifetime: not DurationLifetime })
        {
            throw new NotSupportedException(
                "Attach captures require a DurationLifetime when using RunAsync. Use a session runner for manual or signal-controlled captures.");
        }
    }
}
