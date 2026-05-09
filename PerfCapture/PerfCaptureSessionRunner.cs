using System.Diagnostics;

namespace PerfCapture;

public sealed class PerfCaptureSessionRunner
{
    readonly PerfCommandRunner _commandRunner;

    public PerfCaptureSessionRunner()
        : this(new PerfCommandRunner())
    {
    }

    public PerfCaptureSessionRunner(PerfCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public Task<PerfCaptureSession> StartAsync(
        PerfCaptureSpec spec,
        bool startDisabled = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Control is not null && !IsStandardInputControl(spec.Control))
            throw new NotSupportedException("PerfCaptureSessionRunner supports only the standard-input perf control channel.");

        var sessionSpec = spec with
        {
            StartDisabled = startDisabled,
            Control = PerfControlChannel.StandardInput()
        };
        var plan = PerfCapturePlanBuilder.Build(sessionSpec);
        var process = new Process
        {
            StartInfo = PerfCommandRunner.CreateStartInfo(plan.RecordCommand, redirectStandardInput: true)
        };

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();

        return Task.FromResult(new PerfCaptureSession(process, plan, _commandRunner));
    }

    static bool IsStandardInputControl(PerfControlChannel control)
    {
        return control is FileDescriptorPerfControlChannel
        {
            ControlFileDescriptor.Value: 0,
            AcknowledgementFileDescriptor: null
        };
    }
}
