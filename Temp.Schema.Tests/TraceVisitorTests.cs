using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using Temp.Schema;

namespace Temp.Schema.Tests;

public class TraceVisitorTests
{
    [Test]
    public async Task EntryLogicFindEntries()
    {
        Assert.That(await GetEntries().AnyAsync());
    }

    [Test]
    public async Task FeedSingleSegmentIntoTraceVisitor()
    {

        var entries = GetEntries();
        var visitor = new TraceVisitor(entries);

        await visitor.Visit();

        Assert.Pass("TraceVisitor completed without exceptions.");
    }

    static IAsyncEnumerable<TraceEntry> GetEntries() => new[]
    {
        new TraceEntry
        {
            Event = "branches:",
            Flags = DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_BEGIN | DLFilterFlag.PERF_DLFILTER_FLAG_CALL
        },
        new TraceEntry
        {
            Event = "branches:",
            Flags = DLFilterFlag.PERF_DLFILTER_FLAG_RETURN | DLFilterFlag.PERF_DLFILTER_FLAG_TRACE_END
        }
    }.ToAsyncEnumerable();
}
