using System.Runtime.InteropServices;

namespace PerfConverter.Entry;

[StructLayout(LayoutKind.Sequential)]
public struct TraceSampleEntry
{
    public ulong Id;
    public ulong PerfId;
    public int Pid;
    public int Tid;
    public ulong Time;
    public int Cpu;
    public uint Flags;
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
