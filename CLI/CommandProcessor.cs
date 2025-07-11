namespace CLI;

public class CommandProcessor(PerfMonitorViewModel viewModel)
{
    public void ProcessCommand(string command)
    {
        switch (command)
        {
            case var cmd when cmd.StartsWith("PROGRESS:"):
                HandleProgressCommand(cmd);
                break;
            case var cmd when cmd.StartsWith("FILE_STATUS|"):
                HandleFileStatusCommand(cmd);
                break;
            case var cmd when cmd.StartsWith("FILE_ACTIVITY|"):
                HandleFileActivityCommand(cmd);
                break;
            case var cmd when cmd.StartsWith("EXIT_MESSAGE"):
                HandleExitMessage();
                break;
            default:
                // Unknown command, treat as regular output
                viewModel.OutputLines.Enqueue(command);
                viewModel.TrimOutputLines();
                break;
        }
    }

    private void HandleProgressCommand(string command)
    {
        var progressData = command.AsSpan()[9..].Trim().ToString();
        if (long.TryParse(progressData, out var eventCount))
        {
            viewModel.EventCount = eventCount;
        }
    }


    private void HandleFileStatusCommand(string command)
    {
        var parts = command.Split('|');
        if (parts.Length < 3) return;

        var fileName = parts[1];
        var actionType = parts[2];
        var entryCount = 0;
        
        if (parts.Length >= 4 && int.TryParse(parts[3], out var count))
            entryCount = count;
        
        viewModel.FileStatuses.AddOrUpdate(fileName, 
            new FileStatus { FileName = fileName, Status = actionType, ClosedAt = actionType == "CLOSED" ? DateTime.UtcNow : null },
            (key, existing) => 
            {
                existing.Status = actionType;
                existing.LastUpdated = DateTime.UtcNow;
                
                if (actionType == "CLOSED")
                    existing.ClosedAt = DateTime.UtcNow;
                
                if (actionType == "FLUSHING" && entryCount > 0)
                    existing.FlushedCount += entryCount;
                
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
            entryCount = count;
        
        viewModel.FileStatuses.AddOrUpdate(fileName,
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
        viewModel.ExitMessageReceived = true;
        viewModel.IsComplete = true;
    }

}