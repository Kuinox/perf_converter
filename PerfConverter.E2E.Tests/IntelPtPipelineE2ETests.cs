using System.Diagnostics;
using System.Text;
using Plank.Reading;
using Plank.Schema;
using Temp.Schema;
using Temp.Schema.Schema;

namespace PerfConverter.E2E.Tests;

[Category("E2E")]
public sealed class IntelPtPipelineE2ETests
{
    [Test]
    public async Task IntelPtPipeline_ReconstructsKnownCStack()
    {
        await RequireLinuxToolingAsync();

        var repoRoot = FindRepoRoot();
        using var work = new LocalTempDirectory("perfconverter-e2e-");
        var targetSource = Path.Combine(TestContext.CurrentContext.TestDirectory, "Targets", "e2e_stack_target.c");
        var targetBinary = Path.Combine(work.Path, "e2e_stack_target");
        var perfData = Path.Combine(work.Path, "perf.data");
        var outputPath = Path.Combine(work.Path, "parquet_output");
        var tracePath = Path.Combine(work.Path, "trace.perfetto-trace");

        var dlfilterPublish = Path.Combine(work.Path, "tools", "dlfilter");
        var cliPublish = Path.Combine(work.Path, "tools", "cli");
        var stackFixerPublish = Path.Combine(work.Path, "tools", "stackfixer");
        var perfettoPublish = Path.Combine(work.Path, "tools", "perfetto");

        await RunAsync("dotnet",
            [
                "publish",
                Path.Combine(repoRoot, "PerfConverter", "PerfConverter.csproj"),
                "-c", "Release",
                "-r", "linux-x64",
                "-p:NativeLib=Shared",
                $"-p:PublishDir={dlfilterPublish}{Path.DirectorySeparatorChar}"
            ],
            repoRoot,
            TimeSpan.FromMinutes(4));

        var dlfilterPath = Path.Combine(dlfilterPublish, "PerfConverter.so");
        Assert.That(File.Exists(dlfilterPath), Is.True, $"Missing published dlfilter: {dlfilterPath}");

        await RunAsync("dotnet",
            [
                "publish",
                Path.Combine(repoRoot, "CLI", "CLI.csproj"),
                "-c", "Release",
                "-r", "linux-x64",
                "--self-contained", "false",
                "/p:UseAppHost=false",
                $"/p:PublishDir={cliPublish}{Path.DirectorySeparatorChar}",
                $"/p:PerfConverterSourcePath={dlfilterPath}"
            ],
            repoRoot,
            TimeSpan.FromMinutes(4));

        await RunAsync("dotnet",
            [
                "publish",
                Path.Combine(repoRoot, "StackFixer", "StackFixer.csproj"),
                "-c", "Release",
                "-r", "linux-x64",
                "--self-contained", "false",
                "/p:UseAppHost=false",
                $"/p:PublishDir={stackFixerPublish}{Path.DirectorySeparatorChar}"
            ],
            repoRoot,
            TimeSpan.FromMinutes(4));

        await RunAsync("dotnet",
            [
                "publish",
                Path.Combine(repoRoot, "PerfToPerfetto", "PerfToPerfetto.csproj"),
                "-c", "Release",
                "-r", "linux-x64",
                "--self-contained", "false",
                "/p:UseAppHost=false",
                $"/p:PublishDir={perfettoPublish}{Path.DirectorySeparatorChar}"
            ],
            repoRoot,
            TimeSpan.FromMinutes(4));

        await RunAsync(
            "gcc",
            ["-O0", "-g", "-fno-omit-frame-pointer", "-fno-inline", "-no-pie", targetSource, "-o", targetBinary],
            work.Path);

        var perfRecord = await RunAsync(
            "perf",
            ["record", "-m", "64M", "-e", "intel_pt//u", "-o", perfData, "--", targetBinary],
            work.Path,
            TimeSpan.FromMinutes(2),
            assertSuccess: false);

        if (perfRecord.ExitCode != 0 && IsPermissionFailure(perfRecord))
            Assert.Ignore(perfRecord.StandardError);
        Assert.That(perfRecord.ExitCode, Is.EqualTo(0), perfRecord.StandardError);

        await RunAsync(
            "dotnet",
            [
                Path.Combine(cliPublish, "CLI.dll"),
                perfData,
                "--output", outputPath,
                "--perf-args", "-f --itrace=bei0ns --no-inline"
            ],
            work.Path,
            TimeSpan.FromMinutes(5));

        await RunAsync(
            "dotnet",
            [Path.Combine(stackFixerPublish, "StackFixer.dll"), outputPath],
            work.Path,
            TimeSpan.FromMinutes(3));

        await RunAsync(
            "dotnet",
            [Path.Combine(perfettoPublish, "PerfToPerfetto.dll"), outputPath, tracePath],
            work.Path,
            TimeSpan.FromMinutes(3));

        var stackFrames = StackFrameReader.Read(Path.Combine(outputPath, "stack_frames.parquet"));
        var sourceLocations = SourceLocationReader.Read(Path.Combine(outputPath, "source_locations.parquet"));

        Assert.That(stackFrames, Is.Not.Empty);
        Assert.That(new FileInfo(tracePath).Length, Is.GreaterThan(0));
        AssertStackEventsAreWellNested(stackFrames);
        AssertKnownCallChainIsNested(stackFrames, sourceLocations);
    }

    static async Task RequireLinuxToolingAsync()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Intel PT E2E tests must run on the Linux perf machine.");

        await RequireCommandAsync("dotnet", ["--info"]);
        await RequireCommandAsync("gcc", ["--version"]);
        await RequireCommandAsync("perf", ["--version"]);

        var intelPt = await RunAsync("perf", ["list", "intel_pt"], assertSuccess: false);
        if (intelPt.ExitCode != 0 || !CombinedOutput(intelPt).Contains("intel_pt", StringComparison.Ordinal))
            Assert.Ignore("intel_pt is not available on this machine.");
    }

    static async Task RequireCommandAsync(string fileName, IReadOnlyList<string> arguments)
    {
        var result = await RunAsync(fileName, arguments, assertSuccess: false);
        if (result.ExitCode != 0)
            Assert.Ignore($"{fileName} is unavailable: {CombinedOutput(result)}");
    }

    static void AssertStackEventsAreWellNested(IReadOnlyList<StackFrame> frames)
    {
        foreach (var group in frames.GroupBy(static frame => frame.Tid))
        {
            var stack = new Stack<StackFrame>();
            foreach (var endpoint in group.SelectMany(CreatePerfettoEndpoints).Order(StackEndpointComparer.Instance))
            {
                if (endpoint.IsBegin)
                {
                    stack.Push(endpoint.Frame);
                    continue;
                }

                Assert.That(stack, Is.Not.Empty, $"tid={group.Key} ended frame {endpoint.FrameId} with an empty stack.");
                var openFrame = stack.Pop();
                Assert.That(openFrame.FrameId, Is.EqualTo(endpoint.FrameId),
                    $"tid={group.Key} ended frame {endpoint.FrameId} while frame {openFrame.FrameId} was open on top.");
            }

            Assert.That(
                stack.All(static frame => frame.EndReason != StackFrameBoundaryReason.Return),
                Is.True,
                $"tid={group.Key} still has real returned frames open after replaying stack events.");
        }
    }

    static IEnumerable<StackEndpoint> CreatePerfettoEndpoints(StackFrame frame)
    {
        if (frame.StartReason != StackFrameBoundaryReason.TraceResume)
            yield return new StackEndpoint(frame.StartTime, frame.StartTrace, frame.Depth, frame.FrameId, IsBegin: true, frame);

        if (frame.EndReason == StackFrameBoundaryReason.Return)
            yield return new StackEndpoint(frame.EndTime, frame.EndTrace, frame.Depth, frame.FrameId, IsBegin: false, frame);
    }

    static void AssertKnownCallChainIsNested(
        IReadOnlyList<StackFrame> frames,
        IReadOnlyDictionary<ulong, SourceLocation> sourceLocations)
    {
        var symbolByLocationId = sourceLocations.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Symbol ?? string.Empty);

        var rootIds = FindLocationIds(symbolByLocationId, "e2e_root");
        var midIds = FindLocationIds(symbolByLocationId, "e2e_mid");
        var leafIds = FindLocationIds(symbolByLocationId, "e2e_leaf");

        Assert.That(rootIds, Is.Not.Empty, "No source location was resolved for e2e_root.");
        Assert.That(midIds, Is.Not.Empty, "No source location was resolved for e2e_mid.");
        Assert.That(leafIds, Is.Not.Empty, "No source location was resolved for e2e_leaf.");

        foreach (var group in frames.GroupBy(static frame => frame.Tid))
        {
            var byDepth = group.ToArray();
            foreach (var root in byDepth.Where(frame => rootIds.Contains(frame.LocationId)))
            {
                var mid = byDepth.FirstOrDefault(frame =>
                    midIds.Contains(frame.LocationId) &&
                    frame.Depth == root.Depth + 1 &&
                    IsContainedBy(frame, root));

                if (mid.FrameId == 0)
                    continue;

                var leaf = byDepth.FirstOrDefault(frame =>
                    leafIds.Contains(frame.LocationId) &&
                    frame.Depth == mid.Depth + 1 &&
                    IsContainedBy(frame, mid));

                if (leaf.FrameId != 0)
                    return;
            }
        }

        Assert.Fail($"""
            Could not find nested e2e_root -> e2e_mid -> e2e_leaf frames in stack_frames.parquet.

            e2e_root frames:
            {DescribeFrames(frames, rootIds)}

            e2e_mid frames:
            {DescribeFrames(frames, midIds)}

            e2e_leaf frames:
            {DescribeFrames(frames, leafIds)}
            """);
    }

    static string DescribeFrames(IReadOnlyList<StackFrame> frames, IReadOnlySet<ulong> locationIds)
    {
        var matches = frames
            .Where(frame => locationIds.Contains(frame.LocationId))
            .OrderBy(static frame => frame.StartTime)
            .Take(12)
            .Select(static frame =>
                $"id={frame.FrameId} tid={frame.Tid} depth={frame.Depth} start={frame.StartTime} end={frame.EndTime} startTrace={frame.StartTrace} endTrace={frame.EndTrace} endReason={frame.EndReason}");

        var count = frames.Count(frame => locationIds.Contains(frame.LocationId));
        return $"count={count}\n" + string.Join('\n', matches);
    }

    static HashSet<ulong> FindLocationIds(IReadOnlyDictionary<ulong, string> symbols, string expectedSymbol)
        => symbols
            .Where(pair => pair.Value.Contains(expectedSymbol, StringComparison.Ordinal))
            .Select(static pair => pair.Key)
            .ToHashSet();

    static bool IsContainedBy(StackFrame child, StackFrame parent)
        => child.StartTime >= parent.StartTime &&
           child.EndTime <= parent.EndTime;

    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PerfConverter.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PerfConverter.sln from the test output directory.");
    }

    static async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        bool assertSuccess = true)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                stdout.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                stderr.AppendLine(args.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException($"Command timed out: {fileName} {string.Join(' ', arguments)}");
        }

        var result = new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        if (assertSuccess && result.ExitCode != 0)
        {
            Assert.Fail($"""
                Command failed with exit code {result.ExitCode}: {fileName} {string.Join(' ', arguments)}

                STDOUT:
                {result.StandardOutput}

                STDERR:
                {result.StandardError}
                """);
        }

        return result;
    }

    static T[] ReadColumn<T>(RowGroupReader rowGroup, Column column)
    {
        var values = new List<T>();
        foreach (var page in rowGroup.Column<T>(column).Pages)
        {
            var span = page.Values.Span;
            for (var i = 0; i < span.Length; i++)
                values.Add(span[i]);
        }

        return [.. values];
    }

    static bool IsPermissionFailure(CommandResult result)
    {
        var output = CombinedOutput(result);
        return output.Contains("No permission", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("perf_event_paranoid", StringComparison.OrdinalIgnoreCase);
    }

    static string CombinedOutput(CommandResult result)
        => result.StandardOutput + result.StandardError;

    sealed class LocalTempDirectory : IDisposable
    {
        public LocalTempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    static class StackFrameReader
    {
        public static IReadOnlyList<StackFrame> Read(string path)
        {
            var rows = new List<StackFrame>();
            using var stream = File.OpenRead(path);
            using var reader = StackFrameRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var frameIds = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[0]);
                var tids = ReadColumn<uint>(rowGroup, StackFrameRowSchema.Schema.Columns[1]);
                var depths = ReadColumn<uint>(rowGroup, StackFrameRowSchema.Schema.Columns[2]);
                var startTimes = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[3]);
                var endTimes = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[4]);
                var startTraces = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[5]);
                var endTraces = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[6]);
                var locationIds = ReadColumn<ulong>(rowGroup, StackFrameRowSchema.Schema.Columns[7]);
                var startReasons = ReadColumn<byte>(rowGroup, StackFrameRowSchema.Schema.Columns[10]);
                var endReasons = ReadColumn<byte>(rowGroup, StackFrameRowSchema.Schema.Columns[11]);

                for (var i = 0; i < frameIds.Length; i++)
                {
                    rows.Add(new StackFrame(
                        frameIds[i],
                        tids[i],
                        depths[i],
                        startTimes[i],
                        endTimes[i],
                        startTraces[i],
                        endTraces[i],
                        locationIds[i],
                        (StackFrameBoundaryReason)startReasons[i],
                        (StackFrameBoundaryReason)endReasons[i]));
                }
            }

            return rows;
        }
    }

    static class SourceLocationReader
    {
        public static IReadOnlyDictionary<ulong, SourceLocation> Read(string path)
        {
            var rows = new Dictionary<ulong, SourceLocation>();
            using var stream = File.OpenRead(path);
            using var reader = SourceLocationRowSchema.Schema.CreateReader(stream);

            foreach (var token in reader.EnumerateRowGroups())
            {
                using var rowGroup = reader.OpenRowGroup(stream, token);
                var ids = ReadColumn<ulong>(rowGroup, SourceLocationRowSchema.Schema.Columns[0]);
                var symbols = ReadColumn<byte[]>(rowGroup, SourceLocationRowSchema.Schema.Columns[4]);

                for (var i = 0; i < ids.Length; i++)
                    rows[ids[i]] = new SourceLocation(BytesToString(symbols[i]));
            }

            return rows;
        }

        static string? BytesToString(byte[]? bytes)
            => bytes is { Length: > 0 } ? Encoding.UTF8.GetString(bytes) : null;
    }

    sealed class StackEndpointComparer : IComparer<StackEndpoint>
    {
        public static StackEndpointComparer Instance { get; } = new();

        public int Compare(StackEndpoint left, StackEndpoint right)
        {
            var compare = left.Time.CompareTo(right.Time);
            if (compare != 0)
                return compare;

            compare = left.Trace.CompareTo(right.Trace);
            if (compare != 0)
                return compare;

            if (left.FrameId == right.FrameId && left.IsBegin != right.IsBegin)
                return left.IsBegin ? -1 : 1;

            if (left.IsBegin != right.IsBegin)
                return left.IsBegin ? 1 : -1;

            compare = left.IsBegin
                ? left.Depth.CompareTo(right.Depth)
                : right.Depth.CompareTo(left.Depth);
            if (compare != 0)
                return compare;

            return left.FrameId.CompareTo(right.FrameId);
        }
    }

    readonly record struct CommandResult(int ExitCode, string StandardOutput, string StandardError);

    readonly record struct StackFrame(
        ulong FrameId,
        uint Tid,
        uint Depth,
        ulong StartTime,
        ulong EndTime,
        ulong StartTrace,
        ulong EndTrace,
        ulong LocationId,
        StackFrameBoundaryReason StartReason,
        StackFrameBoundaryReason EndReason);

    readonly record struct SourceLocation(string? Symbol);

    readonly record struct StackEndpoint(
        ulong Time,
        ulong Trace,
        uint Depth,
        ulong FrameId,
        bool IsBegin,
        StackFrame Frame);
}
