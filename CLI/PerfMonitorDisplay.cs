using Spectre.Console;

namespace CLI.Display;

public class PerfMonitorDisplay
{
    private readonly PerfMonitorViewModel _viewModel;
    private readonly Layout _layout;

    public PerfMonitorDisplay(PerfMonitorViewModel viewModel)
    {
        _viewModel = viewModel;
        _layout = CreateLayout();
    }

    private Layout CreateLayout()
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Top").Ratio(2),
                new Layout("Logs").Ratio(1)
            );
        
        layout["Top"].SplitColumns(
            new Layout("Stats").Ratio(2),
            new Layout("FileStatus").Ratio(3)
        );

        return layout;
    }

    public async Task StartLiveDisplayAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Starting live display...");
        
        await AnsiConsole.Live(_layout)
            .StartAsync(async ctx =>
            {
                while (!_viewModel.IsComplete && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        UpdateDisplay();
                        ctx.Refresh();
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.WriteLine($"Display update error: {ex}");
                    }
                    
                    await Task.Delay(10, cancellationToken);
                }
            });
    }

    private void UpdateDisplay()
    {
        UpdateStatsPanel();
        UpdateFileStatusPanel();
        UpdateLogsPanel();
    }

    private void UpdateStatsPanel()
    {
        var statsPanel = new Panel(
            new Markup($"[bold yellow]Event Statistics[/]\n\n" +
                     $"[green]Total Events:[/] {_viewModel.EventCount:N0}\n" +
                     $"[blue]Overall Rate (events/sec):[/] {_viewModel.OverallRate:N0}\n" +
                     $"[cyan]Current Rate (events/sec):[/] {_viewModel.CurrentRate:N0}\n" +
                     $"[yellow]Elapsed Time:[/] {_viewModel.Elapsed:hh\\:mm\\:ss}\n" +
                     $"\n[bold magenta]Memory & GC[/]\n" +
                     $"[white]Memory Usage:[/] {_viewModel.MemoryMB:F1} MB\n" +
                     $"[white]GC Gen0/Gen1/Gen2:[/] {_viewModel.Gen0Count}/{_viewModel.Gen1Count}/{_viewModel.Gen2Count}\n" +
                     $"[white]% Time in GC:[/] {_viewModel.GcPercentage:F1}%\n" +
                     $"{_viewModel.GcStatus}\n" +
                     $"\n[bold]Process Status:[/] {_viewModel.Status}\n" +
                     $"[dim]Press Ctrl+C to stop[/]"))
        {
            Header = new PanelHeader("[bold]Status[/]"),
            Border = BoxBorder.Rounded
        };

        _layout["Top"]["Stats"].Update(statsPanel);
    }

    private void UpdateFileStatusPanel()
    {
        _viewModel.CleanupExpiredFiles();
        
        Tree fileTree;
        if (_viewModel.FileStatuses.IsEmpty)
        {
            fileTree = new Tree("[dim]No files opened yet...[/]");
        }
        else
        {
            fileTree = new Tree("Files");
            var pidGroups = _viewModel.FileStatuses.GroupBy(f => f.Key.Split('/')[0]).OrderBy(g => g.Key);
            
            foreach (var pidGroup in pidGroups)
            {
                var pidNode = fileTree.AddNode($"[bold]PID {pidGroup.Key}[/]");
                var tidGroups = pidGroup.GroupBy(f => f.Key.Split('/')[1]).OrderBy(g => g.Key);
                
                foreach (var tidGroup in tidGroups)
                {
                    var tidNode = pidNode.AddNode($"[bold cyan]TID {tidGroup.Key}[/]");
                    
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
        
        _layout["Top"]["FileStatus"].Update(fileStatusPanel);
    }

    private void UpdateLogsPanel()
    {
        var consoleLines = new List<string>();

        // Add output lines
        foreach (var line in _viewModel.OutputLines.ToArray())
        {
            consoleLines.Add(line);
        }

        // Add error lines
        foreach (var line in _viewModel.ErrorLines.ToArray())
        {
            consoleLines.Add(line);
        }

        // Take only the most recent lines that fit
        var maxLines = Math.Max(1, Console.WindowHeight - 10);
        var displayLines = consoleLines.TakeLast(maxLines).ToArray();

        var consoleContent = displayLines.Length > 0
            ? string.Join("\n", displayLines)
            : $"[dim]Waiting for perf output...\nProcess started at {_viewModel.Elapsed:hh\\:mm\\:ss}[/]";

        var consolePanel = new Panel(new Markup(consoleContent))
        {
            Header = new PanelHeader("[bold]Perf Output[/]"),
            Border = BoxBorder.Rounded
        };

        _layout["Logs"].Update(consolePanel);
    }
}