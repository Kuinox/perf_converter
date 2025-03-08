using System.Runtime.InteropServices;

namespace PerfConverter;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfDlFilterSample
{
    public uint size;
    public ushort ins_lat;
    public ushort p_stage_cyc;
    public ulong ip;
    public int pid;
    public int tid;
    public ulong time;
    public void* addr;
    public ulong id;
    public ulong stream_id;
    public ulong period;
    public ulong weight;
    public ulong transaction;
    public ulong insn_cnt;
    public ulong cyc_cnt;
    public int cpu;
    public uint flags;
    public ulong data_src;
    public ulong phys_addr;
    public ulong data_page_size;
    public ulong code_page_size;
    public ulong cgroup;
    public byte cpumode;
    public byte addr_correlates_sym;
    public ushort misc;
    public uint raw_size;
    public IntPtr raw_data;
    public ulong brstack_nr;
    public IntPtr brstack;
    public ulong raw_callchain_nr;
    public IntPtr raw_callchain;
    public IntPtr @event;
    public int machine_pid;
    public int vcpu;
}
