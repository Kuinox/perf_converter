using System.Runtime.CompilerServices;

namespace Temp.Schema;

/// <summary>
/// Traverses a PerfConverter output directory and invokes a visitor
/// for each pid/tid/segment discovered (visitor-pattern style).
/// Expected layout:
///   <root>/<pid>/<tid>/segment<id>.parquet
///   <root>/<pid>/<tid>/segment<id>_stackranges.parquet (optional)
/// </summary>
public abstract class FileVisitor
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
        var files = Directory.EnumerateFiles(path, "*.parquet");
        foreach (var file in files)
        {
            await VisitFile(file);
        }
    }

    public virtual Task VisitFile(string segmentFile)
        => Task.CompletedTask;
}