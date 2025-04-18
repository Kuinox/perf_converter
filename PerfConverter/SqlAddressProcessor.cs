using System;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Transactions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace PerfConverter;

public unsafe class SqlAddressProcessor : BackgroundBatching<SqlAddressProcessor.AddressEntry>, IAddressProcessor
{
    public struct AddressEntry
    {
        public ulong Id;
        public long TraceId;
        public ulong Address;
        public int Pid;
        public bool IsIp;
        public int Size;
        public int Symoff;
        public long SymStrId;
        public ulong SymStart;
        public ulong SymEnd;
        public long Dso;
        public byte SymBinding;
        public byte Is64Bit;
        public byte IsKernelIp;
        public int BuildIdSize;
        public long BuildId;
        public byte Filtered;
        public long Comm;
        public long Priv;
        public bool Used;
    }

    private ulong _currenAddress = 0;
    private readonly DbConnection _connection;
    private readonly SqlSymProcessor _sqlSymProcessor;

    private SqlAddressProcessor(DbConnection connection, SqlSymProcessor sqlSymProcessor) : base(20_000_000)
    {
        _connection = connection;
        _sqlSymProcessor = sqlSymProcessor;
    }

    public unsafe void ProcessAddress(PerfDlfilterFns* fns, long traceId, int pid, void* ctx)
    {
        var resolved = fns->resolve_addr(ctx);
        if (resolved != null)
        {
            Process(resolved, traceId, pid, isIp: false);
        }
    }

    public unsafe void ProcessIp(PerfDlfilterFns* fns, long traceId, int pid, void* ctx)
    {
        var resolved = fns->resolve_ip(ctx);
        if (resolved == null) return;

        Process(resolved, traceId, pid, isIp: true);
    }

    private unsafe void Process(PerfDlfilterAl* info, long traceId, int pid, bool isIp)
    {
        long symStrId = 0;
        if (info->sym != 0)
        {
            var str = Marshal.PtrToStringUTF8(info->sym)!;
            symStrId = _sqlSymProcessor.Process(str);
        }

        QueueItem(new AddressEntry
        {
            Id = _currenAddress++,
            TraceId = traceId,
            Address = info->addr,
            Pid = pid,
            IsIp = isIp,
            Size = (int)info->size,
            Symoff = (int)info->symoff,
            SymStrId = symStrId,
            SymStart = info->sym_start,
            SymEnd = info->sym_end,
            Dso = info->dso,
            SymBinding = info->sym_binding,
            Is64Bit = info->is_64_bit,
            IsKernelIp = info->is_kernel_ip,
            BuildIdSize = (int)info->buildid_size,
            BuildId = info->buildid,
            Filtered = info->filtered,
            Comm = info->comm,
            Priv = info->priv,
            Used = true
        });
    }

    protected override void BatchSend(IReadOnlyCollection<AddressEntry> batch)
    {
        using var transaction = _connection.BeginTransaction();

        _connection.Execute(@"
            INSERT INTO Addresses (
                Id,
                TraceId,
                Address, Pid, IsIp, Size, Symoff, SymStrId, SymStart, SymEnd,
                Dso, SymBinding, Is64Bit, IsKernelIp,
                BuildIdSize, BuildId, Filtered, Comm, Priv
            ) VALUES (
                $Id,
                $TraceId,
                $Address, $Pid, $IsIp, $Size, $Symoff, $SymStrId, $SymStart, $SymEnd,
                $Dso, $SymBinding, $Is64Bit, $IsKernelIp,
                $BuildIdSize, $BuildId, $Filtered, $Comm, $Priv
            );
        ", batch, transaction);
        transaction.Commit();
    }

    public static SqlAddressProcessor Create(DbConnection connection, SqlSymProcessor sqlSymProcessor)
    {
        connection.Execute(@"
            CREATE TABLE Addresses (
                Id BIGINT PRIMARY KEY,
                TraceId BIGINT NOT NULL,
                Address BIGINT NOT NULL,
                Pid INT NOT NULL,
                IsIp TINYINT NOT NULL,
                Size INT,
                Symoff INT,
                SymStrId BIGINT,
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

        return new SqlAddressProcessor(connection, sqlSymProcessor);
    }
}
