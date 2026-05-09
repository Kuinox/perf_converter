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
    static readonly IntelPtTarget[] Targets =
    [
        new("01_leaf", "e2e_01_leaf.c", ["e2e_leaf"]),
        new("02_chain", "e2e_02_chain.c", ["e2e_root", "e2e_mid", "e2e_leaf"]),
        new("03_branching", "e2e_03_branching.c", ["e2e_root", "e2e_branch", "e2e_left_leaf", "e2e_right_leaf"]),
        new("04_recursion", "e2e_04_recursion.c", ["e2e_root", "e2e_recursive"])
    ];

    [TestCaseSource(nameof(Targets))]
    public async Task IntelPtPipeline_ReconstructsKnownCStack(IntelPtTarget target)
    {
        Log("checking Linux perf tooling");
        await RequireLinuxToolingAsync();

        var repoRoot = FindRepoRoot();
        using var work = new LocalTempDirectory($"perfconverter-e2e-{target.Name}-");
        Log($"repo={repoRoot}");
        Log($"work={work.Path}");
        var targetSource = Path.Combine(TestContext.CurrentContext.TestDirectory, "Targets", target.SourceFile);
        var targetBinary = Path.Combine(work.Path, Path.GetFileNameWithoutExtension(target.SourceFile));
        var sampledTargetBinary = Path.Combine(work.Path, Path.GetFileNameWithoutExtension(target.SourceFile) + "-sampled");
        var perfData = Path.Combine(work.Path, "perf.data");
        var sampledPerfData = Path.Combine(work.Path, "sampled-perf.data");
        var outputPath = Path.Combine(work.Path, "parquet_output");
        var tracePath = Path.Combine(work.Path, "trace.perfetto-trace");

        var tools = FindPublishedTools(repoRoot);
        Log($"CLI={tools.CliDll}");
        Log($"StackFixer={tools.StackFixerDll}");
        Log($"PerfToPerfetto={tools.PerfToPerfettoDll}");

        Log("compiling C target");
        await RunAsync(
            "gcc",
            ["-O0", "-g", "-fno-omit-frame-pointer", "-fno-inline", targetSource, "-o", targetBinary],
            work.Path);

        Log("compiling sampled C target");
        await RunAsync(
            "gcc",
            [
                "-O0", "-g", "-fno-omit-frame-pointer", "-fno-inline",
                "-DE2E_SPIN_SCALE=1000", "-DE2E_WARMUP_ROUNDS=1",
                targetSource, "-o", sampledTargetBinary
            ],
            work.Path);

        Log("recording Intel PT data");
        var perfRecord = await RunAsync(
            "perf",
            ["record", "-m", "64M", "-e", "intel_pt//u", "-o", perfData, "--", targetBinary],
            work.Path,
            TimeSpan.FromMinutes(2),
            assertSuccess: false);

        if (perfRecord.ExitCode != 0 && IsPermissionFailure(perfRecord))
            Assert.Ignore(perfRecord.StandardError);
        Assert.That(perfRecord.ExitCode, Is.EqualTo(0), perfRecord.StandardError);

        Log("recording sampled callgraph truth");
        var sampledRecord = await RunAsync(
            "perf",
            ["record", "-F", "2000", "-e", "cpu-clock:u", "-g", "--call-graph", "fp", "-o", sampledPerfData, "--", sampledTargetBinary],
            work.Path,
            TimeSpan.FromMinutes(2),
            assertSuccess: false);

        if (sampledRecord.ExitCode != 0 && IsPermissionFailure(sampledRecord))
            Assert.Ignore(sampledRecord.StandardError);
        Assert.That(sampledRecord.ExitCode, Is.EqualTo(0), sampledRecord.StandardError);

        var sampledScript = await RunAsync(
            "perf",
            ["script", "-i", sampledPerfData],
            work.Path,
            TimeSpan.FromMinutes(2));

        Log("converting perf data to parquet");
        await RunAsync(
            "dotnet",
            [
                tools.CliDll,
                perfData,
                "--output", outputPath
            ],
            work.Path,
            TimeSpan.FromMinutes(5));

        Log("reconstructing stack frames");
        await RunAsync(
            "dotnet",
            [tools.StackFixerDll, outputPath],
            work.Path,
            TimeSpan.FromMinutes(3));

        Log("writing Perfetto trace");
        await RunAsync(
            "dotnet",
            [tools.PerfToPerfettoDll, outputPath, tracePath],
            work.Path,
            TimeSpan.FromMinutes(3));

        Log("reading stack parquet");
        var stackFrames = StackFrameReader.Read(Path.Combine(outputPath, "stack_frames.parquet"));
        var sourceLocations = SourceLocationReader.Read(Path.Combine(outputPath, "source_locations.parquet"));

        Assert.That(stackFrames, Is.Not.Empty);
        Assert.That(new FileInfo(tracePath).Length, Is.GreaterThan(0));
        AssertStackEventsAreWellNested(stackFrames);
        AssertExpectedSymbols(target, stackFrames, sourceLocations);
        AssertSampledStacksMatchReconstruction(target, stackFrames, sourceLocations, sampledScript.StandardOutput);
    }

    static void Log(string message)
        => TestContext.Progress.WriteLine($"[{DateTimeOffset.UtcNow:O}] {message}");

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
            var maxOpenFrames = 0;
            foreach (var endpoint in group.SelectMany(CreatePerfettoEndpoints).Order(StackEndpointComparer.Instance))
            {
                if (endpoint.IsBegin)
                {
                    stack.Push(endpoint.Frame);
                    maxOpenFrames = Math.Max(maxOpenFrames, stack.Count);
                    continue;
                }

                Assert.That(stack, Is.Not.Empty, $"tid={group.Key} ended frame {endpoint.FrameId} with an empty stack.");
                var open = stack.Pop();
                Assert.That(
                    open.FrameId,
                    Is.EqualTo(endpoint.FrameId),
                    $"tid={group.Key} ended frame {endpoint.FrameId} while frame {open.FrameId} was on top.");
            }

            Assert.That(maxOpenFrames, Is.LessThan(512), $"tid={group.Key} opened an unexpectedly deep visible Perfetto stack.");
        }
    }

    static IEnumerable<StackEndpoint> CreatePerfettoEndpoints(StackFrame frame)
    {
        if (frame.StartReason != StackFrameBoundaryReason.TraceResume)
            yield return new StackEndpoint(frame.StartTime, frame.StartTrace, frame.Depth, frame.FrameId, IsBegin: true, frame);

        if (frame.EndReason == StackFrameBoundaryReason.Return)
            yield return new StackEndpoint(frame.EndTime, frame.EndTrace, frame.Depth, frame.FrameId, IsBegin: false, frame);
    }

    static void AssertExpectedSymbols(
        IntelPtTarget target,
        IReadOnlyList<StackFrame> frames,
        IReadOnlyDictionary<ulong, SourceLocation> sourceLocations)
    {
        var symbolByLocationId = sourceLocations.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Symbol ?? string.Empty);

        foreach (var expectedSymbol in target.ExpectedSymbols)
        {
            var locationIds = FindLocationIds(symbolByLocationId, expectedSymbol);
            Assert.That(locationIds, Is.Not.Empty, $"{target.Name}: no source location was resolved for {expectedSymbol}.");

            var frameCount = frames.Count(frame => locationIds.Contains(frame.LocationId));
            TestContext.Progress.WriteLine($"{target.Name}: {expectedSymbol} sourceLocations={locationIds.Count} stackFrames={frameCount}");
            Assert.That(frameCount, Is.GreaterThan(0), $"{target.Name}: no reconstructed stack frame was emitted for {expectedSymbol}.\n{DescribeFrames(frames, locationIds)}");

            var maxDepth = frames
                .Where(frame => locationIds.Contains(frame.LocationId))
                .Select(static frame => frame.Depth)
                .DefaultIfEmpty()
                .Max();
            Assert.That(maxDepth, Is.LessThan(32), $"{target.Name}: reconstructed stack depth drifted for {expectedSymbol}.\n{DescribeFrames(frames, locationIds)}");
        }
    }

    static void AssertSampledStacksMatchReconstruction(
        IntelPtTarget target,
        IReadOnlyList<StackFrame> frames,
        IReadOnlyDictionary<ulong, SourceLocation> sourceLocations,
        string perfScript)
    {
        var samples = ParseSampledStacks(perfScript, target.ExpectedSymbols);
        Assert.That(samples, Is.Not.Empty, $"{target.Name}: perf sampling did not capture any expected target stack.");

        var framesBySymbol = BuildFramesBySymbol(frames, sourceLocations, target.ExpectedSymbols);
        foreach (var symbol in samples.SelectMany(static sample => sample).Distinct(StringComparer.Ordinal))
        {
            Assert.That(
                framesBySymbol.GetValueOrDefault(symbol),
                Is.Not.Null.And.Not.Empty,
                $"{target.Name}: sampled stack included {symbol}, but Intel PT reconstruction emitted no matching frame.");
        }

        var observedPairs = samples
            .SelectMany(static sample => sample.Zip(sample.Skip(1), static (parent, child) => (Parent: parent, Child: child)))
            .Distinct()
            .ToArray();

        if (target.ExpectedSymbols.Length < 2)
            return;

        Assert.That(observedPairs, Is.Not.Empty, $"{target.Name}: sampled stacks did not include an expected parent/child pair.");
        foreach (var pair in observedPairs)
        {
            Assert.That(
                HasOverlappingOrderedFrames(framesBySymbol[pair.Parent], framesBySymbol[pair.Child]),
                Is.True,
                $"{target.Name}: sampled stack contained {pair.Parent} -> {pair.Child}, but reconstructed frames did not overlap in that order.");
        }
    }

    static IReadOnlyList<string[]> ParseSampledStacks(string perfScript, IReadOnlyCollection<string> expectedSymbols)
    {
        var expected = expectedSymbols.ToHashSet(StringComparer.Ordinal);
        var samples = new List<string[]>();
        var current = new List<string>();

        foreach (var line in perfScript.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                AddCurrentSample();
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                AddCurrentSample();
                continue;
            }

            var symbol = TryParsePerfScriptSymbol(line);
            if (symbol is not null && expected.Contains(symbol) && current.LastOrDefault() != symbol)
                current.Add(symbol);
        }

        AddCurrentSample();
        return samples;

        void AddCurrentSample()
        {
            if (current.Count != 0)
                samples.Add(current.AsEnumerable().Reverse().ToArray());
            current.Clear();
        }
    }

    static string? TryParsePerfScriptSymbol(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].All(Uri.IsHexDigit))
            return null;

        var symbol = parts[1];
        var offset = symbol.IndexOf('+', StringComparison.Ordinal);
        if (offset >= 0)
            symbol = symbol[..offset];

        return symbol.Length == 0 ? null : symbol;
    }

    static Dictionary<string, List<StackFrame>> BuildFramesBySymbol(
        IReadOnlyList<StackFrame> frames,
        IReadOnlyDictionary<ulong, SourceLocation> sourceLocations,
        IEnumerable<string> symbols)
    {
        var result = symbols.Distinct(StringComparer.Ordinal).ToDictionary(
            static symbol => symbol,
            static _ => new List<StackFrame>(),
            StringComparer.Ordinal);

        foreach (var frame in frames)
        {
            if (!sourceLocations.TryGetValue(frame.LocationId, out var location) || location.Symbol is null)
                continue;

            if (result.TryGetValue(location.Symbol, out var matches))
                matches.Add(frame);
        }

        return result;
    }

    static bool HasOverlappingOrderedFrames(IReadOnlyList<StackFrame> parents, IReadOnlyList<StackFrame> children)
        => parents.Any(parent => children.Any(child => parent.Depth < child.Depth && Overlaps(parent, child)));

    static bool Overlaps(StackFrame parent, StackFrame child)
        => IsBeforeOrEqual(parent.StartTime, parent.StartTrace, child.StartTime, child.StartTrace) &&
           IsBeforeOrEqual(child.EndTime, child.EndTrace, parent.EndTime, parent.EndTrace);

    static bool IsBeforeOrEqual(ulong leftTime, ulong leftTrace, ulong rightTime, ulong rightTrace)
        => leftTime < rightTime || (leftTime == rightTime && leftTrace <= rightTrace);

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

    static PublishedTools FindPublishedTools(string repoRoot)
    {
        var cliDll = Path.Combine(repoRoot, "CLI", "bin", "Release", "net10.0", "linux-x64", "publish", "CLI.dll");
        var stackFixerDll = Path.Combine(repoRoot, "StackFixer", "bin", "Release", "net10.0", "linux-x64", "publish", "StackFixer.dll");
        var perfToPerfettoDll = Path.Combine(repoRoot, "PerfToPerfetto", "bin", "Release", "net10.0", "linux-x64", "publish", "PerfToPerfetto.dll");
        var dlfilter = Path.Combine(repoRoot, "CLI", "bin", "Release", "net10.0", "linux-x64", "publish", "PerfConverter.so");

        var missing = new[]
            {
                cliDll,
                stackFixerDll,
                perfToPerfettoDll,
                dlfilter
            }
            .Where(static path => !File.Exists(path))
            .ToArray();

        if (missing.Length != 0)
        {
            Assert.Ignore($"""
                Published E2E tools are missing:
                {string.Join('\n', missing)}

                Publish them once on this machine before running the E2E test:
                dotnet publish PerfConverter/PerfConverter.csproj -c Release -r linux-x64 -p:NativeLib=Shared -p:PublishDir=PerfConverter/artifacts/e2e-dlfilter/
                dotnet publish CLI/CLI.csproj -c Release -r linux-x64 --self-contained false /p:UseAppHost=false /p:PerfConverterSourcePath="$(pwd)/PerfConverter/artifacts/e2e-dlfilter/PerfConverter.so"
                dotnet publish StackFixer/StackFixer.csproj -c Release -r linux-x64 --self-contained false /p:UseAppHost=false
                dotnet publish PerfToPerfetto/PerfToPerfetto.csproj -c Release -r linux-x64 --self-contained false /p:UseAppHost=false
                """);
        }

        return new PublishedTools(cliDll, stackFixerDll, perfToPerfettoDll);
    }

    static async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        bool assertSuccess = true)
    {
        Log($"start: {fileName} {string.Join(' ', arguments)}");
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
        Log($"exit {result.ExitCode}: {fileName} {string.Join(' ', arguments)}");
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

    readonly record struct PublishedTools(string CliDll, string StackFixerDll, string PerfToPerfettoDll);

    public sealed record IntelPtTarget(string Name, string SourceFile, string[] ExpectedSymbols)
    {
        public override string ToString()
            => Name;
    }

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
