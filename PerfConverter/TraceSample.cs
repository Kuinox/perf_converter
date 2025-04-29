using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public struct TraceSample
{
    public ulong Id;
    public int Pid;
    public int Tid;
    public ulong Time;
    public int Cpu;
    public long Ip;
    public long Addr;
    public ulong Period;
    public ulong InsnCnt;
    public ulong CycCnt;
    public ulong Weight;
    public byte Cpumode;
    public byte AddrCorrelatesSym;
    public string? Event;
    public int MachinePid;
    public int Vcpu;
}
