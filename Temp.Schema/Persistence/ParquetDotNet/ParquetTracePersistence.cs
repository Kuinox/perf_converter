using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using Temp.Core;
using Temp.Schema;

namespace PerfConverter.Persistence.ParquetDotNet;

public class ParquetTracePersistence : IBatchPersistence<TraceSampleEntry>
{
    readonly ParquetWriter _writer;
    readonly FileStream _fileStream;
    readonly TraceSampleSchema _schema;
    ParquetTracePersistence(TraceSampleSchema schema, ParquetWriter writer, FileStream fileStream)
    {
        _writer = writer;
        _fileStream = fileStream;
        _schema = schema;
    }

    int _prevSize;
    public async Task PersistAsync(IReadOnlyCollection<TraceSampleEntry> batch)
    {
        if (batch.Count == 0) return;

        if (batch.Count != _prevSize)
        {
            _prevSize = batch.Count;
            _schema.Resize(batch.Count);
        }

        int i = 0;
        foreach (var entry in batch)
        {
            _schema.Id.Buffer[i] = entry.Id;
            _schema.PerfId.Buffer[i] = entry.PerfId;
            _schema.Pid.Buffer[i] = entry.Pid;
            _schema.Tid.Buffer[i] = entry.Tid;
            _schema.Time.Buffer[i] = entry.Time;
            _schema.Cpu.Buffer[i] = entry.Cpu;
            _schema.Flags.Buffer[i] = (uint)entry.Flags;
            _schema.Ip.Buffer[i] = entry.IpAddress;
            _schema.Addr.Buffer[i] = entry.AddressAddress;
            _schema.Period.Buffer[i] = entry.Period;
            _schema.InsnCnt.Buffer[i] = entry.InsnCnt;
            _schema.CycCnt.Buffer[i] = entry.CycCnt;
            _schema.Weight.Buffer[i] = entry.Weight;
            _schema.Cpumode.Buffer[i] = entry.Cpumode;
            _schema.AddrCorrelatesSym.Buffer[i] = entry.AddrCorrelatesSym;
            _schema.Event.Buffer[i] = entry.Event;
            _schema.MachinePid.Buffer[i] = entry.MachinePid;
            _schema.Vcpu.Buffer[i] = entry.Vcpu;
            _schema.IpSymoff.Buffer[i] = entry.IpSymoff;
            _schema.IpSym.Buffer[i] = entry.IpSym;
            _schema.IpSymStart.Buffer[i] = entry.IpSymStart;
            _schema.IpSymEnd.Buffer[i] = entry.IpSymEnd;
            _schema.IpDso.Buffer[i] = entry.IpDso;
            _schema.IpSymBinding.Buffer[i] = entry.IpSymBinding;
            _schema.IpIs64Bit.Buffer[i] = entry.IpIs64Bit;
            _schema.IpIsKernelIp.Buffer[i] = entry.IpIsKernelIp;
            _schema.IpBuildId.Buffer[i] = entry.IpBuildId;
            _schema.IpFiltered.Buffer[i] = entry.IpFiltered;
            _schema.IpComm.Buffer[i] = entry.IpComm;
            _schema.HaveAddress.Buffer[i] = entry.HaveAddress;
            _schema.AddressSymoff.Buffer[i] = entry.AddressSymoff;
            _schema.AddressSym.Buffer[i] = entry.AddressSym;
            _schema.AddressSymStart.Buffer[i] = entry.AddressSymStart;
            _schema.AddressSymEnd.Buffer[i] = entry.AddressSymEnd;
            _schema.AddressDso.Buffer[i] = entry.AddressDso;
            _schema.AddressSymBinding.Buffer[i] = entry.AddressSymBinding;
            _schema.AddressIs64Bit.Buffer[i] = entry.AddressIs64Bit;
            _schema.AddressIsKernelIp.Buffer[i] = entry.AddressIsKernelIp;
            _schema.AddressBuildId.Buffer[i] = entry.AddressBuildId;
            _schema.AddressFiltered.Buffer[i] = entry.AddressFiltered;
            _schema.AddressComm.Buffer[i] = entry.AddressComm;

            i++;
        }
        await _schema.Writer(_writer);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        await _fileStream.DisposeAsync();
    }

    public static async Task<IBatchPersistence<TraceSampleEntry>> Create(string basePath, CompressionMethod compressionMethod)
    {
        var schema = new TraceSampleSchema();
        Directory.CreateDirectory(basePath);
        var filePath = Path.Combine(basePath, "tracesamples.parquet");

        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema.Schema, fileStream);
        writer.CompressionMethod = compressionMethod;

        return new ParquetTracePersistence(schema, writer, fileStream);
    }
}