namespace PerfConverter;

public class ThreadState
{
    class StackInfo
    {

    }
    readonly Stack<StackInfo> _stack = new();
    public void Call()
    {
        var stackInfo = new StackInfo();
        _stack.Push(stackInfo);
    }

    public void Return()
    {
        _stack.Pop();
    }
}

