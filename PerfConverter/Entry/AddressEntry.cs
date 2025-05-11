namespace PerfConverter.Entry;

public struct AddressEntry
{
    public ulong Id;
    public ulong TraceId;
    public ulong Address;
    public uint Pid;
    public bool IsIp;
    public uint Size;
    public uint Symoff;
    public ulong SymStrId;
    public ulong SymStart;
    public ulong SymEnd;
    public ulong Dso;
    public byte SymBinding;
    public byte Is64Bit;
    public byte IsKernelIp;
    public byte[] BuildId;
    public byte Filtered;
    public ulong Comm;
    public ulong Priv;
}
