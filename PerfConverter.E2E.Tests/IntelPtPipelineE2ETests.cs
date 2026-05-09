using System.Diagnostics;
using PerfCapture;
using System.Text;
using Plank.Reading;
using Plank.Schema;
using Temp.Schema;
using Temp.Schema.Schema;

namespace PerfConverter.E2E.Tests;

[Category("E2E")]
public sealed class IntelPtPipelineE2ETests
{
    const ulong SampleMatchToleranceNs = 5_000_000;

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
        var perfData = Path.Combine(work.Path, "perf.data");
        var outputPath = Path.Combine(work.Path, "parquet_output");
        var tracePath = Path.Combine(work.Path, "trace.perfetto-trace");

        var tools = FindPublishedTools(repoRoot);
        Log($"CLI={tools.CliDll}");
        Log($"StackFixer={tools.StackFixerDll}");
        Log($"PerfToPerfetto={tools.PerfToPerfettoDll}");

        Log("compiling C target");
        await RunAsync(
            "gcc",
            [
                "-O0", "-g", "-fno-omit-frame-pointer", "-fno-inline",
                "-DE2E_SPIN_SCALE=10", "-DE2E_WARMUP_ROUNDS=1",
                targetSource, "-o", targetBinary
            ],
            work.Path);

        Log("recording mixed Intel PT and sampled callgraph data");
        var captureSpec = new CommandCaptureSpec
        {
            OutputPath = perfData,
            Event = IntelPtEventSpec.UserOnly(),
            AdditionalEvents = [PerfEventSpec.Named("cpu-clock:u")],
            SampleFrequency = 2000,
            CallGraph = PerfCallGraphMode.FramePointer(),
            Buffer = PerfCaptureBufferPolicy.Growing(auxBuffer: PerfBufferSize.Mebibytes(64)),
            Target = new CommandTarget(targetBinary, [], work.Path)
        };

        using (var captureCts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
        {
            var perfRecord = await new PerfCaptureRunner().RunAsync(captureSpec, captureCts.Token);
            if (!perfRecord.Succeeded && IsPermissionFailure(perfRecord.RecordResult))
                Assert.Ignore(perfRecord.RecordResult.StandardError);

            Assert.That(perfRecord.Succeeded, Is.True, perfRecord.RecordResult.StandardError);
        }

        var sampledScript = await RunPerfCommandAsync(
            ["script", "--ns", "-i", perfData],
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
        var sampledStacks = ParseSampledStacks(sampledScript.StandardOutput, target.ExpectedSymbols);
        var sampledReportPath = WriteSampledStackReport(target, sampledStacks, stackFrames, sourceLocations);
        TestContext.AddTestAttachment(sampledReportPath, $"{target.Name} sampled stack comparison");
        CopyInspectionArtifacts(repoRoot, target, sampledReportPath, tracePath);

        Assert.That(stackFrames, Is.Not.Empty);
        Assert.That(new FileInfo(tracePath).Length, Is.GreaterThan(0));
        AssertStackEventsAreWellNested(stackFrames);
        AssertExpectedSymbols(target, stackFrames, sourceLocations);
        AssertSampledStacksMatchReconstruction(target, stackFrames, sourceLocations, sampledStacks);
    }

    static void Log(string message)
        => TestContext.Progress.WriteLine($"[{DateTimeOffset.UtcNow:O}] {message}");

    static async Task RequireLinuxToolingAsync()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Intel PT E2E tests must run on the Linux perf machine.");

        await RequireCommandAsync("dotnet", ["--info"]);
        await RequireCommandAsync("gcc", ["--version"]);
        await RequirePerfCommandAsync(["--version"]);

        var intelPt = await RequirePerfCommandAsync(["list", "intel_pt"]);
        if (!CombinedOutput(intelPt).Contains("intel_pt", StringComparison.Ordinal))
            Assert.Ignore("intel_pt is not available on this machine.");
    }

    static async Task RequireCommandAsync(string fileName, IReadOnlyList<string> arguments)
    {
        var result = await RunAsync(fileName, arguments, assertSuccess: false);
        if (result.ExitCode != 0)
            Assert.Ignore($"{fileName} is unavailable: {CombinedOutput(result)}");
    }

    static async Task<PerfCommandResult> RequirePerfCommandAsync(IReadOnlyList<string> arguments)
    {
        var result = await new PerfCommandRunner().RunAsync(new PerfCommandPlan
        {
            FileName = "perf",
            Arguments = arguments
        });

        if (result.ExitCode != 0)
            Assert.Ignore($"perf is unavailable: {result.StandardError}");

        return result;
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
        IReadOnlyList<SampledStack> samples)
    {
        Assert.That(samples, Is.Not.Empty, $"{target.Name}: perf sampling did not capture any expected target stack.");

        var framesBySymbol = BuildFramesBySymbol(frames, sourceLocations, target.ExpectedSymbols);
        foreach (var sample in samples)
        {
            var reconstructed = FindRepresentativeReconstructedStack(sample, framesBySymbol);
            Assert.That(
                reconstructed.All(static frame => frame is not null),
                Is.True,
                $"{target.Name}: sampled stack at time={sample.Time} ip={sample.Ip ?? "?"} was not reconstructed at the same timestamp. sampled={string.Join(" -> ", sample.TargetSymbols)} reconstructed={FormatReconstructedStack(sample.TargetSymbols, reconstructed)}");
        }

        foreach (var symbol in samples.SelectMany(static sample => sample.TargetSymbols).Distinct(StringComparer.Ordinal))
        {
            Assert.That(
                framesBySymbol.GetValueOrDefault(symbol),
                Is.Not.Null.And.Not.Empty,
                $"{target.Name}: sampled stack included {symbol}, but Intel PT reconstruction emitted no matching frame.");
        }

        var observedPairs = samples
            .SelectMany(static sample => sample.TargetSymbols.Zip(sample.TargetSymbols.Skip(1), static (parent, child) => (Parent: parent, Child: child)))
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

    static string WriteSampledStackReport(
        IntelPtTarget target,
        IReadOnlyList<SampledStack> samples,
        IReadOnlyList<StackFrame> frames,
        IReadOnlyDictionary<ulong, SourceLocation> sourceLocations)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{target.Name}-sampled-stack-report.md");
        var framesBySymbol = BuildFramesBySymbol(frames, sourceLocations, target.ExpectedSymbols);
        var uniqueSamples = samples
            .GroupBy(static sample => string.Join(" -> ", sample.FullSymbols), StringComparer.Ordinal)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        writer.WriteLine($"# {target.Name} sampled stack comparison");
        writer.WriteLine();
        writer.WriteLine($"Sampled stack count: {samples.Count}");
        writer.WriteLine($"Unique sampled stack count: {uniqueSamples.Length}");
        writer.WriteLine();

        if (uniqueSamples.Length == 0)
        {
            writer.WriteLine("No sampled stacks containing the expected target symbols were captured.");
            return path;
        }

        writer.WriteLine("| Count | Sampled by profiler | Reconstructed by Intel PT |");
        writer.WriteLine("| ---: | --- | --- |");

        foreach (var group in uniqueSamples)
        {
            var sample = group.First();
            var reconstructed = FindRepresentativeReconstructedStack(sample, framesBySymbol);
            writer.WriteLine(
                $"| {group.Count()} | {EscapeMarkdownCell(FormatStack(sample))} | {EscapeMarkdownCell(FormatReconstructedStack(sample.TargetSymbols, reconstructed))} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Reconstructed frame counts");
        writer.WriteLine();
        writer.WriteLine("| Symbol | Frames |");
        writer.WriteLine("| --- | ---: |");
        foreach (var symbol in target.ExpectedSymbols)
            writer.WriteLine($"| `{symbol}` | {framesBySymbol.GetValueOrDefault(symbol)?.Count ?? 0} |");

        writer.WriteLine();
        writer.WriteLine("## All sampled stacks");
        writer.WriteLine();
        writer.WriteLine("| Index | Sampled by profiler | Reconstructed by Intel PT |");
        writer.WriteLine("| ---: | --- | --- |");
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var reconstructed = FindRepresentativeReconstructedStack(sample, framesBySymbol);
            writer.WriteLine(
                $"| {index + 1} | {EscapeMarkdownCell(FormatStack(sample))} | {EscapeMarkdownCell(FormatReconstructedStack(sample.TargetSymbols, reconstructed))} |");
        }

        return path;
    }

    static void CopyInspectionArtifacts(string repoRoot, IntelPtTarget target, string sampledReportPath, string tracePath)
    {
        var outputDirectory = Path.Combine(repoRoot, "artifacts", "perfconverter-e2e-reports", target.Name);
        Directory.CreateDirectory(outputDirectory);
        File.Copy(sampledReportPath, Path.Combine(outputDirectory, Path.GetFileName(sampledReportPath)), overwrite: true);
        File.Copy(tracePath, Path.Combine(outputDirectory, "trace.perfetto-trace"), overwrite: true);
    }

    static IReadOnlyList<StackFrame?> FindRepresentativeReconstructedStack(
        SampledStack sample,
        IReadOnlyDictionary<string, List<StackFrame>> framesBySymbol)
    {
        var symbols = sample.TargetSymbols;
        var chain = new StackFrame?[symbols.Count];
        if (symbols.Count == 0)
            return chain;

        if (symbols.Count == 1)
        {
            chain[0] = FindFrameAtSampleTime(framesBySymbol.GetValueOrDefault(symbols[0]), sample.Time);
            return chain;
        }

        var leafSymbol = symbols[^1];
        if (!framesBySymbol.TryGetValue(leafSymbol, out var leafFrames))
            return chain;

        foreach (var leaf in leafFrames.Where(frame => ContainsSampleTime(frame, sample.Time)).OrderBy(static frame => frame.StartTime).ThenBy(static frame => frame.StartTrace))
        {
            chain[^1] = leaf;
            var child = leaf;
            var matched = true;

            for (var index = symbols.Count - 2; index >= 0; index--)
            {
                if (!framesBySymbol.TryGetValue(symbols[index], out var parentFrames))
                {
                    matched = false;
                    break;
                }

                var parent = parentFrames
                    .Where(parentFrame => parentFrame.Depth < child.Depth && ContainsSampleTime(parentFrame, sample.Time) && Overlaps(parentFrame, child))
                    .OrderByDescending(static parentFrame => parentFrame.Depth)
                    .ThenBy(static parentFrame => parentFrame.StartTime)
                    .Select(static parentFrame => (StackFrame?)parentFrame)
                    .FirstOrDefault();

                if (parent is null)
                {
                    matched = false;
                    break;
                }

                chain[index] = parent;
                child = parent.Value;
            }

            if (matched)
                return chain;
        }

        for (var index = 0; index < symbols.Count; index++)
            chain[index] = FindFrameAtSampleTime(framesBySymbol.GetValueOrDefault(symbols[index]), sample.Time);

        return chain;
    }

    static StackFrame? FindFrameAtSampleTime(IReadOnlyList<StackFrame>? frames, ulong time)
        => frames?
            .Where(frame => ContainsSampleTime(frame, time))
            .OrderBy(static frame => frame.Depth)
            .ThenBy(static frame => frame.StartTime)
            .Select(static frame => (StackFrame?)frame)
            .FirstOrDefault();

    static bool ContainsSampleTime(StackFrame frame, ulong time)
    {
        var start = frame.StartTime > SampleMatchToleranceNs
            ? frame.StartTime - SampleMatchToleranceNs
            : 0;
        var end = frame.EndTime + SampleMatchToleranceNs;
        return start <= time && time <= end;
    }

    static string FormatStack(SampledStack sample)
        => $"time={sample.Time} ip={sample.Ip ?? "?"}<br>" +
           string.Join("<br>", sample.FullSymbols.Select(static symbol => $"`{symbol}`"));

    static string FormatReconstructedStack(IReadOnlyList<string> sample, IReadOnlyList<StackFrame?> frames)
        => string.Join("<br>", sample.Zip(frames, static (symbol, frame) => frame is null
            ? "`missing`"
            : $"`{symbol}` depth={frame.Value.Depth} start={frame.Value.StartTime} end={frame.Value.EndTime} id={frame.Value.FrameId}"));

    static string EscapeMarkdownCell(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    static IReadOnlyList<SampledStack> ParseSampledStacks(string perfScript, IReadOnlyCollection<string> expectedSymbols)
    {
        var expected = expectedSymbols.ToHashSet(StringComparer.Ordinal);
        var samples = new List<SampledStack>();
        var currentFull = new List<string>();
        var currentTarget = new List<string>();
        ulong currentTime = 0;
        string? currentIp = null;
        var acceptCurrentSample = false;

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
                var header = ParsePerfScriptHeader(line);
                acceptCurrentSample = header.IsSampledEvent;
                if (!acceptCurrentSample)
                    continue;

                currentTime = header.Time;
                currentIp = header.Ip;
                if (header.Symbol is not null)
                    AddSymbol(header.Symbol);
                continue;
            }

            if (!acceptCurrentSample)
                continue;

            var symbol = TryParsePerfScriptSymbol(line);
            if (symbol is not null)
                AddSymbol(symbol);
        }

        AddCurrentSample();
        return samples;

        void AddCurrentSample()
        {
            if (currentTarget.Count != 0)
                samples.Add(new SampledStack(
                    currentTime,
                    currentIp,
                    currentFull.AsEnumerable().Reverse().ToArray(),
                    currentTarget.AsEnumerable().Reverse().ToArray()));
            currentFull.Clear();
            currentTarget.Clear();
            currentTime = 0;
            currentIp = null;
            acceptCurrentSample = false;
        }

        void AddSymbol(string symbol)
        {
            if (currentFull.LastOrDefault() != symbol)
                currentFull.Add(symbol);

            if (expected.Contains(symbol) && currentTarget.LastOrDefault() != symbol)
                currentTarget.Add(symbol);
        }
    }

    static (ulong Time, string? Ip, string? Symbol, bool IsSampledEvent) ParsePerfScriptHeader(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ulong time = 0;
        string? ip = null;
        string? symbol = null;
        var seenTime = false;
        var seenEvent = false;

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].EndsWith(":", StringComparison.Ordinal) &&
                TryParsePerfScriptTime(parts[i][..^1], out var parsedTime))
            {
                time = parsedTime;
                seenTime = true;
                continue;
            }

            if (seenTime && parts[i].EndsWith(":", StringComparison.Ordinal))
            {
                seenEvent = true;
                continue;
            }

            if (!seenEvent || !line.Contains("cpu-clock", StringComparison.Ordinal) || !parts[i].All(Uri.IsHexDigit))
                continue;

            ip ??= parts[i];
            if (i + 1 < parts.Length)
                symbol ??= NormalizePerfScriptSymbol(parts[i + 1]);
        }

        return (time, ip, symbol, line.Contains("cpu-clock", StringComparison.Ordinal));
    }

    static bool TryParsePerfScriptTime(string value, out ulong nanoseconds)
    {
        nanoseconds = 0;
        var dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 ||
            !ulong.TryParse(value[..dot], out var seconds) ||
            !ulong.TryParse(value[(dot + 1)..], out var fraction))
            return false;

        var fractionDigits = value.Length - dot - 1;
        while (fractionDigits < 9)
        {
            fraction *= 10;
            fractionDigits++;
        }

        while (fractionDigits > 9)
        {
            fraction /= 10;
            fractionDigits--;
        }

        nanoseconds = checked(seconds * 1_000_000_000UL + fraction);
        return true;
    }

    static string? TryParsePerfScriptSymbol(string line)
    {
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].All(Uri.IsHexDigit))
            return null;

        var symbol = parts[1];
        return NormalizePerfScriptSymbol(symbol);
    }

    static string? NormalizePerfScriptSymbol(string symbol)
    {
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

            foreach (var (expectedSymbol, matches) in result)
            {
                if (location.Symbol.Contains(expectedSymbol, StringComparison.Ordinal))
                    matches.Add(frame);
            }
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

    static async Task<PerfCommandResult> RunPerfCommandAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        Log($"start: perf {string.Join(' ', arguments)}");
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2));
        var result = await new PerfCommandRunner().RunAsync(new PerfCommandPlan
        {
            FileName = "perf",
            Arguments = arguments,
            WorkingDirectory = workingDirectory
        }, cts.Token);

        Log($"exit {result.ExitCode}: perf {string.Join(' ', arguments)}");
        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
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

    static bool IsPermissionFailure(PerfCommandResult result)
    {
        var output = result.StandardOutput + result.StandardError;
        return output.Contains("No permission", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("perf_event_paranoid", StringComparison.OrdinalIgnoreCase);
    }

    static string CombinedOutput(CommandResult result)
        => result.StandardOutput + result.StandardError;

    static string CombinedOutput(PerfCommandResult result)
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

    readonly record struct SampledStack(
        ulong Time,
        string? Ip,
        IReadOnlyList<string> FullSymbols,
        IReadOnlyList<string> TargetSymbols);

    readonly record struct StackEndpoint(
        ulong Time,
        ulong Trace,
        uint Depth,
        ulong FrameId,
        bool IsBegin,
        StackFrame Frame);
}
