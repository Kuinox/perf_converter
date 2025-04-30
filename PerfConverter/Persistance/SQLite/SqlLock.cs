

namespace PerfConverter.Persistance.Sql;

public class SqlLock<T>(Lock sqlLock, IBatchPersistance<T> persistance) : IBatchPersistance<T>
{
    public async Task PersistAsync(IReadOnlyCollection<T> batch)
    {
        sqlLock.Enter();
        await persistance.PersistAsync(batch);
        sqlLock.Exit();
    }

    public ValueTask DisposeAsync() => persistance.DisposeAsync();

}
