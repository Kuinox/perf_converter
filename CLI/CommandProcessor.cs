namespace CLI;

public class CommandProcessor(PerfMonitorViewModel viewModel)
{
    public void ProcessCommand(string command)
    {
        switch (command)
        {
            case var cmd when cmd.StartsWith("EXIT_MESSAGE"):
                HandleExitMessage();
                break;
            case "DOTNET_READY":
                // Control signal only; nothing to display
                break;
            case var cmd when cmd.StartsWith("GC_EVENT:"):
                // GC telemetry currently unused; suppress console noise
                break;
            case var cmd when cmd.StartsWith("MEMORY_STATS:"):
                // Memory telemetry currently unused; suppress console noise
                break;
            default:
                // Unknown command, treat as regular output
                viewModel.OutputLines.Enqueue(command);
                viewModel.TrimOutputLines();
                break;
        }
    }

    private void HandleExitMessage()
    {
        viewModel.ExitMessageReceived = true;
        if (string.IsNullOrWhiteSpace(viewModel.StatusMessage))
        {
            viewModel.StatusMessage = "PerfConverter requested a clean shutdown, waiting for perf to exit...";
        }
    }
}
