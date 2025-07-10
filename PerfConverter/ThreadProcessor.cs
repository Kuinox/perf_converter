using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Runtime.InteropServices;
using Temp.Core;

namespace PerfConverter;

unsafe class ThreadProcessor(uint tid, uint pid, IEnumerable<ulong> auxDrop, Func<string, IPersister<TraceEntry>> tracePersistenceFactory, Func<string, IPersister<StackRange>> stackRangePersistenceFactory)
{
    readonly Queue<ulong> auxDrop = new(auxDrop.Order());
    int _currentSegmentId = 0;
    ulong _currentEntryId = 0;
    string _currentTraceKey = null!;
    string _currentStackRangeKey = null!;
    IPersister<TraceEntry>? _tracePersister;
    IPersister<StackRange>? _stackRangePersister;
    readonly Stack<long> _stackStarts = new();

    public uint Tid { get; } = tid;
    public uint Pid { get; } = pid;


    public void QueueData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        var isNewTrace = auxDrop.TryPeek(out var newTraceTime) && newTraceTime < sample->time;
        if (isNewTrace)
        {
            _tracePersister?.DisposeAsync().AsTask();
            _stackRangePersister?.DisposeAsync().AsTask();
            auxDrop.Dequeue();
            _currentSegmentId++;
            _currentTraceKey = $"{sample->pid}/{sample->tid}/segment{_currentSegmentId}.parquet";
            _currentStackRangeKey = $"{sample->pid}/{sample->tid}/segment{_currentSegmentId}_stackranges.parquet";
            _currentEntryId=0;
            _tracePersister = tracePersistenceFactory(_currentTraceKey);
            _stackRangePersister = stackRangePersistenceFactory(_currentStackRangeKey);
            _stackStarts.Clear();
        }

        var entry = BuildEntry(sample, ip, address, ++_currentEntryId);
        _tracePersister!.Persist(entry);
        
        // Process stack tracking for call/return pairs
        ProcessStackTracking(entry);
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

    private void ProcessStackTracking(TraceEntry trace)
    {
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
        {
            _stackStarts.Push((long)trace.Id);
        }
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
        {
            var startTrace = _stackStarts.Count == 0 ? -1 : _stackStarts.Pop();
            var stackRange = new StackRange()
            {
                StartTrace = startTrace,
                EndTrace = (long)trace.Id
            };
            _stackRangePersister!.Persist(stackRange);
        }
    }

}
