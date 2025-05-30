using PerfConverter.PerfStructs;

namespace PostProcess;

public struct TraceWithStackEntry
{
    public ulong Id;
    public ulong PerfId;
    public uint Pid;
    public uint Tid;
    public ulong Time;
    public uint Cpu;
    public DLFilterFlag Flags;
    public ulong Ip;
    public ulong Addr;
    public ulong Period;
    public ulong InsnCnt;
    public ulong CycCnt;
    public ulong Weight;
    public byte Cpumode;
    public byte AddrCorrelatesSym;
    public ulong EventId;
    public uint MachinePid;
    public uint Vcpu;
    public int SegmentId;
    public PooledStack<ulong>.Snapshot? StackSnapshot;
}
