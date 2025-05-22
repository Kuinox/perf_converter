using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;

namespace PerfConverter.Entry;

[StructLayout(LayoutKind.Sequential)]
public struct TraceSampleEntry
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
    public string? Event;
    public uint MachinePid;
    public uint Vcpu;
}
