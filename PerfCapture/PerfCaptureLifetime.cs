namespace PerfCapture;

public abstract record PerfCaptureLifetime
{
    public static TargetExitLifetime UntilTargetExit() => new TargetExitLifetime();

    public static DurationLifetime ForDuration(PositiveDuration duration) => new DurationLifetime(duration);

    public static SignalLifetime UntilSignal(PerfSignal signal) => new SignalLifetime(signal);

    public static ManualStopLifetime UntilStopped() => new ManualStopLifetime();
}
