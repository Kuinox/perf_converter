using System;
using System.Collections.Generic;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace PerfConverter;

public unsafe class SqlAddressProcessor : IAddressProcessor
{
    private readonly HashSet<ulong> _addresses = [];
    private readonly SqliteConnection _connection;

    private SqlAddressProcessor(SqliteConnection connection) => _connection = connection;

    public unsafe void HandleAdress(PerfDlfilterFns* fns, int pid, void* addr)
    {
        var casted = (ulong)addr;
        if (!_addresses.Add(casted)) return;

        var resolved = fns->resolve_addr(addr);
        if (resolved == null) return;

        InsertResolved(resolved, pid, isIp: false);
    }

    public unsafe void ResolveIp(PerfDlfilterFns* fns, int pid, void* ip)
    {
        if (!_addresses.Add((ulong)ip)) return;

        var resolved = fns->resolve_ip(ip);
        if (resolved == null) return;

        InsertResolved(resolved, pid, isIp: true);
    }

    private unsafe void InsertResolved(PerfDlfilterAl* info, int pid, bool isIp)
    {
        _connection.Execute(@"
                INSERT INTO Addresses (
                    Address, Pid, IsIp, Size, Symoff, Sym, SymStart, SymEnd,
                    Dso, SymBinding, Is64Bit, IsKernelIp,
                    BuildIdSize, BuildId, Filtered, Comm, Priv
                ) VALUES (
                    @Address, @Pid, @IsIp, @Size, @Symoff, @Sym, @SymStart, @SymEnd,
                    @Dso, @SymBinding, @Is64Bit, @IsKernelIp,
                    @BuildIdSize, @BuildId, @Filtered, @Comm, @Priv
                );
            ", new
        {
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
                    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
                    Address BIGINT NOT NULL,
                    Pid INT NOT NULL,
                    IsIp BIT NOT NULL,
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
                    Priv BIGINT
                );
            ");

        return new SqlAddressProcessor(connection);
    }
}
