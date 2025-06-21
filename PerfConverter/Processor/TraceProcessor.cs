using System.Runtime.InteropServices;
using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using Temp.Core;

namespace PerfConverter.Processor;

public unsafe class TraceProcessor(Func<string, IPersister<TraceEntry>> persistenceFactory) : ITraceProcessor
{
    ulong _totalSamples = 0;
    readonly Dictionary<string, IPersister<TraceEntry>> _persistence = [];
    readonly Dictionary<(int, int), string> _keys = [];
    public ulong QueueData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        var id = _totalSamples++;
        ref var key = ref CollectionsMarshal.GetValueRefOrAddDefault(_keys, (sample->pid, sample->tid), out _);
        key ??= $"{sample->pid}/{sample->tid}";
        ref var persistence = ref CollectionsMarshal.GetValueRefOrAddDefault(_persistence, key, out _);
        persistence ??= persistenceFactory(key);
        
        var eventString = Marshal.PtrToStringUTF8(sample->@event);
        var sym = Marshal.PtrToStringUTF8(ip->sym);
        var dso = Marshal.PtrToStringUTF8(ip->dso);
        var ipBuildId = new Span<byte>(ip->buildid, ip->buildid_size).ToArray();
        var ipComm = Marshal.PtrToStringUTF8(ip->comm);

        var entry = new TraceEntry
        {
            Id = id,
            PerfId = sample->id,
            Pid = (uint)sample->pid,
            Tid = (uint)sample->tid,
            Time = sample->time,
            Cpu = (uint)sample->cpu,
            Flags = (DLFilterFlag)sample->flags,
            Period = sample->period,
            InsnCnt = sample->insn_cnt,
            CycCnt = sample->cyc_cnt,
            Weight = sample->weight,
            Cpumode = sample->cpumode,
            AddrCorrelatesSym = sample->addr_correlates_sym,
            Event = eventString,
            MachinePid = (uint)sample->machine_pid,
            Vcpu = (uint)sample->vcpu,

            IpAddress = ip->addr ,
            IpSymoff = ip->symoff,
            IpSym = sym,
            IpSymStart = ip->sym_start ,
            IpSymEnd = ip->sym_end ,
            IpDso = dso,
            IpSymBinding = ip->sym_binding,
            IpIs64Bit = ip->is_64_bit,
            IpIsKernelIp = ip->is_kernel_ip,
            IpBuildId = ipBuildId,
            IpFiltered = ip->filtered,
            IpComm = ipComm
        };

        if(address != null)
        {
            var addrSym = Marshal.PtrToStringUTF8(address->sym);
            var addrDso = Marshal.PtrToStringUTF8(address->dso);
            var addrBuildId = new Span<byte>(address->buildid, address->buildid_size).ToArray();
            var addrComm = Marshal.PtrToStringUTF8(address->comm);
            
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

        persistence.Persist(entry);

        return id;
    }
}
