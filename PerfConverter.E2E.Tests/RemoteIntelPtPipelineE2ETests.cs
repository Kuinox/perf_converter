using System.Diagnostics;
using System.Text;
using PerfToPerfetto;
using Plank.Reading;
using Plank.Schema;
using Temp.Schema;
using Temp.Schema.Schema;

namespace PerfConverter.E2E.Tests;

[Category("E2E")]
public sealed class RemoteIntelPtPipelineE2ETests
{
    [Test]
    public async Task IntelPtPipeline_ReconstructsKnownCStack()
    {
        var settings = RemoteSettings.FromEnvironment();
        if (!settings.Enabled)
            Assert.Ignore("Set PERFCONVERTER_E2E_ENABLE_REMOTE=1 to run the remote Intel PT E2E test.");

        using var local = new LocalTempDirectory("perfconverter-e2e-");
        var localOutputPath = Path.Combine(local.Path, "parquet_output");
        var localTracePath = Path.Combine(local.Path, "trace.perfetto-trace");

        await using var remote = await RemoteWorkspace.CreateAsync(settings);
        await remote.CopyToAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Targets", "e2e_stack_target.c"), "e2e_stack_target.c");

        await remote.RunAsync($"""
            set -euo pipefail
            repo={QuoteShell(settings.RemoteRepoPath)}
            work=$(pwd)
            cd "$repo"
            dotnet publish PerfConverter/PerfConverter.csproj -c Release -r linux-x64 -p:NativeLib=Shared -p:PublishDir=artifacts/e2e-dlfilter/
            dlfilter="$(pwd)/PerfConverter/artifacts/e2e-dlfilter/PerfConverter.so"
            test -s "$dlfilter"
            dotnet publish CLI/CLI.csproj -c Release -r linux-x64 --self-contained false /p:UseAppHost=false /p:PerfConverterSourcePath="$dlfilter"
            dotnet publish StackFixer/StackFixer.csproj -c Release -r linux-x64 --self-contained false /p:UseAppHost=false
            dotnet publish PerfToPerfetto/PerfToPerfetto.csproj -c Release -r linux-x64 --self-contained false /p:UseAppHost=false
            cd "$work"
            gcc -O0 -g -fno-omit-frame-pointer -fno-inline -no-pie e2e_stack_target.c -o e2e_stack_target
            perf record -m 64M -e intel_pt//u -o perf.data -- ./e2e_stack_target
            dotnet "$repo/CLI/bin/Release/net10.0/linux-x64/publish/CLI.dll" perf.data --output parquet_output --perf-args "-f --itrace=bei0ns --no-inline"
            dotnet "$repo/StackFixer/bin/Release/net10.0/linux-x64/publish/StackFixer.dll" parquet_output
            dotnet "$repo/PerfToPerfetto/bin/Release/net10.0/linux-x64/publish/PerfToPerfetto.dll" parquet_output trace.perfetto-trace
            test -s parquet_output/stack_frames.parquet
            test -s parquet_output/source_locations.parquet
            test -s trace.perfetto-trace
            """, timeout: TimeSpan.FromMinutes(8));

        await remote.CopyFromAsync("parquet_output", localOutputPath, recursive: true);
        await remote.CopyFromAsync("trace.perfetto-trace", localTracePath);

        var stackFrames = StackFrameReader.Read(Path.Combine(localOutputPath, "stack_frames.parquet"));
        var sourceLocations = SourceLocationReader.Read(Path.Combine(localOutputPath, "source_locations.parquet"));

        Assert.That(stackFrames, Is.Not.Empty);
        Assert.That(new FileInfo(localTracePath).Length, Is.GreaterThan(0));
        AssertStackEventsAreWellNested(stackFrames);
        AssertKnownCallChainIsNested(stackFrames, sourceLocations);
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

    static async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
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
        if (result.ExitCode != 0)
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

    readonly record struct RemoteSettings(string Remote, string KeyPath, string RemoteRepoPath, bool Enabled)
    {
        public static RemoteSettings FromEnvironment()
        {
            var enabled = Environment.GetEnvironmentVariable("PERFCONVERTER_E2E_ENABLE_REMOTE") == "1";
            var remote = Environment.GetEnvironmentVariable("PERFCONVERTER_E2E_REMOTE");
            var keyPath = Environment.GetEnvironmentVariable("PERFCONVERTER_E2E_KEY");
            var remoteRepoPath = Environment.GetEnvironmentVariable("PERFCONVERTER_E2E_REMOTE_REPO");

            if (!enabled)
                return new RemoteSettings(string.Empty, string.Empty, string.Empty, Enabled: false);

            if (string.IsNullOrWhiteSpace(remote))
                Assert.Ignore("Set PERFCONVERTER_E2E_REMOTE to run the remote Intel PT E2E test.");
            if (string.IsNullOrWhiteSpace(keyPath))
                Assert.Ignore("Set PERFCONVERTER_E2E_KEY to run the remote Intel PT E2E test.");
            if (string.IsNullOrWhiteSpace(remoteRepoPath))
                Assert.Ignore("Set PERFCONVERTER_E2E_REMOTE_REPO to the already-cloned remote repository path.");

            return new RemoteSettings(remote, keyPath, remoteRepoPath, Enabled: true);
        }
    }

    sealed class RemoteWorkspace : IAsyncDisposable
    {
        readonly RemoteSettings _settings;
        bool _disposed;

        RemoteWorkspace(RemoteSettings settings, string path)
        {
            _settings = settings;
            Path = path;
        }

        public string Path { get; }

        public static async Task<RemoteWorkspace> CreateAsync(RemoteSettings settings)
        {
            var result = await RunSshAsync(settings, "mktemp -d /tmp/perfconverter-e2e.XXXXXX");
            return new RemoteWorkspace(settings, result.StandardOutput.Trim());
        }

        public Task RunAsync(string script, TimeSpan? timeout = null)
            => RunSshAsync(_settings, $"cd {QuoteShell(Path)} && {script}", timeout);

        public Task CopyToAsync(string localPath, string remoteRelativePath)
            => RemoteIntelPtPipelineE2ETests.RunAsync(
                "scp",
                ScpArguments(_settings, localPath, $"{_settings.Remote}:{QuoteScp(Path + "/" + remoteRelativePath)}"));

        public Task CopyFromAsync(string remoteRelativePath, string localPath, bool recursive = false)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(localPath))!);
            var arguments = ScpArguments(_settings, $"{_settings.Remote}:{QuoteScp(Path + "/" + remoteRelativePath)}", localPath, recursive);
            return RemoteIntelPtPipelineE2ETests.RunAsync("scp", arguments, timeout: TimeSpan.FromMinutes(2));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;
            await RunSshAsync(_settings, $"rm -rf {QuoteShell(Path)}");
        }

        static Task<CommandResult> RunSshAsync(RemoteSettings settings, string command, TimeSpan? timeout = null)
            => RemoteIntelPtPipelineE2ETests.RunAsync("ssh", SshArguments(settings, command), timeout: timeout);

        static IReadOnlyList<string> SshArguments(RemoteSettings settings, string command)
            =>
            [
                "-i",
                settings.KeyPath,
                "-o",
                "BatchMode=yes",
                "-o",
                "ConnectTimeout=10",
                settings.Remote,
                command
            ];

        static IReadOnlyList<string> ScpArguments(RemoteSettings settings, string from, string to, bool recursive = false)
        {
            var arguments = new List<string>
            {
                "-i",
                settings.KeyPath,
                "-o",
                "BatchMode=yes",
                "-o",
                "ConnectTimeout=10"
            };

            if (recursive)
                arguments.Add("-r");

            arguments.Add(from);
            arguments.Add(to);
            return arguments;
        }
    }

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

    static string QuoteShell(string value)
        => "'" + value.Replace("'", "'\"'\"'") + "'";

    static string QuoteScp(string value)
        => value.Replace(" ", "\\ ");

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
