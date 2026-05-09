using System.Diagnostics;

namespace PerfCapture;

public sealed class PerfCommandRunner
{
    public async Task<PerfCommandResult> RunAsync(
        PerfCommandPlan command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var process = new Process();
        process.StartInfo = CreateStartInfo(command);

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new PerfCommandResult
        {
            Command = command,
            ExitCode = process.ExitCode,
            StandardOutput = await stdoutTask.ConfigureAwait(false),
            StandardError = await stderrTask.ConfigureAwait(false)
        };
    }

    internal static ProcessStartInfo CreateStartInfo(PerfCommandPlan command, bool redirectStandardInput = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName.Value,
            UseShellExecute = false,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (command.WorkingDirectory is not null)
            startInfo.WorkingDirectory = command.WorkingDirectory;

        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var (key, value) in command.Environment)
        {
            if (value is null)
                startInfo.Environment.Remove(key);
            else
                startInfo.Environment[key] = value;
        }

        return startInfo;
    }
}
