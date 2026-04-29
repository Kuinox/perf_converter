using System.Globalization;
using System.Linq;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace CLI.Display;

public sealed class PerfMonitorDisplay
{
    const int MaxLogLines = 12;
    const int MaxFileRows = 10;
    readonly PerfMonitorViewModel _viewModel;
    readonly Action _requestShutdown;
    readonly Table _summaryTable;
    readonly Sparkline _totalRateSparkline;
    readonly Table _fileTable;
    readonly TextBlock _logsText;
    readonly Visual _root;

    public PerfMonitorDisplay(PerfMonitorViewModel viewModel, Action requestShutdown)
    {
        _viewModel = viewModel;
        _requestShutdown = requestShutdown;
        _summaryTable = new Table { ShowHeaderSeparator = false };
        _totalRateSparkline = new Sparkline().MinHeight(1).MaxHeight(1).MinWidth(20);
        _fileTable = new Table { ShowHeaderSeparator = true };
        _logsText = new TextBlock { Wrap = false };
        _root = CreateRoot();
    }

    public async Task StartLiveDisplayAsync(CancellationToken cancellationToken = default)
    {
        var options = new TerminalRunOptions
        {
            ExitGesture = null,
            UpdateWaitDuration = TimeSpan.FromMilliseconds(50)
        };

        await Terminal.RunAsync(_root, async _ =>
        {
            UpdateDisplay();
            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return TerminalLoopResult.Stop;
            }

            return cancellationToken.IsCancellationRequested ? TerminalLoopResult.Stop : TerminalLoopResult.Continue;
        }, options, cancellationToken);
    }

    Visual CreateRoot()
    {
        var contentGrid = new Grid
        {
            RowGap = 1,
            ColumnGap = 1
        }.Stretch();

        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GridUnitType.Auto, 0) });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GridUnitType.Star, 1) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GridUnitType.Fixed, 42) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GridUnitType.Star, 1) });

        contentGrid.Cells.Add(new GridCell(CreateGroup("Summary", _summaryTable.Stretch(), "Ctrl+C stops perf")) { Row = 0, Column = 0 });
        contentGrid.Cells.Add(new GridCell(CreateGroup("Throughput", CreateThroughputVisual(), "current events/sec")) { Row = 0, Column = 1 });
        contentGrid.Cells.Add(new GridCell(CreateGroup("Files", _fileTable.Stretch(), "recent activity")) { Row = 1, Column = 0, ColumnSpan = 2 });

        var logsGroup = CreateGroup("Perf Output", _logsText.Scrollable().Stretch(), "latest stdout/stderr").Stretch().MinHeight(8);

        var root = new DockLayout(
            top: new TextBlock("PerfConverter Monitor").MinHeight(1),
            content: contentGrid,
            bottom: logsGroup)
            .Stretch();

        root.AddKeyBinding(new KeyGesture('c', TerminalModifiers.Ctrl), _requestShutdown);
        return root;
    }

    static Group CreateGroup(string title, Visual content, string? footer = null)
    {
        return new Group(new TextBlock(title), content)
        {
            BottomRightText = string.IsNullOrWhiteSpace(footer) ? null : new TextBlock(footer)
        }.Stretch();
    }

    Visual CreateThroughputVisual()
    {
        var grid = new Grid
        {
            RowGap = 1
        }.Stretch();

        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GridUnitType.Auto, 0) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GridUnitType.Auto, 0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GridUnitType.Star, 1) });
        grid.Cells.Add(new GridCell(new TextBlock("Total current throughput trend")) { Row = 0, Column = 0 });
        grid.Cells.Add(new GridCell(_totalRateSparkline.HorizontalAlignment(Align.Stretch).VerticalAlignment(Align.Start)) { Row = 1, Column = 0 });

        return grid;
    }

    void UpdateDisplay()
    {
        _viewModel.CleanupExpiredFiles();

        UpdateSummary();
        UpdateThroughputChart();
        UpdateFileTable();
        UpdateLogs();
    }

    void UpdateSummary()
    {
        var statusMessage = string.IsNullOrWhiteSpace(_viewModel.StatusMessage)
            ? "Monitoring perf script output"
            : _viewModel.StatusMessage;
        var elapsed = GetDisplayElapsed();

        _summaryTable.HeaderCells.Clear();
        _summaryTable.RowCells.Clear();
        AddSummaryRow("Status", _viewModel.Status);
        AddSummaryRow("Message", statusMessage);
        AddSummaryRow("Elapsed", $"{elapsed:hh\\:mm\\:ss}");
        AddSummaryRow("Trace Start", TraceTimestampFormatter.Format(_viewModel.FirstTraceTimestampNs));
        AddSummaryRow("Trace Now", TraceTimestampFormatter.Format(_viewModel.LastTraceTimestampNs));
        AddSummaryRow("Trace Span", TraceTimestampFormatter.FormatRange(_viewModel.FirstTraceTimestampNs, _viewModel.LastTraceTimestampNs));
        AddSummaryRow("Events", _viewModel.EventCount.ToString("N0", CultureInfo.InvariantCulture));
        AddSummaryRow("Current", $"{_viewModel.CurrentRate:N0}/s");
        AddSummaryRow("Overall", $"{_viewModel.OverallRate:N0}/s");
        AddSummaryRow("Memory", $"{_viewModel.MemoryMB.ToString("F1", CultureInfo.InvariantCulture)} MB");
        AddSummaryRow("GC", $"{_viewModel.Gen0Count}/{_viewModel.Gen1Count}/{_viewModel.Gen2Count} ({_viewModel.GcPercentage.ToString("F1", CultureInfo.InvariantCulture)}%)");
        AddSummaryRow("GC Status", _viewModel.GcStatus);
    }

    void UpdateThroughputChart()
    {
        var history = _viewModel.GetTotalRateHistorySnapshot();
        SparklineExtensions.Values(_totalRateSparkline, history);
        _totalRateSparkline.Minimum = 0;
        _totalRateSparkline.Maximum = history.Length == 0 ? 1 : Math.Max(1, history.Max());
    }

    void UpdateFileTable()
    {
        var rows = _viewModel.FileStatuses
            .OrderByDescending(x => x.Value.LastActivity ?? x.Value.LastUpdated)
            .ThenBy(x => x.Key)
            .Take(MaxFileRows)
            .Select(kvp =>
            {
                var (pid, tid, fileName) = SplitFileKey(kvp.Key);
                return new Visual[]
                {
                    new TextBlock(pid),
                    new TextBlock(tid),
                    new TextBlock(fileName),
                    new TextBlock(kvp.Value.Status),
                    new TextBlock(kvp.Value.CurrentRate.ToString("N0", CultureInfo.InvariantCulture)),
                    CreateSparkline(kvp.Value.GetRateHistorySnapshot()),
                    new TextBlock(kvp.Value.FlushedCount.ToString("N0", CultureInfo.InvariantCulture))
                };
            })
            .ToArray();

        _fileTable.HeaderCells.Clear();
        foreach (var header in new[] { "PID", "TID", "File", "State", "Rate/s", "Trend", "Flushed" })
        {
            _fileTable.HeaderCells.Add(new TextBlock(header));
        }

        _fileTable.RowCells.Clear();
        foreach (var row in rows)
        {
            _fileTable.AddRow(row);
        }

        if (rows.Length == 0)
        {
            _fileTable.AddRow(
                new TextBlock(""),
                new TextBlock(""),
                new TextBlock("No files yet"),
                new TextBlock(""),
                new TextBlock(""),
                new TextBlock(""),
                new TextBlock(""));
        }

    }

    void UpdateLogs()
    {
        var elapsed = GetDisplayElapsed();
        var lines = _viewModel.OutputLines
            .Concat(_viewModel.ErrorLines)
            .TakeLast(MaxLogLines)
            .ToArray();

        _logsText.Text = lines.Length == 0
            ? $"Waiting for perf output...{Environment.NewLine}Process uptime: {elapsed:hh\\:mm\\:ss}"
            : string.Join(Environment.NewLine, lines);
    }

    TimeSpan GetDisplayElapsed()
    {
        if (!_viewModel.ProcessHasExited)
        {
            var elapsed = DateTime.UtcNow - _viewModel.ProcessStartTime;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        return _viewModel.Elapsed;
    }

    void AddSummaryRow(string key, string value)
    {
        _summaryTable.AddRow(new TextBlock(key), new TextBlock(value) { Wrap = true });
    }

    static Sparkline CreateSparkline(double[] history)
    {
        var sparkline = new Sparkline(history).MinWidth(12).MinHeight(1).MaxHeight(1).VerticalAlignment(Align.Start);
        sparkline.Minimum = 0;
        sparkline.Maximum = history.Length == 0 ? 1 : Math.Max(1, history.Max());
        return sparkline;
    }

    static (string Pid, string Tid, string FileName) SplitFileKey(string key)
    {
        var parts = key.Split('/');
        if (parts.Length >= 3)
        {
            return (parts[0], parts[1], parts[^1]);
        }

        return ("-", "-", key);
    }
}
