using CLI.ViewModel;

namespace CLI.Messages;

public class CommandProcessor
{
    private readonly PerfMonitorViewModel _viewModel;

    public CommandProcessor(PerfMonitorViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void ProcessCommand(string command)
    {
        switch (command)
        {
            case var cmd when cmd.StartsWith("PROGRESS:"):
                HandleProgressCommand(cmd);
                break;
            case var cmd when cmd.StartsWith("GC_EVENT:"):
                HandleGcEventCommand(cmd);
                break;
            case var cmd when cmd.StartsWith("MEMORY_STATS:"):
                HandleMemoryStatsCommand(cmd);
                break;
            case var cmd when cmd.StartsWith("FILE_STATUS|"):
                HandleFileStatusCommand(cmd);
                break;
            case var cmd when cmd.StartsWith("FILE_ACTIVITY|"):
                HandleFileActivityCommand(cmd);
                break;
            case "EXIT_MESSAGE":
                HandleExitMessage();
                break;
            default:
                // Unknown command, treat as regular output
                _viewModel.OutputLines.Enqueue(command);
                _viewModel.TrimOutputLines();
                break;
        }
    }

    private void HandleProgressCommand(string command)
    {
        var progressData = command.AsSpan()[9..].Trim().ToString();
        if (long.TryParse(progressData, out var eventCount))
        {
            _viewModel.EventCount = eventCount;
        }
    }

    private void HandleGcEventCommand(string command)
    {
        _viewModel.LastGcEvent = DateTime.UtcNow;
        var gcData = command.AsSpan()[9..].ToString();
        ParseGcData(gcData);
    }

    private void HandleMemoryStatsCommand(string command)
    {
        var memData = command.AsSpan()[13..].ToString();
        ParseMemoryData(memData);
    }

    private void HandleFileStatusCommand(string command)
    {
        var parts = command.Split('|');
        if (parts.Length < 3) return;

        var fileName = parts[1];
        var actionType = parts[2];
        var entryCount = 0;
        
        if (parts.Length >= 4 && int.TryParse(parts[3], out var count))
        {
            entryCount = count;
        }
        
        _viewModel.FileStatuses.AddOrUpdate(fileName, 
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

    private void HandleFileActivityCommand(string command)
    {
        var parts = command.Split('|');
        if (parts.Length < 3) return;

        var fileName = parts[1];
        var actionType = parts[2];
        var entryCount = 0;
        
        if (parts.Length >= 4 && int.TryParse(parts[3], out var count))
        {
            entryCount = count;
        }
        
        _viewModel.FileStatuses.AddOrUpdate(fileName,
            new FileStatus { FileName = fileName, Status = "BUFFERING", BufferedCount = entryCount, LastActivity = DateTime.UtcNow },
            (key, existing) =>
            {
                existing.LastActivity = DateTime.UtcNow;
                existing.BufferedCount = entryCount;
                return existing;
            });
    }

    private void HandleExitMessage()
    {
        _viewModel.ExitMessageReceived = true;
        _viewModel.IsComplete = true;
    }

    private void ParseGcData(string gcData)
    {
        var parts = gcData.Split(',');
        foreach (var part in parts)
        {
            var keyValue = part.Split('=');
            if (keyValue.Length == 2)
            {
                var key = keyValue[0];
                var value = keyValue[1];
                if (long.TryParse(value, out var longValue))
                {
                    switch (key)
                    {
                        case "Gen0": _viewModel.Gen0Count = longValue; break;
                        case "Gen1": _viewModel.Gen1Count = longValue; break;
                        case "Gen2": _viewModel.Gen2Count = longValue; break;
                        case "Memory": _viewModel.TotalMemory = longValue; break;
                    }
                }
            }
        }
    }

    private void ParseMemoryData(string memData)
    {
        var parts = memData.Split(',');
        foreach (var part in parts)
        {
            var keyValue = part.Split('=');
            if (keyValue.Length == 2)
            {
                var key = keyValue[0];
                var value = keyValue[1];
                if (long.TryParse(value, out var longValue))
                {
                    switch (key)
                    {
                        case "Total": _viewModel.TotalMemory = longValue; break;
                        case "Gen0": _viewModel.Gen0Count = longValue; break;
                        case "Gen1": _viewModel.Gen1Count = longValue; break;
                        case "Gen2": _viewModel.Gen2Count = longValue; break;
                    }
                }
            }
        }
    }
}