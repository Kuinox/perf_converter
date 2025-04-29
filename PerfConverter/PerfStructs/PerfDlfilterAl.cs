using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfDlfilterAl
{
    public uint size;
    public uint symoff;
    public IntPtr sym;
    public ulong addr;
    public ulong sym_start;
    public ulong sym_end;
    public IntPtr dso;
    public byte sym_binding;
    public byte is_64_bit;
    public byte is_kernel_ip;
    public int buildid_size;
    public void* buildid;
    public byte filtered;
    public IntPtr comm;
    public IntPtr priv;
}
