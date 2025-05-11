using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Persistence;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PerfMetadataExtract;

public class ParquetAuxLostPersistence : IBatchPersistence<AuxDataLostEntry>
{
    private ParquetSchema _schema;
    private ParquetWriter _writer;
    private FileStream _fileStream;

    ulong[] _times;
    ulong[] _pids;
    ulong[] _tid;
    ulong[] _cpu;
    ulong[] _flags;

    private ParquetAuxLostPersistence(ParquetSchema schema, ParquetWriter writer, FileStream fileStream)
    {
        ResizeArrays(0);
        _schema = schema;
        _writer = writer;
        _fileStream = fileStream;
    }

    public async Task PersistAsync(IReadOnlyCollection<AuxDataLostEntry> batch)
    {
        int count = batch.Count;

        ResizeArrays(count);

        int i = 0;
        foreach (var entry in batch)
        {
            _times[i] = entry.Time;
            _pids[i] = entry.Pid;
            _tid[i] = entry.Tid;
            _cpu[i] = entry.Cpu;
            _flags[i] = entry.Flags;
            i++;
        }

        var timesColumn = new DataColumn(_schema.DataFields[0], _times);
        var pidsColumn = new DataColumn(_schema.DataFields[1], _pids);
        var tidColumn = new DataColumn(_schema.DataFields[2], _tid);
        var cpuColumn = new DataColumn(_schema.DataFields[3], _cpu);
        var flagsColumn = new DataColumn(_schema.DataFields[4], _flags);

        using var groupWriter = _writer.CreateRowGroup();
        await groupWriter.WriteColumnAsync(timesColumn);
        await groupWriter.WriteColumnAsync(pidsColumn);
        await groupWriter.WriteColumnAsync(tidColumn);
        await groupWriter.WriteColumnAsync(cpuColumn);
        await groupWriter.WriteColumnAsync(flagsColumn);
    }

    [MemberNotNull(
        nameof(_times),
        nameof(_pids),
        nameof(_tid),
        nameof(_cpu),
        nameof(_flags))
        ]
    void ResizeArrays(int newSize)
    {
        if (_times != null && _times.Length == newSize)
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
            return;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.
        _times = new ulong[newSize];
        _pids = new ulong[newSize];
        _tid = new ulong[newSize];
        _cpu = new ulong[newSize];
        _flags = new ulong[newSize];
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _fileStream.DisposeAsync();
    }

    public static async Task<ParquetAuxLostPersistence> Create(string filePath, CompressionMethod compressionMethod)
    {
        var schema = new ParquetSchema(
            new DataField<ulong>("time"),
            new DataField<ulong>("pid"),
            new DataField<ulong>("tid"),
            new DataField<ulong>("cpu"),
            new DataField<ulong>("flags")
        );

        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema, fileStream);

        writer.CompressionMethod = compressionMethod;

        return new ParquetAuxLostPersistence(schema, writer, fileStream);
    }
}
