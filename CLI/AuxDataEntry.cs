namespace CLI;

public readonly record struct AuxDataEntry(
    ulong Time,
    uint Pid,
    uint Tid,
    uint Cpu,
    ulong Flags);
