using System.Runtime.CompilerServices;

namespace Temp.Schema;

/// <summary>
/// Traverses a PerfConverter output directory and invokes a visitor
/// for each pid/tid/segment discovered (visitor-pattern style).
/// Expected layout:
///   <root>/<pid>/<tid>/segment<id>.parquet
///   <root>/<pid>/<tid>/segment<id>_stackranges.parquet (optional)
/// </summary>
public abstract class Visitor
{
    public virtual async Task VisitRoot(string path)
    {
        await VisitTracks([.. Directory.EnumerateDirectories(path)]);
    }

    public virtual async Task VisitTracks(string[] paths)
    {
        foreach (var tid in paths)
        {
            await VisitTrack(tid);
        }
    }

    public virtual async Task VisitTrack(string path)
    {
        var segments = Directory.EnumerateFiles(path, "segment*.parquet")
            .Where(x => !x.Contains("_stackranges.parquet"));
        foreach (var segment in segments)
        {
            var segmentIdStr = Path.GetFileName(segment).Replace("segment", "").Replace(".parquet", "");
            if (!int.TryParse(segmentIdStr, out var segmentId))
                throw new InvalidDataException($"Invalid segment file name: {segmentIdStr}");

            var stackRangesFile = segment.Replace(".parquet", "_stackranges.parquet");

            if (File.Exists(stackRangesFile))
                await VisitSegmentWithStackRanges(segment, stackRangesFile);
            else
                await VisitSegment(segment);
        }
    }

    public virtual async Task VisitSegmentWithStackRanges(string segmentFile, string stackRangesFile)
    {
        await VisitSegment(segmentFile);
        await VisitStackRanges(stackRangesFile);
    }

    public virtual Task VisitStackRanges(string stackRangesFile)
        => Task.CompletedTask;

    public virtual Task VisitSegment(string segmentFile)
        => Task.CompletedTask;
}