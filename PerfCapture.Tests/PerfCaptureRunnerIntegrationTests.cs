using PerfCapture;

namespace PerfCapture.Tests;

[Category("Integration")]
public sealed class PerfCaptureRunnerIntegrationTests
{
    [Test]
    public async Task RunAsync_CommandCapture_ExecutesTargetAndDecodesSamples()
    {
        await PerfIntegration.RequirePerfAsync();

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "perf.data");
            var markerPath = Path.Combine(tempDirectory.FullName, "target-ran.txt");
            var environmentPath = Path.Combine(tempDirectory.FullName, "env.txt");
            var workingDirectoryPath = Path.Combine(tempDirectory.FullName, "cwd.txt");
            var spec = new CommandCaptureSpec
            {
                OutputPath = outputPath,
                Event = PerfEventSpec.CpuClock(),
                Target = new CommandTarget(
                    "/bin/sh",
                    [
                        "-c",
                        "echo ran > \"$1\"; printf '%s' \"$PERFCAPTURE_TEST_VALUE\" > \"$2\"; pwd > \"$3\"; i=0; while [ \"$i\" -lt 5000000 ]; do i=$((i+1)); done",
                        "sh",
                        markerPath,
                        environmentPath,
                        workingDirectoryPath
                    ],
                    WorkingDirectory: tempDirectory.FullName,
                    Environment: new Dictionary<string, string?> { ["PERFCAPTURE_TEST_VALUE"] = "visible" })
            };

            var result = await PerfIntegration.RunCaptureOrIgnorePermissionAsync(spec);

            Assert.That(result.RecordResult.ExitCode, Is.EqualTo(0), result.RecordResult.StandardError);
            Assert.That(File.ReadAllText(markerPath).Trim(), Is.EqualTo("ran"));
            Assert.That(File.ReadAllText(environmentPath), Is.EqualTo("visible"));
            Assert.That(File.ReadAllText(workingDirectoryPath).Trim(), Is.EqualTo(tempDirectory.FullName));
            Assert.That(result.RecordResult.StandardError, Does.Contain("Captured and wrote"));

            await PerfIntegration.AssertPerfDataAsync(outputPath, "cpu-clock", [" sh "]);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_ProcessAttachWithDuration_DecodesTargetSamples()
    {
        await PerfIntegration.RequirePerfAsync();

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        using var targetProcess = StartBusyShell();
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "process.data");
            var spec = new AttachCaptureSpec
            {
                OutputPath = outputPath,
                Target = new ProcessTarget(targetProcess.Id),
                Lifetime = PerfCaptureLifetime.ForDuration(TimeSpan.FromMilliseconds(500))
            };

            var result = await PerfIntegration.RunCaptureOrIgnorePermissionAsync(spec);

            Assert.That(result.RecordResult.ExitCode, Is.EqualTo(0), result.RecordResult.StandardError);
            await PerfIntegration.AssertPerfDataAsync(outputPath, "cpu-clock", [" sh "]);
        }
        finally
        {
            KillProcess(targetProcess);
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_ThreadAttachWithDuration_DecodesTargetSamples()
    {
        await PerfIntegration.RequirePerfAsync();

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        using var targetProcess = StartBusyShell();
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "thread.data");
            var spec = new AttachCaptureSpec
            {
                OutputPath = outputPath,
                Target = new ThreadTarget(targetProcess.Id),
                Lifetime = PerfCaptureLifetime.ForDuration(TimeSpan.FromMilliseconds(500))
            };

            var result = await PerfIntegration.RunCaptureOrIgnorePermissionAsync(spec);

            Assert.That(result.RecordResult.ExitCode, Is.EqualTo(0), result.RecordResult.StandardError);
            await PerfIntegration.AssertPerfDataAsync(outputPath, "cpu-clock", [" sh "]);
        }
        finally
        {
            KillProcess(targetProcess);
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_SystemWideWithDuration_DecodesSamples()
    {
        await PerfIntegration.RequirePerfAsync();

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "system.data");
            var spec = new AttachCaptureSpec
            {
                OutputPath = outputPath,
                Target = new SystemWideTarget("0"),
                Lifetime = PerfCaptureLifetime.ForDuration(TimeSpan.FromMilliseconds(300))
            };

            var result = await PerfIntegration.RunCaptureOrIgnorePermissionAsync(spec);

            Assert.That(result.RecordResult.ExitCode, Is.EqualTo(0), result.RecordResult.StandardError);
            await PerfIntegration.AssertPerfDataAsync(outputPath, "cpu-clock");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_PostProcessingStep_RunsPerfInject()
    {
        await PerfIntegration.RequirePerfAsync();

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "perf.data");
            var injectedPath = Path.Combine(tempDirectory.FullName, "perfjit.data");
            var spec = new CommandCaptureSpec
            {
                OutputPath = outputPath,
                Target = new CommandTarget("/bin/sh", ["-c", "i=0; while [ \"$i\" -lt 1000000 ]; do i=$((i+1)); done"]),
                PostProcessingSteps = [new PerfInjectJitSymbolsStep(injectedPath)]
            };

            var result = await PerfIntegration.RunCaptureOrIgnorePermissionAsync(spec);

            Assert.That(result.Succeeded, Is.True, result.RecordResult.StandardError);
            Assert.That(result.PostProcessingResults, Has.Count.EqualTo(1));
            Assert.That(result.PostProcessingResults[0].ExitCode, Is.EqualTo(0), result.PostProcessingResults[0].StandardError);
            await PerfIntegration.AssertPerfDataAsync(injectedPath, "cpu-clock");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task StartAsync_ControlledStandardInputSession_SamplesOnlyAfterEnable()
    {
        await PerfIntegration.RequirePerfAsync();

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        try
        {
            var disabledOutputPath = Path.Combine(tempDirectory.FullName, "controlled-disabled.data");
            var enabledOutputPath = Path.Combine(tempDirectory.FullName, "controlled-enabled.data");

            var disabledResult = await RunControlledBusyCaptureAsync(
                disabledOutputPath,
                enabledWindows: 0);
            var enabledResult = await RunControlledBusyCaptureAsync(
                enabledOutputPath,
                enabledWindows: 2);

            Assert.That(disabledResult.Succeeded, Is.True, disabledResult.RecordResult.StandardError);
            Assert.That(enabledResult.Succeeded, Is.True, enabledResult.RecordResult.StandardError);
            Assert.That(disabledResult.RecordResult.StandardError, Does.Contain("Events disabled"));
            Assert.That(disabledResult.RecordResult.StandardError, Does.Not.Contain("Events enabled"));
            Assert.That(CountOccurrences(enabledResult.RecordResult.StandardError, "Events enabled"), Is.EqualTo(2));
            Assert.That(CountOccurrences(enabledResult.RecordResult.StandardError, "Events disabled"), Is.GreaterThanOrEqualTo(3));

            Assert.That(await CountPerfScriptSamplesAsync(disabledOutputPath, "cpu-clock"), Is.Zero);
            Assert.That(await CountPerfScriptSamplesAsync(enabledOutputPath, "cpu-clock"), Is.GreaterThan(0));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_IntelPtFilterAddressFilter_DecodesBranchSamples()
    {
        await PerfIntegration.RequireIntelPtAsync();

        var loaderPath = GetLoaderPath();
        if (loaderPath is null)
            Assert.Ignore("Could not find ld-linux for a stable address-filter symbol.");

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "intelpt.data");
            var spec = new CommandCaptureSpec
            {
                OutputPath = outputPath,
                Event = IntelPtEventSpec.UserOnly(),
                Buffer = PerfCaptureBufferPolicy.Growing(
                    PerfBufferSize.Mebibytes(1),
                    PerfBufferSize.Mebibytes(8)),
                AddressFilters = [PerfAddressFilter.Filter("_start", objectFile: loaderPath)],
                Target = new CommandTarget("/bin/true", [])
            };

            var result = await PerfIntegration.RunCaptureOrIgnorePermissionAsync(spec);

            Assert.That(result.RecordResult.ExitCode, Is.EqualTo(0), result.RecordResult.StandardError);
            await PerfIntegration.AssertPerfDataAsync(
                outputPath,
                "intel_pt",
                ["branches:u", " true "],
                ["--itrace=b"],
                expectedScriptEvent: "branches:u");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_IntelPtTraceStopAddressFilter_DecodesBranchSamples()
    {
        await PerfIntegration.RequireIntelPtAsync();

        var libcPath = GetLibcPath();
        if (libcPath is null)
            Assert.Ignore("Could not find libc for a stable address-filter symbol.");

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "intelpt-tracestop.data");
            var spec = new CommandCaptureSpec
            {
                OutputPath = outputPath,
                Event = IntelPtEventSpec.UserOnly(),
                Buffer = PerfCaptureBufferPolicy.Growing(
                    PerfBufferSize.Mebibytes(1),
                    PerfBufferSize.Mebibytes(8)),
                AddressFilters = [PerfAddressFilter.TraceStop("exit", objectFile: libcPath)],
                Target = new CommandTarget("/bin/true", [])
            };

            var result = await PerfIntegration.RunCaptureOrIgnorePermissionAsync(spec);

            Assert.That(result.RecordResult.ExitCode, Is.EqualTo(0), result.RecordResult.StandardError);
            await PerfIntegration.AssertPerfDataAsync(
                outputPath,
                "intel_pt",
                ["branches:u", " true "],
                ["--itrace=b"],
                expectedScriptEvent: "branches:u");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_IntelPtStopAddressFilter_DecodesBranchSamples()
    {
        await PerfIntegration.RequireIntelPtAsync();

        var loaderPath = GetLoaderPath();
        if (loaderPath is null)
            Assert.Ignore("Could not find ld-linux for a stable address-filter symbol.");

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "intelpt-stop.data");
            var spec = new CommandCaptureSpec
            {
                OutputPath = outputPath,
                Event = IntelPtEventSpec.UserOnly(),
                Buffer = PerfCaptureBufferPolicy.Growing(
                    PerfBufferSize.Mebibytes(1),
                    PerfBufferSize.Mebibytes(8)),
                AddressFilters = [PerfAddressFilter.Stop("_dl_start", 16, loaderPath)],
                Target = new CommandTarget("/bin/true", [])
            };

            var plan = PerfCapturePlanBuilder.Build(spec);
            Assert.That(plan.RecordCommand.Arguments, Does.Contain($"stop _dl_start / 16 @{loaderPath}"));

            var result = await PerfIntegration.RunCaptureOrIgnorePermissionAsync(spec);

            Assert.That(result.RecordResult.ExitCode, Is.EqualTo(0), result.RecordResult.StandardError);
            await PerfIntegration.AssertPerfDataAsync(
                outputPath,
                "intel_pt",
                ["branches:u", " true "],
                ["--itrace=b"],
                expectedScriptEvent: "branches:u");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RunAsync_IntelPtStartAddressFilter_DecodesBranchSamplesOrReportsUnsupported()
    {
        await PerfIntegration.RequireIntelPtAsync();

        var loaderPath = GetLoaderPath();
        if (loaderPath is null)
            Assert.Ignore("Could not find ld-linux for a stable address-filter symbol.");

        var tempDirectory = Directory.CreateTempSubdirectory("perfcapture-tests-");
        try
        {
            var outputPath = Path.Combine(tempDirectory.FullName, "intelpt-start.data");
            var spec = new CommandCaptureSpec
            {
                OutputPath = outputPath,
                Event = IntelPtEventSpec.UserOnly(),
                Buffer = PerfCaptureBufferPolicy.Growing(
                    PerfBufferSize.Mebibytes(1),
                    PerfBufferSize.Mebibytes(8)),
                AddressFilters = [PerfAddressFilter.Start("_start", 16, loaderPath)],
                Target = new CommandTarget("/bin/true", [])
            };

            var plan = PerfCapturePlanBuilder.Build(spec);
            Assert.That(plan.RecordCommand.Arguments, Does.Contain($"start _start / 16 @{loaderPath}"));

            var result = await new PerfCaptureRunner().RunAsync(spec);
            if (!result.Succeeded && PerfIntegration.IsPermissionFailure(result.RecordResult))
                Assert.Ignore(result.RecordResult.StandardError);
            if (!result.Succeeded && IsUnsupportedAddressFilter(result.RecordResult))
                Assert.Pass(result.RecordResult.StandardError);

            Assert.That(result.RecordResult.ExitCode, Is.EqualTo(0), result.RecordResult.StandardError);
            await PerfIntegration.AssertPerfDataAsync(
                outputPath,
                "intel_pt",
                ["branches:u", " true "],
                ["--itrace=b"],
                expectedScriptEvent: "branches:u");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Test]
    public void RunAsync_AttachManualLifetime_ThrowsInsteadOfHanging()
    {
        var spec = new AttachCaptureSpec
        {
            OutputPath = "perf.data",
            Target = new ProcessTarget(Environment.ProcessId),
            Lifetime = PerfCaptureLifetime.UntilStopped()
        };

        Assert.ThrowsAsync<NotSupportedException>(() => new PerfCaptureRunner().RunAsync(spec));
    }

    static System.Diagnostics.Process StartBusyShell()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("while :; do :; done");

        return System.Diagnostics.Process.Start(startInfo) ??
               throw new InvalidOperationException("Failed to start busy shell.");
    }

    static async Task<PerfCaptureRunResult> RunControlledBusyCaptureAsync(string outputPath, int enabledWindows = 0)
    {
        var spec = new CommandCaptureSpec
        {
            OutputPath = outputPath,
            Target = new CommandTarget("/bin/sh", ["-c", "while :; do :; done"])
        };

        await using var session = await new PerfCaptureSessionRunner().StartAsync(spec);

        await Task.Delay(200);
        if (session.HasExited)
        {
            var earlyResult = await session.WaitForExitAsync();
            if (PerfIntegration.IsPermissionFailure(earlyResult.RecordResult))
                Assert.Ignore(earlyResult.RecordResult.StandardError);

            Assert.Fail(earlyResult.RecordResult.StandardError);
        }

        for (var i = 0; i < enabledWindows; i++)
        {
            await session.EnableAsync();
            await Task.Delay(250);
            await session.DisableAsync();
            await Task.Delay(150);
        }

        if (enabledWindows == 0)
            await Task.Delay(650);

        await session.StopAsync();
        return await session.WaitForExitAsync();
    }

    static async Task<int> CountPerfScriptSamplesAsync(string outputPath, string eventName)
    {
        Assert.That(File.Exists(outputPath), Is.True);

        var script = await PerfIntegration.RunCommandAsync("perf", ["script", "-i", outputPath]);
        Assert.That(script.ExitCode, Is.EqualTo(0), script.StandardError);

        return script.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains(eventName, StringComparison.Ordinal));
    }

    static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var startIndex = 0;

        while (true)
        {
            var index = value.IndexOf(fragment, startIndex, StringComparison.Ordinal);
            if (index < 0)
                return count;

            count++;
            startIndex = index + fragment.Length;
        }
    }

    static void KillProcess(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    static bool IsUnsupportedAddressFilter(PerfCommandResult result)
    {
        var output = result.StandardOutput + result.StandardError;
        return output.Contains("Operation not supported", StringComparison.OrdinalIgnoreCase) &&
               output.Contains("failed to set filter", StringComparison.OrdinalIgnoreCase);
    }

    static string? GetLoaderPath()
    {
        foreach (var path in new[]
        {
            "/usr/lib/x86_64-linux-gnu/ld-linux-x86-64.so.2",
            "/lib64/ld-linux-x86-64.so.2",
            "/lib/x86_64-linux-gnu/ld-linux-x86-64.so.2"
        })
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    static string? GetLibcPath()
    {
        foreach (var path in new[]
        {
            "/usr/lib/x86_64-linux-gnu/libc.so.6",
            "/lib/x86_64-linux-gnu/libc.so.6",
            "/lib64/libc.so.6"
        })
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
