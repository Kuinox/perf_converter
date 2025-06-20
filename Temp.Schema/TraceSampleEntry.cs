using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;

namespace PerfConverter.Entry;

[StructLayout(LayoutKind.Sequential)]
public struct TraceSampleEntry
{
    public ulong Id;
    public ulong PerfId;
    public ushort InstructionLatency;
    public uint Pid;
    public uint Tid;
    public ulong Time;
    public uint Cpu;
    public DLFilterFlag Flags;
    public ulong Period;
    public ulong InsnCnt;
    public ulong CycCnt;
    public ulong Weight;
    public byte Cpumode;
    public byte AddrCorrelatesSym;
    public string? Event;
    public uint MachinePid;
    public uint Vcpu;

    // ip
    public ulong IpAddress;
    public uint IpSymoff;
    public string? IpSym;
    public ulong IpSymStart;
    public ulong IpSymEnd;
    public string? IpDso;
    public byte IpSymBinding;
    public byte IpIs64Bit;
    public byte IpIsKernelIp;
    public byte[] IpBuildId;
    public byte IpFiltered;
    public string? IpComm;

    // address
    public bool HaveAddress;
    public ulong AddressAddress;
    public uint AddressSymoff;
    public string? AddressSym;
    public ulong AddressSymStart;
    public ulong AddressSymEnd;
    public string? AddressDso;
    public byte AddressSymBinding;
    public byte AddressIs64Bit;
    public byte AddressIsKernelIp;
    public byte[] AddressBuildId;
    public byte AddressFiltered;
    public string? AddressComm;
}
