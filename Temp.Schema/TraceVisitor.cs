using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Temp.Schema;

public class TraceVisitor(IAsyncEnumerable<TraceEntry> entries)
{
    public virtual async ValueTask Visit()
    {
        await foreach (var entry in entries)
        {
            VisitEntry(entry);
        }
     }

    protected virtual void VisitEntry(TraceEntry entry)
    {
        if (entry.Event.StartsWith("branches:"))
        {
            VisitBranch(entry);
        }
        if (entry.Event.StartsWith("instructions:"))
        {
            VisitInstruction(entry);
        }
    }

    protected virtual void VisitInstruction(TraceEntry entry)
    {
    }

    TraceEntry _previous;
    protected virtual void VisitBranch(TraceEntry entry)
    {
        if (entry.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_BEGIN))
        {
            VisitTraceBegin(entry);
        }

        if (entry.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_END))
        {
            VisitTraceEnd(entry);
        }

        if (entry.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL))
        {
            VisitPush(entry);
        }
        if (entry.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN))
        {
            Return(entry);
        }
        _previous = entry;
    }
    bool _first = true;
    bool _groundedTrace = true;
    protected virtual void VisitTraceBegin(TraceEntry entry)
    {
        if (!_first)
        {
            // regular trace have multiple begin/end, i think that's happens when the thread get descheduled.
            // i speculate that we have a broken trace when there is a new trace start without a previous end.
            var previousIsEnd = _previous.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_END);
            var brokenTrace = !previousIsEnd;

            if (brokenTrace)
            {
                _segments.Add(new());
                _groundedTrace = false;
            }
        }
        _first = false;
    }

    /// <summary>
    /// Not the end of the full trace, it's the end of a subsection of the trace, but that's how perf call it.
    /// I speculate thoses are caused by thread interrupt.
    /// </summary>
    /// <param name="entry"></param>
    protected virtual void VisitTraceEnd(TraceEntry entry)
    {
    }


    readonly List<SegmentState> _segments = [new()];
    SegmentState CurrentSegment => _segments.Last();
    protected virtual void Return(TraceEntry entry)
    {
        if (!CurrentSegment.Stack.TryPop(out _))
        {
            CurrentSegment.ReverseStack.Push(entry);
        }
    }

    protected virtual void VisitPush(TraceEntry entry)
    {
        CurrentSegment.Stack.Push(entry);
    }


    class SegmentState
    {
        public Stack<TraceEntry> Stack { get; } = [];
        public Stack<TraceEntry> ReverseStack { get; } = [];
    }

}
