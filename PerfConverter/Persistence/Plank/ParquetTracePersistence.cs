using PerfConverter.Entry;
using Temp.Schema.Schema;

namespace PerfConverter.Persistence.Plank;

public sealed class ParquetTracePersistence(TraceSampleRowSchema.PipelineWriter writer) : IPersister<TraceEntry>
{
    bool _disposed;

    public void Persist(TraceEntry entry)
    {
        var row = writer.GetRow();
        row.Id = entry.Id;
        row.PerfId = entry.PerfId;
        row.Pid = entry.Pid;
        row.Tid = entry.Tid;
        row.Time = entry.Time;
        row.Cpu = entry.Cpu;
        row.Flags = (uint)entry.Flags;
        row.Ip = entry.IpAddress;
        row.Addr = entry.AddressAddress;
        row.Period = entry.Period;
        row.InsnCnt = entry.InsnCnt;
        row.CycCnt = entry.CycCnt;
        row.Weight = entry.Weight;
        row.Cpumode = entry.Cpumode;
        row.AddrCorrelatesSym = entry.AddrCorrelatesSym;
        row.Event = entry.Event;
        row.MachinePid = entry.MachinePid;
        row.Vcpu = entry.Vcpu;
        row.SourceFileName = entry.SourceFileName ?? string.Empty;
        row.SourceLineNumber = entry.SourceLineNumber;
        row.IpSymoff = entry.IpSymoff;
        row.IpSym = entry.IpSym;
        row.IpSymStart = entry.IpSymStart;
        row.IpSymEnd = entry.IpSymEnd;
        row.IpDso = entry.IpDso;
        row.IpSymBinding = entry.IpSymBinding;
        row.IpIs64Bit = entry.IpIs64Bit;
        row.IpIsKernelIp = entry.IpIsKernelIp;
        row.IpBuildId = entry.IpBuildId;
        row.IpFiltered = entry.IpFiltered;
        row.IpComm = entry.IpComm;
        row.HaveAddress = entry.HaveAddress;
        row.AddressSymoff = entry.AddressSymoff;
        row.AddressSym = entry.AddressSym;
        row.AddressSymStart = entry.AddressSymStart;
        row.AddressSymEnd = entry.AddressSymEnd;
        row.AddressDso = entry.AddressDso;
        row.AddressSymBinding = entry.AddressSymBinding;
        row.AddressIs64Bit = entry.AddressIs64Bit;
        row.AddressIsKernelIp = entry.AddressIsKernelIp;
        row.AddressBuildId = entry.AddressBuildId;
        row.AddressFiltered = entry.AddressFiltered;
        row.AddressComm = entry.AddressComm;
        writer.Next();
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            writer.Complete();
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }

    public static Task<IPersister<TraceEntry>> Create(string filePath)
    {
        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
        var writer = TraceSampleRowSchema.CreateRowWriter(fileStream);

        return Task.FromResult<IPersister<TraceEntry>>(new ParquetTracePersistence(writer));
    }
}
