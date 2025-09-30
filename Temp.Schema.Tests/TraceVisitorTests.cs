using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PerfConverter.Entry;
using PerfConverter.Schema;
using Temp.Schema;
using Temp.Schema.Schema;

namespace Temp.Schema.Tests;

public class TraceVisitorTests
{
    private const string BranchesPath = @"C:\Users\Kuinox\Documents\parquet_output\pid=18461\tid=18461\branches.parquet";

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

    static IAsyncEnumerable<TraceEntry> GetEntries() => new TraceSampleSchema().ReadAll(BranchesPath);
}
