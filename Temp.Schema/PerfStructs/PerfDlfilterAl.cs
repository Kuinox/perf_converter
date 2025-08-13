using System.Runtime.InteropServices;

namespace PerfConverter.PerfStructs;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfDlfilterAl
{
    public uint size;
    public uint symoff;
    public nint sym;
    public ulong addr;
    public ulong sym_start;
    public ulong sym_end;
    public nint dso;
    public byte sym_binding;
    public byte is_64_bit;
    public byte is_kernel_ip;
    public int buildid_size;
    public void* buildid;
    public byte filtered;
    public nint comm;
    public nint priv;
}
