using System;
using System.Runtime.InteropServices;
using Dapper;
using Microsoft.Data.Sqlite;

namespace PerfConverter;

public unsafe class SqlAddressProcessor(SqliteConnection connection) : BackgroundBatching<SqlAddressProcessor.AddressEntry>(200_000), IAddressProcessor
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
        public long Sym;
        public string? SymStr;
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
        var symStr = info->sym != 0
            ? Marshal.PtrToStringUTF8((IntPtr)info->sym)
            : null;

        QueueItem(new AddressEntry
        {
            Id = _currenAddress++,
            TraceId = traceId,
            Address = info->addr,
            Pid = pid,
            IsIp = isIp,
            Size = (int)info->size,
            Symoff = (int)info->symoff,
            Sym = (long)info->sym,
            SymStr = symStr,
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
            Priv = (long)info->priv,
            Used = true
        });
    }

    protected override void BatchSend(IReadOnlyCollection<AddressEntry> batch)
    {
        connection.Execute(@"
            INSERT INTO Addresses (
                Id,
                TraceId,
                Address, Pid, IsIp, Size, Symoff, Sym, SymStr, SymStart, SymEnd,
                Dso, SymBinding, Is64Bit, IsKernelIp,
                BuildIdSize, BuildId, Filtered, Comm, Priv
            ) VALUES (
                @Id,
                @TraceId,
                @Address, @Pid, @IsIp, @Size, @Symoff, @Sym, @SymStr, @SymStart, @SymEnd,
                @Dso, @SymBinding, @Is64Bit, @IsKernelIp,
                @BuildIdSize, @BuildId, @Filtered, @Comm, @Priv
            );
        ", batch);
    }

    public static SqlAddressProcessor Create(SqliteConnection connection)
    {
        connection.Execute(@"
            CREATE TABLE Addresses (
                Id INTEGER PRIMARY KEY,
                TraceId BIGINT NOT NULL,
                Address BIGINT NOT NULL,
                Pid INT NOT NULL,
                IsIp TINYINT NOT NULL,
                Size INT,
                Symoff INT,
                Sym BIGINT,
                SymStr TEXT,
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
