using Parquet;
using PerfConverter.Entry;
using Temp.Schema.Schema;

namespace PerfConverter.Persistence.ParquetDotNet;

public class ParquetTracePersistence(TraceSampleSchema schema, ParquetWriter writer, FileStream fileStream) : IBatchPersistence<TraceEntry>
{
    int _prevSize;
    public async Task PersistAsync(IReadOnlyCollection<TraceEntry> batch)
    {
        if (batch.Count == 0) return;

        if (batch.Count != _prevSize)
        {
            _prevSize = batch.Count;
            schema.Resize(batch.Count);
        }

        int i = 0;
        foreach (var entry in batch)
        {
            schema.Id.Buffer[i] = entry.Id;
            schema.PerfId.Buffer[i] = entry.PerfId;
            schema.Pid.Buffer[i] = entry.Pid;
            schema.Tid.Buffer[i] = entry.Tid;
            schema.Time.Buffer[i] = entry.Time;
            schema.Cpu.Buffer[i] = entry.Cpu;
            schema.Flags.Buffer[i] = (uint)entry.Flags;
            schema.Ip.Buffer[i] = entry.IpAddress;
            schema.Addr.Buffer[i] = entry.AddressAddress;
            schema.Period.Buffer[i] = entry.Period;
            schema.InsnCnt.Buffer[i] = entry.InsnCnt;
            schema.CycCnt.Buffer[i] = entry.CycCnt;
            schema.Weight.Buffer[i] = entry.Weight;
            schema.Cpumode.Buffer[i] = entry.Cpumode;
            schema.AddrCorrelatesSym.Buffer[i] = entry.AddrCorrelatesSym;
            schema.Event.Buffer[i] = entry.Event;
            schema.MachinePid.Buffer[i] = entry.MachinePid;
            schema.Vcpu.Buffer[i] = entry.Vcpu;
            schema.SourceFileName.Buffer[i] = entry.SourceFileName;
            schema.SourceLineNumber.Buffer[i] = entry.SourceLineNumber;
            schema.IpSymoff.Buffer[i] = entry.IpSymoff;
            schema.IpSym.Buffer[i] = entry.IpSym;
            schema.IpSymStart.Buffer[i] = entry.IpSymStart;
            schema.IpSymEnd.Buffer[i] = entry.IpSymEnd;
            schema.IpDso.Buffer[i] = entry.IpDso;
            schema.IpSymBinding.Buffer[i] = entry.IpSymBinding;
            schema.IpIs64Bit.Buffer[i] = entry.IpIs64Bit;
            schema.IpIsKernelIp.Buffer[i] = entry.IpIsKernelIp;
            schema.IpBuildId.Buffer[i] = entry.IpBuildId;
            schema.IpFiltered.Buffer[i] = entry.IpFiltered;
            schema.IpComm.Buffer[i] = entry.IpComm;
            schema.HaveAddress.Buffer[i] = entry.HaveAddress;
            schema.AddressSymoff.Buffer[i] = entry.AddressSymoff;
            schema.AddressSym.Buffer[i] = entry.AddressSym;
            schema.AddressSymStart.Buffer[i] = entry.AddressSymStart;
            schema.AddressSymEnd.Buffer[i] = entry.AddressSymEnd;
            schema.AddressDso.Buffer[i] = entry.AddressDso;
            schema.AddressSymBinding.Buffer[i] = entry.AddressSymBinding;
            schema.AddressIs64Bit.Buffer[i] = entry.AddressIs64Bit;
            schema.AddressIsKernelIp.Buffer[i] = entry.AddressIsKernelIp;
            schema.AddressBuildId.Buffer[i] = entry.AddressBuildId;
            schema.AddressFiltered.Buffer[i] = entry.AddressFiltered;
            schema.AddressComm.Buffer[i] = entry.AddressComm;

            i++;
        }
        await schema.Writer(writer);
    }

    public async ValueTask DisposeAsync()
    {
        await writer.DisposeAsync();
        await fileStream.DisposeAsync();
    }

    public static async Task<IBatchPersistence<TraceEntry>> Create(string filePath)
    {
        var schema = new TraceSampleSchema();

        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = await ParquetWriter.CreateAsync(schema.Schema, fileStream);

        return new ParquetTracePersistence(schema, writer, fileStream);
    }
}
