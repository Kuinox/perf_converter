using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Linq;
using Temp.Schema;

namespace CLI;

internal class Program
{
    const int SigInt = 2;
    const int SigTerm = 15;

    [DllImport("libc", SetLastError = true)]
    static extern int kill(int pid, int sig);

    [DllImport("libc", SetLastError = true)]
    static extern int setpgid(int pid, int pgid);

    static async Task<int> Main(string[] args)
    {
        try
        {

            var rootCommand = new RootCommand("PerfConverter CLI - Helper tool for running perf with PerfConverter DLFilter");

            var inputFileArgument = new Argument<FileInfo>(
                name: "input-file",
                description: "Path to the perf data file");

            var perfArgsOption = new Option<string>(
                ["--perf-args", "-p"],
                getDefaultValue: () => "-f --itrace=bei0ns --no-inline -F tid",
                "Additional arguments to pass to perf script");

            var outputOption = new Option<DirectoryInfo>(
                ["--output", "-o"],
                getDefaultValue: () => new DirectoryInfo("parquet_output"),
                "Output directory for Parquet files");

            var dryRunOption = new Option<bool>(
                ["--dry-run", "-n"],
                "Show the command that would be executed without running it");

            rootCommand.AddArgument(inputFileArgument);
            rootCommand.AddOption(perfArgsOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(dryRunOption);

            rootCommand.SetHandler(async context =>
            {
                var inputFile = context.ParseResult.GetValueForArgument(inputFileArgument)!;
                var perfArgs = context.ParseResult.GetValueForOption(perfArgsOption)!;
                var outputDir = context.ParseResult.GetValueForOption(outputOption)!;
                var dryRun = context.ParseResult.GetValueForOption(dryRunOption);

                await RunPerfCommand(inputFile, perfArgs, outputDir, dryRun);
            });

            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> RunPerfCommand(FileInfo inputFile, string perfArgs, DirectoryInfo outputDir, bool dryRun)
    {
        if (!inputFile.Exists)
        {
            Console.Error.WriteLine($"Error: Input file '{inputFile.FullName}' does not exist.");
            return 1;
        }

        var dlFilterPath = Path.Combine(AppContext.BaseDirectory, "PerfConverter.so");

        if (!File.Exists(dlFilterPath))
        {
            Console.Error.WriteLine($"Error: PerfConverter.so not found at '{dlFilterPath}'.");
            return 1;
        }

        if (!outputDir.Exists)
            outputDir.Create();

        Environment.SetEnvironmentVariable("OUTPUT_DIRECTORY", outputDir.FullName);

        var perfCommand = $"perf script {perfArgs} -i {inputFile.FullName} --dlfilter {dlFilterPath}";

        if (dryRun)
        {
            Console.WriteLine("Would execute:");
            Console.WriteLine($"export OUTPUT_DIRECTORY=\"{outputDir.FullName}\"");
            Console.WriteLine(perfCommand);
            return 0;
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = "perf",
            Arguments = $"script {perfArgs} -i {inputFile.FullName} --dlfilter {dlFilterPath}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        processInfo.Environment["OUTPUT_DIRECTORY"] = outputDir.FullName;
        try
        {
            return await RunPerfWithMonitor(processInfo);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
    private static async Task<int> RunPerfWithMonitor(ProcessStartInfo processInfo)
    {
        var viewModel = new PerfMonitorViewModel();
        using var metricsPipeServer = PerfMetricsPipeServer.Create(processInfo, viewModel);

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Console.Error.WriteLine("Failed to start perf process.");
            return 1;
        }

        TryAssignUnixProcessGroup(process);

        viewModel.ProcessStartTime = DateTime.UtcNow;
        var chrono = Stopwatch.StartNew();
        var commandProcessor = new CommandProcessor(viewModel);
        var messageHandler = new MessageHandler(viewModel, commandProcessor);
        var exitTimeoutCts = new CancellationTokenSource();
        var stdoutDrained = false;
        var stderrDrained = false;
        var shutdownRequested = 0;
        Task? shutdownTask = null;

        Task RequestShutdownAsync()
        {
            if (Interlocked.Exchange(ref shutdownRequested, 1) != 0)
                return shutdownTask ?? Task.CompletedTask;

            viewModel.ShutdownRequested = true;
            shutdownTask = Task.Run(async () =>
            {
                try
                {
                    viewModel.StatusMessage = "Ctrl+C received, requesting graceful shutdown...";

                    if (process.HasExited)
                        return;

                    if (!TryRequestGracefulShutdown(process))
                    {
                        viewModel.StatusMessage = "Could not signal perf cleanly, forcing termination...";
                        process.Kill(true);
                        await process.WaitForExitAsync();
                        return;
                    }

                    using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    try
                    {
                        await process.WaitForExitAsync(shutdownTimeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited)
                        {
                            viewModel.StatusMessage = "SIGINT timed out, escalating to SIGTERM...";
                            TrySendUnixSignal(process, SigTerm, preferProcessGroup: true);
                        }
                    }

                    if (!process.HasExited)
                    {
                        using var terminateTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        try
                        {
                            await process.WaitForExitAsync(terminateTimeout.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            if (!process.HasExited)
                            {
                                viewModel.StatusMessage = "Graceful shutdown timed out, forcing termination...";
                                process.Kill(true);
                                await process.WaitForExitAsync();
                            }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited while shutdown was in flight.
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error killing process: {ex.Message}");
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(true);
                            await process.WaitForExitAsync();
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                }
            });

            return shutdownTask;
        }

        void RequestShutdown()
        {
            _ = RequestShutdownAsync();
        }

        // Set up Ctrl+C handler
        ConsoleCancelEventHandler cancelHandler = (sender, e) =>
        {
            e.Cancel = true;
            RequestShutdown();
        };
        Console.CancelKeyPress += cancelHandler;
        PosixSignalRegistration? sigIntRegistration = null;
        PosixSignalRegistration? sigTermRegistration = null;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            sigIntRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
            {
                ctx.Cancel = true;
                RequestShutdown();
            });

            sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
            {
                ctx.Cancel = true;
                RequestShutdown();
            });
        }

        // Set up process event handlers
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                messageHandler.ProcessOutputMessage(e.Data);
            }
            else
            {
                // e.Data is null when stdout is closed
                stdoutDrained = true;
                if (stdoutDrained && stderrDrained && process.HasExited)
                {
                    viewModel.PipesDrained = true;
                }
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                messageHandler.ProcessErrorMessage(e.Data);
            }
            else
            {
                // e.Data is null when stderr is closed
                stderrDrained = true;
                if (stdoutDrained && stderrDrained && process.HasExited)
                {
                    viewModel.PipesDrained = true;
                }
            }
        };

        process.EnableRaisingEvents = true;

        process.Exited += (sender, e) =>
        {
            viewModel.ProcessHasExited = true;
            viewModel.StatusMessage = viewModel.ExitMessageReceived
                ? "PerfConverter finished cleanup, waiting for pipes to drain..."
                : "Process completed, waiting for pipes to drain...";
            
            // Check if pipes are already drained
            if (stdoutDrained && stderrDrained)
            {
                viewModel.PipesDrained = true;
            }
            
            _ = Task.Run(async () =>
            {
                // Wait for pipes to drain, but with a reasonable timeout
                var maxWait = TimeSpan.FromSeconds(5);
                var start = DateTime.UtcNow;
                
                while (!viewModel.PipesDrained && (DateTime.UtcNow - start) < maxWait)
                {
                    try
                    {
                        await Task.Delay(100);
                    }
                    catch (ObjectDisposedException)
                    {
                        // If we get disposed, just break out
                        break;
                    }
                }
                
                // Force completion even if pipes didn't drain cleanly
                viewModel.PipesDrained = true;
                viewModel.StatusMessage = "Done.";
                viewModel.IsComplete = true;
            });
        };

        viewModel.IsComplete = process.HasExited;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Start background tasks
        var displayTask = StartStatusDisplayAsync(viewModel, exitTimeoutCts.Token);
        var updateTask = Task.Run(async () =>
        {
            while (!viewModel.IsComplete)
            {
                viewModel.Elapsed = chrono.Elapsed;
                viewModel.OverallRate = viewModel.Elapsed.TotalSeconds > 0 ? (int)(viewModel.EventCount / viewModel.Elapsed.TotalSeconds) : 0;

                if (viewModel.LastCurrentRateUpdateUtc == DateTime.MinValue)
                {
                    viewModel.CurrentRate = viewModel.OverallRate;
                }
                else if ((DateTime.UtcNow - viewModel.LastCurrentRateUpdateUtc) > TimeSpan.FromSeconds(1))
                {
                    viewModel.CurrentRate = 0;
                    viewModel.RecordTotalRate(0);
                }

                await Task.Delay(100, exitTimeoutCts.Token);
            }
        }, exitTimeoutCts.Token);

        await Task.WhenAny(displayTask, updateTask);

        exitTimeoutCts.Cancel();
        exitTimeoutCts.Dispose();
        Console.CancelKeyPress -= cancelHandler;
        sigIntRegistration?.Dispose();
        sigTermRegistration?.Dispose();

        if (shutdownTask is not null)
        {
            await shutdownTask;
        }

        // Ensure process has exited before accessing ExitCode
        if (!process.HasExited)
        {
            await process.WaitForExitAsync();
        }

        var exitCode = process.ExitCode;

        if (exitCode != 0 && !viewModel.RawErrorLines.IsEmpty)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("===== FULL PERF STDERR =====");
            foreach (var line in viewModel.RawErrorLines)
            {
                Console.Error.WriteLine(line);
            }
            Console.Error.WriteLine("===== END PERF STDERR =====");
        }

        return exitCode;
    }

    static async Task StartStatusDisplayAsync(PerfMonitorViewModel viewModel, CancellationToken cancellationToken)
    {
        var interactive = !Console.IsErrorRedirected;
        var lastLineLength = 0;
        var lastRendered = DateTime.MinValue;

        try
        {
            while (!viewModel.IsComplete)
            {
                if (!interactive)
                {
                    if ((DateTime.UtcNow - lastRendered) >= TimeSpan.FromSeconds(5))
                    {
                        Console.Error.WriteLine(BuildStatusLine(viewModel));
                        lastRendered = DateTime.UtcNow;
                    }
                }
                else
                {
                    var line = BuildStatusLine(viewModel);
                    var paddedLine = line.Length < lastLineLength
                        ? line + new string(' ', lastLineLength - line.Length)
                        : line;
                    Console.Error.Write('\r');
                    Console.Error.Write(paddedLine);
                    lastLineLength = paddedLine.Length;
                }

                await Task.Delay(250, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (interactive)
            {
                var line = BuildStatusLine(viewModel);
                var paddedLine = line.Length < lastLineLength
                    ? line + new string(' ', lastLineLength - line.Length)
                    : line;
                Console.Error.Write('\r');
                Console.Error.WriteLine(paddedLine);
            }
        }
    }

    static string BuildStatusLine(PerfMonitorViewModel viewModel)
    {
        var elapsed = viewModel.ProcessHasExited
            ? viewModel.Elapsed
            : DateTime.UtcNow - viewModel.ProcessStartTime;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var statusMessage = string.IsNullOrWhiteSpace(viewModel.StatusMessage)
            ? viewModel.Status
            : viewModel.StatusMessage;

        return string.Join(
            " | ",
            [
                $"status: {viewModel.Status}",
                $"elapsed: {elapsed:hh\\:mm\\:ss}",
                $"events: {viewModel.EventCount:N0}",
                $"rate: {viewModel.CurrentRate:N0}/s",
                statusMessage
            ]);
    }

    static bool TryRequestGracefulShutdown(Process process)
    {
        if (process.HasExited)
            return true;

        if (OperatingSystem.IsWindows())
        {
            return process.CloseMainWindow();
        }

        return TrySendUnixSignal(process, SigInt, preferProcessGroup: true);
    }

    static void TryAssignUnixProcessGroup(Process process)
    {
        if (OperatingSystem.IsWindows() || process.HasExited)
            return;

        try
        {
            _ = setpgid(process.Id, process.Id);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    static bool TrySendUnixSignal(Process process, int signal, bool preferProcessGroup)
    {
        if (OperatingSystem.IsWindows() || process.HasExited)
            return false;

        try
        {
            if (preferProcessGroup && kill(-process.Id, signal) == 0)
                return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }

        return kill(process.Id, signal) == 0;
    }
}
