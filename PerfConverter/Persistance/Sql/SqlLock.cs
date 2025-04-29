namespace PerfConverter.Persistance.Sql;

class SqlLock
{
    private readonly static Lock _lock = new();

    public static void Wait()
    {
        _lock.Enter();
    }
    public static void Release()
    {
        _lock.Exit();
    }
}
