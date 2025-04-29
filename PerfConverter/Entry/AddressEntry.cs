namespace PerfConverter.Entry;

public struct AddressEntry
{
    public ulong Id;
    public long TraceId;
    public ulong Address;
    public int Pid;
    public bool IsIp;
    public int Size;
    public int Symoff;
    public long SymStrId;
    public ulong SymStart;
    public ulong SymEnd;
    public long Dso;
    public byte SymBinding;
    public byte Is64Bit;
    public byte IsKernelIp;
    public byte[] BuildId;
    public byte Filtered;
    public long Comm;
    public long Priv;
}
