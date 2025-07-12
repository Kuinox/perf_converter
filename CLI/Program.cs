using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Temp.Schema;
using Spectre.Console;
using CLI.Display;

namespace CLI;

internal class Program
{
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
                getDefaultValue: () => "-f --itrace=bei0ns",
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
            AnsiConsole.MarkupLine($"[red]Error: Input file '{inputFile.FullName}' does not exist.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[yellow]Extracting auxiliary data loss events...[/]");
        var dataLost = GetAuxDataLost(inputFile.FullName);

        var dlFilterPath = Path.Combine(AppContext.BaseDirectory, "PerfConverter.so");

        if (!File.Exists(dlFilterPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: PerfConverter.so not found at '{dlFilterPath}'[/]");
            return 1;
        }

        if (!outputDir.Exists)
            outputDir.Create();

        Environment.SetEnvironmentVariable("OUTPUT_DIRECTORY", outputDir.FullName);

        var perfCommand = $"perf script {perfArgs} -i {inputFile.FullName} --dlfilter {dlFilterPath}";
        var auxDataLoss = JsonSerializer.Serialize(dataLost);

        if (dryRun)
        {
            AnsiConsole.MarkupLine("[green]Would execute:[/]");
            AnsiConsole.WriteLine($"export OUTPUT_DIRECTORY=\"{outputDir.FullName}\"");
            AnsiConsole.WriteLine($"export AUX_DATA_LOSS='{auxDataLoss}'");
            AnsiConsole.WriteLine(perfCommand);
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
        processInfo.Environment["AUX_DATA_LOSS"] = auxDataLoss;

        try
        {
            return await RunPerfWithMonitor(processInfo);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error executing perf command: {ex.Message}[/]");
            return 1;
        }
    }


    private static IReadOnlyCollection<AuxDataLost> GetAuxDataLost(string perfFilePath)
    {
        List<AuxDataLost> _dataLostTimes = [];
        var entryCount = 0L;
        var lastUpdate = DateTime.UtcNow;

        return AnsiConsole.Status()
            .Start("Processing aux data...", ctx =>
            {
                AuxDataExtractor.Process(perfFilePath, entry =>
                {
                    entryCount++;
                    if (entry.HasValue)
                    {
                        if (entry.Value.Flags != 0)
                        {
                            _dataLostTimes.Add(new AuxDataLost(entry.Value.Time, entry.Value.Tid, entry.Value.Pid));
                        }
                    }

                    if (DateTime.UtcNow - lastUpdate > TimeSpan.FromMilliseconds(100))
                    {
                        ctx.Status($"Processed {entryCount:N0} entries, found {_dataLostTimes.Count} aux data loss events");
                        lastUpdate = DateTime.UtcNow;
                    }
                });

                ctx.Status($"Completed: {entryCount:N0} entries processed, {_dataLostTimes.Count} aux data loss events found");
                return _dataLostTimes;
            });
    }

    private static async Task<int> RunPerfWithMonitor(ProcessStartInfo processInfo)
    {
        using var process = Process.Start(processInfo);
        if (process == null)
        {
            AnsiConsole.MarkupLine("[red]Failed to start perf process.[/]");
            return 1;
        }

        var chrono = Stopwatch.StartNew();
        var viewModel = new PerfMonitorViewModel();
        var commandProcessor = new CommandProcessor(viewModel);
        var messageHandler = new MessageHandler(viewModel, commandProcessor);
        var display = new PerfMonitorDisplay(viewModel);

        // GC event listener will be started when .NET runtime is ready
        GcEventListener? gcEventListener = null;

        // Function to start GC listener when .NET runtime is ready
        void StartGcListener()
        {
            if (gcEventListener == null)
            {
                try
                {
                    gcEventListener = new GcEventListener(process.Id, viewModel);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not start GC event listener: {ex.Message}");
                }
            }
        }

        var exitTimeoutCts = new CancellationTokenSource();

        // Set up Ctrl+C handler
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error killing process: {ex.Message}[/]");
                    Environment.Exit(1);
                }
            }
        };

        // Set up process event handlers
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                if (e.Data == "DOTNET_READY")
                {
                    StartGcListener();
                }
                messageHandler.ProcessOutputMessage(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                messageHandler.ProcessErrorMessage(e.Data);
            }
        };

        process.EnableRaisingEvents = true;

        process.Exited += (sender, e) =>
        {
            _ = Task.Run(async () =>
            {
                viewModel.StatusMessage = "Process completed, waiting for cleanup...";
                await Task.Delay(10000, exitTimeoutCts.Token);
                if (!exitTimeoutCts.Token.IsCancellationRequested)
                {
                    viewModel.StatusMessage = "Done.";
                    viewModel.IsComplete = true;
                }
            }, exitTimeoutCts.Token);
        };

        viewModel.IsComplete = process.HasExited;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Start background tasks
        var displayTask = display.StartLiveDisplayAsync(exitTimeoutCts.Token);
        var updateTask = Task.Run(async () =>
        {
            while (!viewModel.IsComplete)
            {
                viewModel.Elapsed = chrono.Elapsed;
                viewModel.OverallRate = viewModel.Elapsed.TotalSeconds > 0 ? (int)(viewModel.EventCount / viewModel.Elapsed.TotalSeconds) : 0;

                await Task.Delay(100, exitTimeoutCts.Token);
            }
        }, exitTimeoutCts.Token);

        await Task.WhenAny(displayTask, updateTask);

        exitTimeoutCts.Cancel();
        exitTimeoutCts.Dispose();

        // Clean up GC event listener
        gcEventListener?.Dispose();

        // Ensure process has exited before accessing ExitCode
        if (!process.HasExited)
        {
            await process.WaitForExitAsync();
        }

        return process.ExitCode;
    }
}