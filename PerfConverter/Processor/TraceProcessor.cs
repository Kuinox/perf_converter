using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Temp.Core;
using Temp.Schema;

namespace PerfConverter.Processor;

public unsafe class TraceProcessor : ITraceProcessor
{
    readonly IReadOnlyDictionary<(int, int), (List<ulong> items, int count)> _auxDataLoss;
    readonly Dictionary<string, IPersister<TraceEntry>> _persistence = [];
    readonly Dictionary<(int, int), int> _traceCountPerPidTid = [];
    readonly Dictionary<(int, int, int), string> _keys = [];
    readonly Dictionary<string, ulong> _counts = [];
    readonly Func<string, IPersister<TraceEntry>> _persistenceFactory;

    public TraceProcessor(Func<string, IPersister<TraceEntry>> persistenceFactory)
    {
        _persistenceFactory = persistenceFactory;
        var auxDataLossJson = Environment.GetEnvironmentVariable("AUX_DATA_LOSS")!;
        var auxDataLoss = JsonSerializer.Deserialize(auxDataLossJson, SourceGenerationContext.Default.AuxDataLostArray)!;
        _auxDataLoss = auxDataLoss
            .GroupBy(x => (x.Pid, x.Tid))
            .ToDictionary(
                x => x.Key,
                x => x.Select(x => x.Time).Append(0uL).OrderDescending().ToList())
            .ToDictionary(
            x => x.Key,
            x => (x.Value, x.Value.Count));
    }

    public ulong QueueData(PerfDlFilterSample* sample, PerfDlfilterAl* ip, PerfDlfilterAl* address)
    {
        var (items, count) = _auxDataLoss[(sample->pid, sample->tid)];
        var fileCount = count - items.Count;

        var isNewTrace = items.Count > 0 && items[^1] < sample->time;

        if (isNewTrace)
            _auxDataLoss[(sample->pid, sample->tid)].items.Remove(0);

        ref var key = ref CollectionsMarshal.GetValueRefOrAddDefault(_keys, (sample->pid, sample->tid, fileCount), out _);
        key ??= $"{sample->pid}/{sample->tid}/{fileCount}";

        ref var persistence = ref CollectionsMarshal.GetValueRefOrAddDefault(_persistence, key, out _);
        persistence ??= _persistenceFactory(key);

        ref var id = ref CollectionsMarshal.GetValueRefOrAddDefault(_counts, key, out _);
        if(isNewTrace)
            id = 0;
        
        id++;

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

        persistence.Persist(entry);

        return id;
    }
}
