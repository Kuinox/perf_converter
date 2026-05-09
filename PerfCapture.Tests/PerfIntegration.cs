using PerfCapture;

namespace PerfCapture.Tests;

static class PerfIntegration
{
    public static async Task RequirePerfAsync()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Real perf integration tests require Linux.");

        var result = await RunCommandAsync("perf", ["--version"]);
        if (result.ExitCode != 0)
            Assert.Ignore($"perf is unavailable: {result.StandardError}");
    }

    public static async Task RequireIntelPtAsync()
    {
        await RequirePerfAsync();

        var result = await RunCommandAsync("perf", ["list", "intel_pt"]);
        if (result.ExitCode != 0 || !CombinedOutput(result).Contains("intel_pt", StringComparison.Ordinal))
            Assert.Ignore("intel_pt is not available on this machine.");
    }

    public static async Task<PerfCaptureRunResult> RunCaptureOrIgnorePermissionAsync(PerfCaptureSpec spec)
    {
        var result = await new PerfCaptureRunner().RunAsync(spec);
        if (!result.Succeeded && IsPermissionFailure(result.RecordResult))
            Assert.Ignore(result.RecordResult.StandardError);

        return result;
    }

    public static async Task AssertPerfDataAsync(
        string outputPath,
        string expectedEvent,
        IReadOnlyList<string>? expectedScriptFragments = null,
        IReadOnlyList<string>? scriptArguments = null,
        string? expectedScriptEvent = null)
    {
        Assert.That(File.Exists(outputPath), Is.True);
        Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));

        var evlist = await RunCommandAsync("perf", ["evlist", "-i", outputPath]);
        Assert.That(evlist.ExitCode, Is.EqualTo(0), evlist.StandardError);
        Assert.That(CombinedOutput(evlist), Does.Contain(expectedEvent));

        var arguments = new List<string> { "script" };
        if (scriptArguments is not null)
            arguments.AddRange(scriptArguments);
        arguments.Add("-i");
        arguments.Add(outputPath);

        var script = await RunCommandAsync("perf", arguments);
        Assert.That(script.ExitCode, Is.EqualTo(0), script.StandardError);
        Assert.That(script.StandardOutput, Does.Contain(expectedScriptEvent ?? expectedEvent));

        foreach (var fragment in expectedScriptFragments ?? [])
            Assert.That(script.StandardOutput, Does.Contain(fragment));
    }

    public static Task<PerfCommandResult> RunCommandAsync(CommandExecutable fileName, IReadOnlyList<string> arguments)
    {
        return new PerfCommandRunner().RunAsync(new PerfCommandPlan
        {
            FileName = fileName,
            Arguments = arguments
        });
    }

    public static bool IsPermissionFailure(PerfCommandResult result)
    {
        var output = CombinedOutput(result);
        return output.Contains("No permission", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("perf_event_paranoid", StringComparison.OrdinalIgnoreCase);
    }

    static string CombinedOutput(PerfCommandResult result) => result.StandardOutput + result.StandardError;
}
