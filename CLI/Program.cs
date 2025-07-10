using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text;
using System.Collections.Concurrent;
using Temp.Schema;
using Spectre.Console;

namespace CLI;

internal class Program
{
    static async Task<int> Main(string[] args)
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

        AnsiConsole.MarkupLine($"[green]Executing:[/] {perfCommand}");
        AnsiConsole.MarkupLine($"[blue]Output directory:[/] {outputDir.FullName}");
        AnsiConsole.WriteLine();

        // Execute the command
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
            return await RunPerfWithLayout(processInfo);
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

    private static async Task<int> RunPerfWithLayout(ProcessStartInfo processInfo)
    {
        using var process = Process.Start(processInfo);
        if (process == null)
        {
            AnsiConsole.MarkupLine("[red]Failed to start perf process.[/]");
            return 1;
        }

        var chrono = Stopwatch.StartNew();
        var eventCount = 0L;
        var lastEventCount = 0L;
        var outputLines = new ConcurrentQueue<string>();
        var errorLines = new ConcurrentQueue<string>();
        var isComplete = false;

        // Create layout
        var layout = new Layout("Root")
            .SplitColumns(
                new Layout("Left").Size(30),
                new Layout("Right")
            );

        // Set up process event handlers
        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            
            if (e.Data.StartsWith("PROGRESS:"))
            {
                try
                {
                    var sliced = e.Data.AsSpan()[9..].Trim().ToString();
                    if (long.TryParse(sliced, out var count))
                    {
                        Interlocked.Exchange(ref eventCount, count);
                    }
                }
                catch
                {
                    // Ignore parse errors for progress messages
                }
            }
            else
            {
                outputLines.Enqueue(e.Data);
                // Keep only last 50 lines
                if (outputLines.Count > 50)
                {
                    outputLines.TryDequeue(out _);
                }
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorLines.Enqueue($"[red]{e.Data}[/]");
                // Keep only last 20 lines
                if (errorLines.Count > 20)
                {
                    errorLines.TryDequeue(out _);
                }
            }
        };

        var displayUpdateTimer = new Timer(_ => UpdateDisplay(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        
        void UpdateDisplay()
        {
            if (isComplete) return;
            
            try
            {
                // Update left panel (statistics)
                var currentEventCount = eventCount;
                var elapsed = chrono.Elapsed;
                var rate = elapsed.TotalSeconds > 0 ? (int)(currentEventCount / elapsed.TotalSeconds) : 0;
                var deltaRate = elapsed.TotalSeconds > 1 ? (int)((currentEventCount - lastEventCount) / 1.0) : 0;
                
                var processStatus = process.HasExited ? "[red]Exited[/]" : "[green]Running[/]";
                
                var statsPanel = new Panel(
                    new Markup($"[bold yellow]Event Statistics[/]\\n\\n" +
                             $"[green]Total Events:[/] {currentEventCount:N0}\\n" +
                             $"[blue]Overall Rate:[/] {rate:N0} events/sec\\n" +
                             $"[cyan]Current Rate:[/] {deltaRate:N0} events/sec\\n" +
                             $"[yellow]Elapsed Time:[/] {elapsed.ToString(@"hh\:mm\:ss")}\\n" +
                             $"[white]Process Status:[/] {processStatus}\\n\\n" +
                             $"[dim]Press Ctrl+C to stop[/]"))
                {
                    Header = new PanelHeader("[bold]Status[/]"),
                    Border = BoxBorder.Rounded
                };

                layout["Left"].Update(statsPanel);

                // Update right panel (console output)
                var consoleLines = new List<string>();
                
                // Add output lines
                foreach (var line in outputLines.ToArray())
                {
                    consoleLines.Add(line);
                }
                
                // Add error lines
                foreach (var line in errorLines.ToArray())
                {
                    consoleLines.Add(line);
                }

                // Take only the most recent lines that fit
                var maxLines = Math.Max(1, Console.WindowHeight - 10);
                var displayLines = consoleLines.TakeLast(maxLines).ToArray();
                
                var consoleContent = displayLines.Length > 0 
                    ? string.Join("\\n", displayLines)
                    : $"[dim]Waiting for perf output...\\nProcess started at {elapsed:hh\\\\:mm\\\\:ss}[/]";

                var consolePanel = new Panel(new Markup(consoleContent))
                {
                    Header = new PanelHeader("[bold]Perf Output[/]"),
                    Border = BoxBorder.Rounded
                };

                layout["Right"].Update(consolePanel);
                lastEventCount = currentEventCount;
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteLine($"Display update error: {ex}");
            }
        }

        process.EnableRaisingEvents = true;
        process.Exited += (sender, e) =>
        {
            isComplete = true;
            displayUpdateTimer?.Dispose();
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Start the live display
        try
        {
            await AnsiConsole.Live(layout)
                .StartAsync(async ctx =>
                {
                    // Wait for process to complete
                    await process.WaitForExitAsync();
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine($"Display error: {ex.Message}");
            AnsiConsole.WriteLine($"Exception type: {ex.GetType().Name}");
            AnsiConsole.WriteLine($"Stack trace: {ex.StackTrace}");
            
            // Wait for process without display
            await process.WaitForExitAsync();
        }
        finally
        {
            displayUpdateTimer?.Dispose();
            isComplete = true;
        }
        
        return process.ExitCode;
    }
}