using Dapper;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PerfConverter
{
    public class SqlSymProcessor : BackgroundBatching<SqlSymProcessor.SymbolEntry>
    {
        readonly Dictionary<string, long> _ids = [];
        readonly DbConnection connection;

        public struct SymbolEntry
        {
            public long Id;
            public string Symbol;
        }

        private SqlSymProcessor(DbConnection connection) : base(20_000_000)
        {
            this.connection = connection;
        }

        public long Process(string sym)
        {
            var defaultEntry = CollectionsMarshal.GetValueRefOrAddDefault(_ids, sym, out var exists);
            if (!exists)
            {
                defaultEntry = _ids.Count;
                QueueItem(new SymbolEntry { Id = defaultEntry, Symbol = sym });
            }
            return defaultEntry;
        }

        protected override void BatchSend(IReadOnlyCollection<SymbolEntry> batch)
        {
            var transaction = connection.BeginTransaction();
            connection.Execute(@"
                INSERT INTO SymbolsStr (Id, Symbol)
                VALUES (@Id, @Symbol)
            ", batch, transaction);
            transaction.Commit();
        }

        public static SqlSymProcessor Create(DbConnection connection)
        {
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS SymbolsStr (
                    Id INTEGER PRIMARY KEY,
                    Symbol TEXT NOT NULL
                );
            ");

            return new SqlSymProcessor(connection);
        }
    }
}
