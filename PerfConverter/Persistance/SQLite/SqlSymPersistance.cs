using Dapper;
using PerfConverter.Entry;
using System.Data.Common;

namespace PerfConverter.Persistance.Sql;

public class SqlSymPersistance : IBatchPersistance<SymbolEntry>
{
    readonly DbConnection _connection;
    SqlSymPersistance(DbConnection connection) => _connection = connection;

    public async Task PersistAsync(IReadOnlyCollection<SymbolEntry> batch)
    {
        using var transaction = _connection.BeginTransaction();
        await _connection.ExecuteAsync(@"
                INSERT INTO SymbolsStr (Id, Symbol)
                VALUES (@Id, @Symbol)
            ", batch, transaction);
        transaction.Commit();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static IBatchPersistance<SymbolEntry> Create(DbConnection connection)
    {
        connection.Execute(@"
                CREATE TABLE IF NOT EXISTS SymbolsStr (
                    Id INTEGER PRIMARY KEY,
                    Symbol TEXT NOT NULL
                );
            ");

        return new SqlSymPersistance(connection);
    }
}
