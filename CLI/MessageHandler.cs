namespace CLI;

public class MessageHandler
{
    private readonly PerfMonitorViewModel _viewModel;
    private readonly CommandProcessor _commandProcessor;

    public MessageHandler(PerfMonitorViewModel viewModel, CommandProcessor commandProcessor)
    {
        _viewModel = viewModel;
        _commandProcessor = commandProcessor;
    }

    public void ProcessOutputMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (IsCommand(message))
        {
            _commandProcessor.ProcessCommand(message);
        }
        else
        {
            _viewModel.OutputLines.Enqueue(message);
            _viewModel.TrimOutputLines();
        }
    }

    public void ProcessErrorMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (IsCommand(message))
        {
            _commandProcessor.ProcessCommand(message);
        }
        else
        {
            _viewModel.ErrorLines.Enqueue($"[red]{message}[/]");
            _viewModel.TrimErrorLines();
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