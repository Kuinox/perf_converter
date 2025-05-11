using System.Runtime.InteropServices;

namespace PerfConverter.PerfStructs;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerfDlFilterSample
{
    public uint size;
    /// <summary>
    /// ins_lat: Instruction latency in core cycles. This is the global instruction latency
    /// </summary>
    public ushort ins_lat;
    /// <summary>
    /// p_stage_cyc: On powerpc, this presents the number of cycles spent in a pipeline stage. And currently supported only on powerpc.
    /// </summary>
    [Obsolete("This field is not supported on x86")]
    public ushort p_stage_cyc;
    /// <summary>
    /// ip: Instruction pointer
    /// </summary>
    public void* ip;
    /// <summary>
    /// Process ID
    /// </summary>
    public int pid;
    /// <summary>
    /// Thread ID
    /// </summary>
    public int tid;
    /// <summary>
    /// Time, depends on clock used in the perf args
    public ulong time;
    /// <summary>
    /// (Full) virtual address of the sampled instruction
    /// </summary>
    public void* addr;

    public ulong id;
    public ulong stream_id;

    /// <summary>
    ///  Raw number of event count of sample
    /// </summary>
    public ulong period;

    public ulong weight;

    public ulong transaction;
    /// <summary>
    /// For instructions-per-cycle (IPC)
    /// </summary>
    public ulong insn_cnt;
    /// <summary>
    /// For instructions-per-cycle (IPC)
    /// </summary>
    public ulong cyc_cnt;
    /// <summary>
    /// CPU number the task was running on
    /// </summary>
    public int cpu;
    public uint flags;

    public ulong data_src;
    public ulong phys_addr;
    public ulong data_page_size;
    public ulong code_page_size;
    public ulong cgroup;
    public byte cpumode;
    /// <summary>
    /// True => resolve_addr() can be called
    /// </summary>
    public byte addr_correlates_sym;
    /// <summary>
    /// !!! UNSURE !!!
    /// 
    /// The current state of perf_event_header::misc bits usage:
    /// ('|' used bit, '-' unused bit)
    /// 
    ///  012         CDEF
    ///  |||---------||||
    /// 
    ///  Where:
    ///    0-2     CPUMODE_MASK
    /// 
    /// C       PROC_MAP_PARSE_TIMEOUT
    /// D       MMAP_DATA / COMM_EXEC / FORK_EXEC / SWITCH_OUT
    /// E       MMAP_BUILD_ID / EXACT_IP / SCHED_OUT_PREEMPT
    /// F(reserved)
    /// </summary>
    public ushort misc;
    /// <summary>
    /// If PERF_SAMPLE_RAW is enabled, then a 32-bit value
    /// indicating size is included followed by an array of
    /// 8-bit values of size size.The values are padded
    /// with 0 to have 64-bit alignment.
    /// 
    /// This RAW record data is opaque with respect to the
    /// ABI.  The ABI doesn't make any promises with
    /// respect to the stability of its content, it may
    /// vary depending on event, hardware, and kernel
    /// version.
    /// </summary>
    public uint raw_size;
    public nint raw_data;

    /// <summary>
    /// Number of brstack entries
    /// </summary>
    public ulong brstack_nr;
    /// <summary>
    /// If PERF_SAMPLE_BRANCH_STACK is enabled, then a
    /// 64-bit value indicating the number of records is
    /// included, followed by bnr perf_branch_entry
    /// structures which each include the fields:
    ///  <br/>
    /// from This indicates the source instruction(may
    ///         not be a branch).
    ///  <br/>
    /// 
    /// to The branch target.
    ///  <br/>
    /// 
    /// mispred
    ///         The branch target was mispredicted.
    ///  <br/>
    ///         
    /// predicted
    ///         The branch target was predicted.
    ///  <br/>
    /// 
    /// in_tx (since Linux 3.11)
    ///         The branch was in a transactional memory
    ///  <br/>
    ///         
    /// transaction.
    ///  <br/>
    /// 
    /// abort(since Linux 3.11)
    ///         The branch was in an aborted transactional
    ///         memory transaction.
    ///  <br/>
    /// 
    /// cycles(since Linux 4.3)
    ///         This reports the number of cycles elapsed
    /// since the previous branch stack update.
    /// 
    ///  <br/>
    /// 
    /// 
    /// The entries are from most to least recent, so the
    /// first entry has the most recent branch.
    /// 
    /// Support for mispred, predicted, and cycles is
    /// optional; if not supported, those values will be 0.
    /// 
    /// The type of branches recorded is specified by the
    /// branch_sample_type field.
    /// </summary>
    public nint brstack;
    /// <summary>
    ///  Number of raw_callchain entries
    /// </summary>
    public ulong raw_callchain_nr;
    public nint raw_callchain;
    /// <summary>
    /// Event name
    /// </summary>
    public nint @event;
    public int machine_pid;
    public int vcpu;
}
