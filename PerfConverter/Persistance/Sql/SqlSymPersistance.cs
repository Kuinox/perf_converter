using Dapper;
using PerfConverter.Persistance;
using System.Data.Common;

namespace PerfConverter.Persistance.Sql
{
    public class SqlSymPersistance : ISymPersistance
    {
        readonly DbConnection _connection;
        private SqlSymPersistance(DbConnection connection) => _connection = connection;
        public static SqlSymPersistance Create(DbConnection connection)
        {
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS SymbolsStr (
                    Id INTEGER PRIMARY KEY,
                    Symbol TEXT NOT NULL
                );
            ");

            return new SqlSymPersistance(connection);
        }
        public void Persist(IReadOnlyCollection<SymbolEntry> batch)
        {
            using var transaction = _connection.BeginTransaction();
            _connection.Execute(@"
                INSERT INTO SymbolsStr (Id, Symbol)
                VALUES (@Id, @Symbol)
            ", batch, transaction);
            transaction.Commit();
        }
    }
}
