using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using Temp.Core;

namespace PerfConverter;

unsafe class ThreadProcessor(uint tid, uint pid, IEnumerable<ulong> auxDrop, Func<string, IPersister<TraceEntry>> persistenceFactory)
{
    readonly Queue<ulong> auxDrop = new(auxDrop.Order());
    int _currentSegmentId = 0;
    ulong _currentEntryId = 0;
    string _currentKey = null!;
    IPersister<TraceEntry> _persister = null!;

    public uint Tid { get; } = tid;
    public uint Pid { get; } = pid;


    public void QueueData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        var isNewTrace = auxDrop.TryPeek(out var newTraceTime) && newTraceTime < sample->time;
        if (isNewTrace)
        {
            _persister.DisposeAsync().AsTask();
            auxDrop.Dequeue();
            _currentSegmentId++;
            _currentKey = $"{sample->pid}/{sample->tid}/segment{_currentSegmentId}.parquet";
            _currentEntryId=0;
            _persister = persistenceFactory(_currentKey);
        }

        var entry = BuildEntry(sample, ip, address, ++_currentEntryId);
        _persister.Persist(entry);
    }

    private static TraceEntry BuildEntry(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address, ulong id)
    {
        var entry = new TraceEntry
        {
            Id = id,
            PerfId = sample->id,
            Pid = sample->pid,
            Tid = sample->tid,
            Time = sample->time,
            Cpu = (uint)sample->cpu,
            Flags = (DLFilterFlag)sample->flags,
            Period = sample->period,
            InsnCnt = sample->insn_cnt,
            CycCnt = sample->cyc_cnt,
            Weight = sample->weight,
            Cpumode = sample->cpumode,
            AddrCorrelatesSym = sample->addr_correlates_sym,
            Event = Marshal.PtrToStringUTF8(sample->@event),
            MachinePid = (uint)sample->machine_pid,
            Vcpu = (uint)sample->vcpu,

            IpAddress = ip->addr,
            IpSymoff = ip->symoff,
            IpSym = Marshal.PtrToStringUTF8(ip->sym),
            IpSymStart = ip->sym_start,
            IpSymEnd = ip->sym_end,
            IpDso = Marshal.PtrToStringUTF8(ip->dso),
            IpSymBinding = ip->sym_binding,
            IpIs64Bit = ip->is_64_bit,
            IpIsKernelIp = ip->is_kernel_ip,
            IpBuildId = new Span<byte>(ip->buildid, ip->buildid_size).ToArray(),
            IpFiltered = ip->filtered,
            IpComm = Marshal.PtrToStringUTF8(ip->comm)
        };

        if (address != null)
        {
            var addrSym = Marshal.PtrToStringUTF8(address->sym);
            var addrDso = Marshal.PtrToStringUTF8(address->dso);
            var addrBuildId = new Span<byte>(address->buildid, address->buildid_size).ToArray();
            var addrComm = Marshal.PtrToStringUTF8(address->comm);
            entry.HaveAddress = true;
            entry.AddressAddress = address->addr;
            entry.AddressSymoff = address->symoff;
            entry.AddressSym = addrSym;
            entry.AddressSymStart = address->sym_start;
            entry.AddressSymEnd = address->sym_end;
            entry.AddressDso = addrDso;
            entry.AddressSymBinding = address->sym_binding;
            entry.AddressIs64Bit = address->is_64_bit;
            entry.AddressIsKernelIp = address->is_kernel_ip;
            entry.AddressBuildId = addrBuildId;
            entry.AddressFiltered = address->filtered;
            entry.AddressComm = addrComm;
        }

        return entry;
    }

}
