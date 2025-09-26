using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PerfConverter.Schema;
using Temp.Schema;

namespace Temp.Schema.Tests;

public class TraceVisitorTests
{
    private const string BasePath = @"C:\Users\Kuinox\Documents\parquet_output\parquet_output\pid=18461\tid=18461";

    [Test]
    public async Task FeedSingleSegmentIntoTraceVisitor()
    {
        var schema = new TraceSampleSchema();

        var entries = Directory
            .EnumerateFiles(BasePath, "segment*.parquet", SearchOption.TopDirectoryOnly)
            .Where(x => !x.Contains("stack"))
            .OrderBy(f => int.Parse(Path.GetFileName(f).Replace("segment", "").Replace(".parquet", "")))
            .ToAsyncEnumerable()
            .SelectMany(x => schema.ReadAll(x));


        var visitor = new TraceVisitor(entries);

        await visitor.Visit();

        Assert.Pass("TraceVisitor completed without exceptions.");
    }
}
