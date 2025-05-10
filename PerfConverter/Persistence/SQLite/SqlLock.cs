

namespace PerfConverter.Persistence.Sql;

public class SqlLock<T>(Lock sqlLock, IBatchPersistence<T> persistence) : IBatchPersistence<T>
{
    public async Task PersistAsync(IReadOnlyCollection<T> batch)
    {
        sqlLock.Enter();
        await persistence.PersistAsync(batch);
        sqlLock.Exit();
    }

    public ValueTask DisposeAsync() => persistence.DisposeAsync();

}
