using Temp.Core;

namespace PerfConverter;

public class StatePersister : IPersister<int>
{
    readonly string _filePath;
    public StatePersister(string filePath)
    {
        _filePath = filePath;
    }

    public void Persist(int val)
    {
        File.WriteAllText(_filePath, val.ToString());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
