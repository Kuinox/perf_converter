using System;
using System.Collections.Generic;
using Dapper;
using Microsoft.Data.Sqlite;

namespace PerfConverter;

public unsafe class SqlAddressProcessor : IAddressProcessor
{
    private readonly SqliteConnection _connection;

    private SqlAddressProcessor(SqliteConnection connection) => _connection = connection;

    public unsafe void ProcessAddress(PerfDlfilterFns* fns, long traceId, int pid, void* ctx)
    {
        var resolved = fns->resolve_addr(ctx);
        InsertResolved(resolved, traceId, pid, isIp: false);
    }

    public unsafe void ProcessIp(PerfDlfilterFns* fns, long traceId, int pid, void* ctx)
    {
        try
        {
            var resolved = fns->resolve_ip(ctx);
            Console.Error.WriteLine($"ResolveIp: resolved address: {(ulong)resolved:X}");
            if (resolved == null) return;

            InsertResolved(resolved, traceId, pid, isIp: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ResolveIp: Exception calling resolve_ip: {ex}");
        }
    }

    private unsafe void InsertResolved(PerfDlfilterAl* info, long traceId, int pid, bool isIp)
    {
        _connection.Execute(@"
            INSERT INTO Addresses (
                TraceId,
                Address, Pid, IsIp, Size, Symoff, Sym, SymStart, SymEnd,
                Dso, SymBinding, Is64Bit, IsKernelIp,
                BuildIdSize, BuildId, Filtered, Comm, Priv
            ) VALUES (
                @TraceId,
                @Address, @Pid, @IsIp, @Size, @Symoff, @Sym, @SymStart, @SymEnd,
                @Dso, @SymBinding, @Is64Bit, @IsKernelIp,
                @BuildIdSize, @BuildId, @Filtered, @Comm, @Priv
            );
        ", new
        {
            TraceId = traceId,
            Address = info->addr,
            Pid = pid,
            IsIp = isIp,
            Size = (int)info->size,
            Symoff = (int)info->symoff,
            Sym = (long)info->sym,
            SymStart = info->sym_start,
            SymEnd = info->sym_end,
            Dso = (long)info->dso,
            SymBinding = info->sym_binding,
            Is64Bit = info->is_64_bit,
            IsKernelIp = info->is_kernel_ip,
            BuildIdSize = (int)info->buildid_size,
            BuildId = (long)info->buildid,
            Filtered = info->filtered,
            Comm = (long)info->comm,
            Priv = (long)info->priv
        });
    }

    public static SqlAddressProcessor Create(SqliteConnection connection)
    {
        connection.Execute(@"
            CREATE TABLE Addresses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TraceId BIGINT NOT NULL,
                Address BIGINT NOT NULL,
                Pid INT NOT NULL,
                IsIp TINYINT NOT NULL,
                Size INT,
                Symoff INT,
                Sym BIGINT,
                SymStart BIGINT,
                SymEnd BIGINT,
                Dso BIGINT,
                SymBinding TINYINT,
                Is64Bit TINYINT,
                IsKernelIp TINYINT,
                BuildIdSize INT,
                BuildId BIGINT,
                Filtered TINYINT,
                Comm BIGINT,
                Priv BIGINT,
                FOREIGN KEY (TraceId) REFERENCES TraceSamples(Id)
            );
        ");

        return new SqlAddressProcessor(connection);
    }
}
