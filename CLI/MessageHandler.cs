namespace CLI;

public class MessageHandler(PerfMonitorViewModel viewModel, CommandProcessor commandProcessor)
{
    private static readonly string[] IgnoredPrefixes =
    [
        "POOL_SIZE|",
        "FLUSH_TIMING|"
    ];

    public void ProcessOutputMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (ShouldIgnore(message)) return;

        if (IsCommand(message))
        {
            commandProcessor.ProcessCommand(message);
        }
        else
        {
            viewModel.OutputLines.Enqueue(message);
            viewModel.TrimOutputLines();
        }
    }

    public void ProcessErrorMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (ShouldIgnore(message)) return;

        if (IsCommand(message))
        {
            commandProcessor.ProcessCommand(message);
        }
        else
        {
            viewModel.RawErrorLines.Enqueue(message);
            viewModel.ErrorLines.Enqueue(message);
            viewModel.TrimErrorLines();
        }
    }

    private static bool IsCommand(string message)
    {
        return message.StartsWith("GC_EVENT:") ||
               message.StartsWith("MEMORY_STATS:") ||
               message.StartsWith("EXIT_MESSAGE") ||
               message.StartsWith("DOTNET_READY");
    }

    private static bool ShouldIgnore(string message)
    {
        foreach (var prefix in IgnoredPrefixes)
        {
            if (message.StartsWith(prefix)) return true;
        }
        return false;
    }
}
