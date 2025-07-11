namespace CLI;

public class MessageHandler(PerfMonitorViewModel viewModel, CommandProcessor commandProcessor)
{
    public void ProcessOutputMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

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

        if (IsCommand(message))
        {
            commandProcessor.ProcessCommand(message);
        }
        else
        {
            viewModel.ErrorLines.Enqueue($"[red]{message}[/]");
            viewModel.TrimErrorLines();
        }
    }

    private static bool IsCommand(string message)
    {
        return message.StartsWith("PROGRESS:") ||
               message.StartsWith("GC_EVENT:") ||
               message.StartsWith("MEMORY_STATS:") ||
               message.StartsWith("FILE_STATUS|") ||
               message.StartsWith("FILE_ACTIVITY|") ||
               message == "EXIT_MESSAGE";
    }
}