using PerfConverter.PerfStructs;
using PerfConverter.Entry;

namespace PostProcess;

public class SegmentStack
{
    private PooledStack<ulong> _stack;
    private int _stackCount;
    private int _dropIndex;
    private int _segmentId;

    public SegmentStack()
    {
        _stack = new PooledStack<ulong>();
        _stackCount = 0;
        _dropIndex = 0;
        _segmentId = 0;
    }

    public int SegmentId => _segmentId;

    public TraceWithStackEntry ProcessTrace(TraceSampleEntry trace, List<ulong>? dropTimes)
    {
        // Handle drops if any
        if (dropTimes != null)
        {
            while (_dropIndex < dropTimes.Count && trace.Time >= dropTimes[_dropIndex])
            {
                _dropIndex++;
                ClearStack();
                _segmentId++;
            }
        }

        // Handle call
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
        {
            _stack.Push(trace.Id);
            _stackCount++;
        }

        // Create entry with current stack snapshot
        var entry = new TraceWithStackEntry
        {
            Id = trace.Id,
            PerfId = trace.PerfId,
            Pid = trace.Pid,
            Tid = trace.Tid,
            Time = trace.Time,
            Cpu = trace.Cpu,
            Flags = trace.Flags,
            Ip = trace.Ip,
            Addr = trace.Addr,
            Period = trace.Period,
            InsnCnt = trace.InsnCnt,
            CycCnt = trace.CycCnt,
            Weight = trace.Weight,
            Cpumode = trace.Cpumode,
            AddrCorrelatesSym = trace.AddrCorrelatesSym,
            EventId = trace.EventId,
            MachinePid = trace.MachinePid,
            Vcpu = trace.Vcpu,
            SegmentId = _segmentId,
            StackSnapshot = _stack.CreateSnapshot()
        };

        // Handle return
        if (trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN) && _stackCount > 0)
        {
            _stack.Pop();
            _stackCount--;
        }

        return entry;
    }

    private void ClearStack()
    {
        _stack = new PooledStack<ulong>();
        _stackCount = 0;
    }
}