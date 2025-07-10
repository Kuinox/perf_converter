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

public class FileStatus
{
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int EntryCount { get; set; }
    public int BufferedCount { get; set; }
    public int FlushedCount { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime? LastActivity { get; set; }
}

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

        //AnsiConsole.MarkupLine($"[green]Executing:[/] {perfCommand}");
        //AnsiConsole.MarkupLine($"[blue]Output directory:[/] {outputDir.FullName}");
        //AnsiConsole.WriteLine();

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
        var rateHistory = new Queue<(DateTime timestamp, long eventCount)>();
        var outputLines = new ConcurrentQueue<string>();
        var errorLines = new ConcurrentQueue<string>();
        var fileStatuses = new ConcurrentDictionary<string, FileStatus>();
        var isComplete = false;

        // Set up Ctrl+C handler to kill the process
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent immediate termination
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(true); // Kill process tree
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error killing process: {ex.Message}[/]");
                    Environment.Exit(1);
                }
            }
        };

        // Create layout
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Top"),
                new Layout("Logs").Ratio(1)
            );
        
        layout["Top"].SplitColumns(
            new Layout("Stats").Size(50),
            new Layout("FileStatus").Ratio(2)
        );

        // Set up process event handlers
        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            if (e.Data.StartsWith("PROGRESS:"))
            {
                var sliced = e.Data.AsSpan()[9..].Trim().ToString();
                eventCount = long.Parse(sliced);
            }
            else
            {
                outputLines.Enqueue(e.Data);
                while (outputLines.Count > 50)
                {
                    outputLines.TryDequeue(out _);
                }
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                // Parse FILE_STATUS and FILE_ACTIVITY lines
                if (e.Data.StartsWith("FILE_STATUS|") || e.Data.StartsWith("FILE_ACTIVITY|"))
                {
                    var parts = e.Data.Split('|');
                    if (parts.Length >= 3)
                    {
                        var fileName = parts[1];
                        var actionType = parts[2];
                        var entryCount = 0;
                        
                        if (parts.Length >= 4 && int.TryParse(parts[3], out var count))
                        {
                            entryCount = count;
                        }
                        
                        if (e.Data.StartsWith("FILE_ACTIVITY|"))
                        {
                            // Handle buffering activity - entryCount is current buffer size
                            fileStatuses.AddOrUpdate(fileName,
                                new FileStatus { FileName = fileName, Status = "BUFFERING", BufferedCount = entryCount, LastActivity = DateTime.UtcNow },
                                (key, existing) =>
                                {
                                    existing.LastActivity = DateTime.UtcNow;
                                    existing.BufferedCount = entryCount; // Set to current buffer size, not accumulate
                                    return existing;
                                });
                        }
                        else
                        {
                            // Handle status changes
                            fileStatuses.AddOrUpdate(fileName, 
                                new FileStatus { FileName = fileName, Status = actionType, ClosedAt = actionType == "CLOSED" ? DateTime.UtcNow : null },
                                (key, existing) => 
                                {
                                    existing.Status = actionType;
                                    existing.LastUpdated = DateTime.UtcNow;
                                    if (actionType == "CLOSED")
                                    {
                                        existing.ClosedAt = DateTime.UtcNow;
                                    }
                                    if (actionType == "FLUSHING" && entryCount > 0)
                                    {
                                        existing.FlushedCount += entryCount;
                                    }
                                    return existing;
                                });
                        }
                    }
                }
                else
                {
                    errorLines.Enqueue($"[red]{e.Data}[/]");
                    // Keep only last 20 lines
                    if (errorLines.Count > 20)
                    {
                        errorLines.TryDequeue(out _);
                    }
                }
            }
        };


        process.EnableRaisingEvents = true;
        process.Exited += (sender, e) =>
        {
            isComplete = true;
        };
        isComplete = process.HasExited;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Start the live display
        Console.WriteLine("starting live display...");
        try
        {
            await AnsiConsole.Live(layout)
                .StartAsync(async ctx =>
                {
                    while (!isComplete)
                    {
                        try
                        {
                            // Update left panel (statistics)
                            var currentEventCount = eventCount;
                            var elapsed = chrono.Elapsed;
                            var rate = elapsed.TotalSeconds > 0 ? (int)(currentEventCount / elapsed.TotalSeconds) : 0;
                            
                            // Calculate rolling average rate over last 5 seconds
                            var now = DateTime.UtcNow;
                            rateHistory.Enqueue((now, currentEventCount));
                            
                            // Remove entries older than 5 seconds
                            while (rateHistory.Count > 0 && (now - rateHistory.Peek().timestamp).TotalSeconds > 5)
                            {
                                rateHistory.Dequeue();
                            }
                            
                            var currentRate = 0;
                            if (rateHistory.Count >= 2)
                            {
                                var oldest = rateHistory.First();
                                var newest = rateHistory.Last();
                                var timeDiff = (newest.timestamp - oldest.timestamp).TotalSeconds;
                                if (timeDiff > 0)
                                {
                                    currentRate = (int)((newest.eventCount - oldest.eventCount) / timeDiff);
                                }
                            }


                            var statsPanel = new Panel(
                                new Markup($"[bold yellow]Event Statistics[/]\n\n" +
                                         $"[green]Total Events:[/] {currentEventCount:N0}\n" +
                                         $"[blue]Overall Rate (events/sec):[/] {rate:N0}\n" +
                                         $"[cyan]Current Rate (events/sec):[/] {currentRate:N0}\n" +
                                         $"[yellow]Elapsed Time:[/] {elapsed:hh\\:mm\\:ss}\n" +
                                         $"[dim]Press Ctrl+C to stop[/]"))
                            {
                                Header = new PanelHeader("[bold]Status[/]"),
                                Border = BoxBorder.Rounded
                            };

                            layout["Top"]["Stats"].Update(statsPanel);
                            
                            // Update file status panel using Tree
                            var filesToRemove = new List<string>();
                            
                            // Clean up expired closed files
                            foreach (var kvp in fileStatuses.ToArray())
                            {
                                var file = kvp.Value;
                                if (file.Status == "CLOSED" && file.ClosedAt.HasValue && (now - file.ClosedAt.Value).TotalSeconds > 10)
                                {
                                    filesToRemove.Add(kvp.Key);
                                }
                            }
                            
                            foreach (var key in filesToRemove)
                            {
                                fileStatuses.TryRemove(key, out _);
                            }
                            
                            Tree fileTree;
                            if (fileStatuses.IsEmpty)
                            {
                                fileTree = new Tree("[dim]No files opened yet...[/]");
                            }
                            else
                            {
                                fileTree = new Tree("Files");
                                // Group by PID
                                var pidGroups = fileStatuses.GroupBy(f => f.Key.Split('/')[0]).OrderBy(g => g.Key);
                                
                                foreach (var pidGroup in pidGroups)
                                {
                                    var pidNode = fileTree.AddNode($"[bold]PID {pidGroup.Key}[/]");
                                    
                                    // Group by TID within each PID
                                    var tidGroups = pidGroup.GroupBy(f => f.Key.Split('/')[1]).OrderBy(g => g.Key);
                                    
                                    foreach (var tidGroup in tidGroups)
                                    {
                                        var tidNode = pidNode.AddNode($"[bold cyan]TID {tidGroup.Key}[/]");
                                        
                                        // Add files for this TID
                                        foreach (var kvp in tidGroup.OrderBy(f => f.Key))
                                        {
                                            var file = kvp.Value;
                                            
                                            var statusColor = file.Status switch
                                            {
                                                "BUFFERING" => "[green]",
                                                "FLUSHING" => "[yellow]",
                                                "CLOSED" => "[dim green]",
                                                _ => "[white]"
                                            };
                                            
                                            var fileName = file.FileName.Split('/').Last();
                                            var statusText = file.Status;
                                            if (file.Status == "BUFFERING" && file.BufferedCount > 0)
                                            {
                                                statusText += $" ({file.BufferedCount:N0})";
                                            }
                                            
                                            var fileDisplay = $"{statusColor}{statusText}[/] [blue]{fileName}[/] [dim]({file.FlushedCount:N0})[/]";
                                            tidNode.AddNode(fileDisplay);
                                        }
                                    }
                                }
                            }
                            
                            var fileStatusPanel = new Panel(fileTree)
                            {
                                Header = new PanelHeader("[bold]File Status[/]"),
                                Border = BoxBorder.Rounded
                            };
                            
                            layout["Top"]["FileStatus"].Update(fileStatusPanel);

                            // Update bottom panel (console output)
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
                                ? string.Join("\n", displayLines)
                                : $"[dim]Waiting for perf output...\nProcess started at {elapsed:hh\\:mm\\:ss}[/]";

                            var consolePanel = new Panel(new Markup(consoleContent))
                            {
                                Header = new PanelHeader("[bold]Perf Output[/]"),
                                Border = BoxBorder.Rounded
                            };

                            layout["Logs"].Update(consolePanel);
                            
                            ctx.Refresh();
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.WriteLine($"Display update error: {ex}");
                        }
                        
                        await Task.Delay(10);
                    }
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

        return process.ExitCode;
    }
}