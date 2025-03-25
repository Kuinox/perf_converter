namespace PerfConverter.Fuchsia;

/// <summary>
/// Time units that can be used for trace events
/// </summary>
public enum TimestampMode
{
    Time,       // Wall clock time
    Cycles,     // CPU cycles
    Instructions // Instruction count
}