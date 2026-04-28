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
    readonly TextBlock _summaryText;
    readonly Sparkline _totalRateSparkline;
    readonly BreakdownChart _fileStateChart;
    readonly Table _fileTable;
    readonly TextBlock _logsText;
    readonly Visual _root;

    public PerfMonitorDisplay(PerfMonitorViewModel viewModel, Action requestShutdown)
    {
        _viewModel = viewModel;
        _requestShutdown = requestShutdown;
        _summaryText = new TextBlock { Wrap = true };
        _totalRateSparkline = new Sparkline().MinHeight(1).MaxHeight(1).MinWidth(20);
        _fileStateChart = new BreakdownChart { ShowValues = true, ShowPercentages = true };
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

        contentGrid.Cells.Add(new GridCell(CreateGroup("Summary", _summaryText, "Ctrl+C stops perf")) { Row = 0, Column = 0 });
        contentGrid.Cells.Add(new GridCell(CreateGroup("Throughput", CreateThroughputVisual(), "current events/sec")) { Row = 0, Column = 1 });
        contentGrid.Cells.Add(new GridCell(CreateGroup("File States", _fileStateChart.Stretch(), "live status mix")) { Row = 1, Column = 0 });
        contentGrid.Cells.Add(new GridCell(CreateGroup("Files", _fileTable.Stretch(), "recent activity")) { Row = 1, Column = 1 });

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
        UpdateFileStateChart();
        UpdateFileTable();
        UpdateLogs();
    }

    void UpdateSummary()
    {
        var statusMessage = string.IsNullOrWhiteSpace(_viewModel.StatusMessage)
            ? "Monitoring perf script output"
            : _viewModel.StatusMessage;
        var elapsed = GetDisplayElapsed();

        _summaryText.Text = string.Join(
            Environment.NewLine,
            [
                $"Status: {_viewModel.Status}",
                statusMessage,
                $"Elapsed: {elapsed:hh\\:mm\\:ss}",
                $"Events: {_viewModel.EventCount:N0}",
                $"Current rate: {_viewModel.CurrentRate:N0}/s",
                $"Overall rate: {_viewModel.OverallRate:N0}/s",
                $"Memory: {_viewModel.MemoryMB.ToString("F1", CultureInfo.InvariantCulture)} MB",
                $"GC: {_viewModel.Gen0Count}/{_viewModel.Gen1Count}/{_viewModel.Gen2Count}  ({_viewModel.GcPercentage.ToString("F1", CultureInfo.InvariantCulture)}%)",
                _viewModel.GcStatus
            ]);
    }

    void UpdateThroughputChart()
    {
        var history = _viewModel.GetTotalRateHistorySnapshot();
        SparklineExtensions.Values(_totalRateSparkline, history);
        _totalRateSparkline.Minimum = 0;
        _totalRateSparkline.Maximum = history.Length == 0 ? 1 : Math.Max(1, history.Max());
    }

    void UpdateFileStateChart()
    {
        var files = _viewModel.FileStatuses.Values.ToArray();
        var buffering = files.Count(x => x.Status == "BUFFERING");
        var flushing = files.Count(x => x.Status == "FLUSHING");
        var closed = files.Count(x => x.Status == "CLOSED");

        var segments = new[]
        {
            CreateSegment("BUFFERING", buffering, Colors.MediumSeaGreen),
            CreateSegment("FLUSHING", flushing, Colors.Goldenrod),
            CreateSegment("CLOSED", closed, Colors.SlateGray)
        };

        _fileStateChart.Segments.Clear();
        foreach (var segment in segments.Where(x => x.Value > 0))
        {
            _fileStateChart.Segments.Add(segment);
        }

        if (_fileStateChart.Segments.Count == 0)
        {
            _fileStateChart.Segments.Add(CreateSegment("No files", 1, Colors.DimGray));
        }
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

    static BreakdownSegment CreateSegment(string label, double value, Color color)
    {
        return new BreakdownSegment(value, new TextBlock(label))
        {
            Color = color
        };
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
