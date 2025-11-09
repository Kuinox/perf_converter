namespace PerfConverter;

/// <summary>
/// Configuration settings for PerfConverter, loaded from environment variables
/// </summary>
public static class Configuration
{
    /// <summary>
    /// When true, enables verbose progress and file activity signals.
    /// Set ENABLE_PROGRESS_SIGNALS=true to enable. Default is false.
    /// </summary>
    public static readonly bool EnableProgressSignals =
        Environment.GetEnvironmentVariable("ENABLE_PROGRESS_SIGNALS")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
}
