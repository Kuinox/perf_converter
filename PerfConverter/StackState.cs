namespace PerfConverter;

public class ThreadState
{
    public class StackInfo
    {
        public string? SymbolName { get; set; }
        public ulong Address { get; set; }
        public ulong Time { get; set; }
        public ulong InstrCount { get; set; }
        public ulong CycleCount { get; set; }
        
        public StackInfo(string? symbolName, ulong address, ulong time, ulong instrCount, ulong cycleCount)
        {
            SymbolName = symbolName;
            Address = address;
            Time = time;
            InstrCount = instrCount;
            CycleCount = cycleCount;
        }
    }
    
    readonly Stack<StackInfo> _stack = new();
    
    public StackInfo? LastReturnedFrame { get; set; }
    public ulong LastBranchIp { get; set; }
    
    public void Call(string? symbolName, ulong address, ulong time, ulong instrCount, ulong cycleCount)
    {
        var stackInfo = new StackInfo(symbolName, address, time, instrCount, cycleCount);
        _stack.Push(stackInfo);
    }

    public StackInfo? Return()
    {
        if (_stack.Count > 0)
        {
            var frame = _stack.Pop();
            LastReturnedFrame = frame;
            return frame;
        }
        return null;
    }
    
    public int StackDepth => _stack.Count;
    
    public StackInfo? CurrentFrame => _stack.Count > 0 ? _stack.Peek() : null;
    
    public IEnumerable<StackInfo> GetStackFrames()
    {
        return _stack.ToArray().Reverse();
    }
}