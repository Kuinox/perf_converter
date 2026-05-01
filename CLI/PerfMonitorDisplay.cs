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
    const int MaxFileRows = 16;
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
        _summaryTable = new Table { ShowHeaderSeparator = true };
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
            if (_viewModel.IsComplete)
            {
                return TerminalLoopResult.Stop;
            }

            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return TerminalLoopResult.Stop;
            }

            return cancellationToken.IsCancellationRequested || _viewModel.IsComplete
                ? TerminalLoopResult.Stop
                : TerminalLoopResult.Continue;
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
        contentGrid.Cells.Add(new GridCell(CreateRightColumn().Stretch()) { Row = 0, Column = 1 });
        contentGrid.Cells.Add(new GridCell(CreateGroup("Files", _fileTable.Stretch(), "recent activity").Stretch())
        {
            Row = 1,
            Column = 0,
            ColumnSpan = 2
        });

        var root = new DockLayout(
            top: new TextBlock("PerfConverter Monitor").MinHeight(1),
            content: contentGrid,
            bottom: null)
            .Stretch();

        root.AddKeyBinding(new KeyGesture('c', TerminalModifiers.Ctrl), _requestShutdown);
        return root;
    }

    Visual CreateRightColumn()
    {
        var rightColumn = new Grid
        {
            RowGap = 1
        }.Stretch();

        rightColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GridUnitType.Star, 1) });
        rightColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(GridUnitType.Star, 1) });
        rightColumn.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GridUnitType.Star, 1) });

        rightColumn.Cells.Add(new GridCell(CreateGroup("Throughput", CreateThroughputVisual(), "current events/sec").Stretch())
        {
            Row = 0,
            Column = 0
        });
        rightColumn.Cells.Add(new GridCell(CreateGroup("Perf Output", _logsText.Scrollable().Stretch(), "latest stdout/stderr").Stretch().MinHeight(8))
        {
            Row = 1,
            Column = 0
        });

        return rightColumn;
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

        var rows = new (string Metric, string Value)[]
        {
            ("Status", _viewModel.Status),
            ("Message", statusMessage),
            ("Elapsed", elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)),
            ("Trace Span", TraceTimestampFormatter.FormatRange(_viewModel.FirstTraceTimestampNs, _viewModel.LastTraceTimestampNs)),
            ("Events", _viewModel.EventCount.ToString("N0", CultureInfo.InvariantCulture)),
            ("Current rate", $"{_viewModel.CurrentRate.ToString("N0", CultureInfo.InvariantCulture)}/s"),
            ("Overall rate", $"{_viewModel.OverallRate.ToString("N0", CultureInfo.InvariantCulture)}/s"),
            ("Memory", $"{_viewModel.MemoryMB.ToString("F1", CultureInfo.InvariantCulture)} MB"),
            ("GC", $"{_viewModel.Gen0Count.ToString("N0", CultureInfo.InvariantCulture)}/{_viewModel.Gen1Count.ToString("N0", CultureInfo.InvariantCulture)}/{_viewModel.Gen2Count.ToString("N0", CultureInfo.InvariantCulture)} ({_viewModel.GcPercentage.ToString("F1", CultureInfo.InvariantCulture)}%)"),
            ("GC Status", _viewModel.GcStatus)
        };

        _summaryTable.HeaderCells.Clear();
        _summaryTable.HeaderCells.Add(new TextBlock("Metric"));
        _summaryTable.HeaderCells.Add(new TextBlock("Value"));

        _summaryTable.RowCells.Clear();
        foreach (var (metric, value) in rows)
        {
            _summaryTable.AddRow(new TextBlock(metric), new TextBlock(value));
        }
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

    static Sparkline CreateSparkline(double[] history)
    {
        var sparkline = new Sparkline(history).MinWidth(8).MaxWidth(8).MinHeight(1).MaxHeight(1).VerticalAlignment(Align.Start);
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
