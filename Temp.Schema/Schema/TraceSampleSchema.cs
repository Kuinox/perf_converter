using PerfConverter.Entry;
using PerfConverter.Schema;
using Plank.Schema;
using Plank.Writing;

namespace Temp.Schema.Schema;

public class TraceSampleSchema
{
    static readonly ColumnOptions DeltaOnly = new(encodings: [EncodingKind.DeltaBinaryPacked]);
    static readonly ColumnOptions DictOnly = new(encodings: [EncodingKind.RleDictionary]);
    static readonly ColumnOptions PlainOnly = new(encodings: [EncodingKind.Plain]);
    static readonly ColumnOptions OptionalDictOnly = new(ParquetRepetition.Optional, [EncodingKind.RleDictionary]);

    public TraceSampleSchema()
    {
        Schema = new([
            Id.Column,
            PerfId.Column,
            Pid.Column,
            Tid.Column,
            Time.Column,
            Cpu.Column,
            Flags.Column,
            Ip.Column,
            Addr.Column,
            Period.Column,
            InsnCnt.Column,
            CycCnt.Column,
            Weight.Column,
            Cpumode.Column,
            AddrCorrelatesSym.Column,
            Event.Column,
            MachinePid.Column,
            Vcpu.Column,
            SourceFileName.Column,
            SourceLineNumber.Column,
            IpSymoff.Column,
            IpSym.Column,
            IpSymStart.Column,
            IpSymEnd.Column,
            IpDso.Column,
            IpSymBinding.Column,
            IpIs64Bit.Column,
            IpIsKernelIp.Column,
            IpBuildId.Column,
            IpFiltered.Column,
            IpComm.Column,
            HaveAddress.Column,
            AddressSymoff.Column,
            AddressSym.Column,
            AddressSymStart.Column,
            AddressSymEnd.Column,
            AddressDso.Column,
            AddressSymBinding.Column,
            AddressIs64Bit.Column,
            AddressIsKernelIp.Column,
            AddressBuildId.Column,
            AddressFiltered.Column,
            AddressComm.Column
        ]);
    }

    public PlankColumn<ulong> Id { get; } = new("id", DeltaOnly);
    public PlankColumn<ulong> PerfId { get; } = new("perfId", DeltaOnly);
    public PlankColumn<uint> Pid { get; } = new("pid", DictOnly);
    public PlankColumn<uint> Tid { get; } = new("tid", DictOnly);
    public PlankColumn<ulong> Time { get; } = new("time", DeltaOnly);
    public PlankColumn<uint> Cpu { get; } = new("cpu", DictOnly);
    public PlankColumn<uint> Flags { get; } = new("flags", DictOnly);
    public PlankColumn<ulong> Ip { get; } = new("ip", DeltaOnly);
    public PlankColumn<ulong> Addr { get; } = new("addr", DeltaOnly);
    public PlankColumn<ulong> Period { get; } = new("period", DeltaOnly);
    public PlankColumn<ulong> InsnCnt { get; } = new("insnCnt", DeltaOnly);
    public PlankColumn<ulong> CycCnt { get; } = new("cycCnt", DeltaOnly);
    public PlankColumn<ulong> Weight { get; } = new("weight", DeltaOnly);
    public PlankColumn<byte> Cpumode { get; } = new("cpumode", DictOnly);
    public PlankColumn<byte> AddrCorrelatesSym { get; } = new("addrCorrelatesSym", DictOnly);
    public PlankColumn<ReadOnlyMemory<byte>> Event { get; } = new("event", DictOnly);
    public PlankColumn<uint> MachinePid { get; } = new("machinePid", DictOnly);
    public PlankColumn<uint> Vcpu { get; } = new("vcpu", DictOnly);
    public PlankColumn<ReadOnlyMemory<byte>?> SourceFileName { get; } = new("srcFileName", OptionalDictOnly);
    public PlankColumn<uint> SourceLineNumber { get; } = new("srcLineNumber", DictOnly);
    public PlankColumn<uint> IpSymoff { get; } = new("ipSymoff", PlainOnly);
    public PlankColumn<ReadOnlyMemory<byte>?> IpSym { get; } = new("ipSym", OptionalDictOnly);
    public PlankColumn<ulong> IpSymStart { get; } = new("ipSymStart", DeltaOnly);
    public PlankColumn<ulong> IpSymEnd { get; } = new("ipSymEnd", DeltaOnly);
    public PlankColumn<ReadOnlyMemory<byte>?> IpDso { get; } = new("ipDso", OptionalDictOnly);
    public PlankColumn<byte> IpSymBinding { get; } = new("ipSymBinding", DictOnly);
    public PlankColumn<byte> IpIs64Bit { get; } = new("ipIs64Bit", DictOnly);
    public PlankColumn<byte> IpIsKernelIp { get; } = new("ipIsKernelIp", DictOnly);
    public PlankColumn<ReadOnlyMemory<byte>> IpBuildId { get; } = new("ipBuildId", PlainOnly);
    public PlankColumn<byte> IpFiltered { get; } = new("ipFiltered", DictOnly);
    public PlankColumn<ReadOnlyMemory<byte>?> IpComm { get; } = new("ipComm", OptionalDictOnly);
    public PlankColumn<bool> HaveAddress { get; } = new("haveAddress", DictOnly);
    public PlankColumn<uint> AddressSymoff { get; } = new("addressSymoff", PlainOnly);
    public PlankColumn<ReadOnlyMemory<byte>?> AddressSym { get; } = new("addressSym", OptionalDictOnly);
    public PlankColumn<ulong> AddressSymStart { get; } = new("addressSymStart", DeltaOnly);
    public PlankColumn<ulong> AddressSymEnd { get; } = new("addressSymEnd", DeltaOnly);
    public PlankColumn<ReadOnlyMemory<byte>?> AddressDso { get; } = new("addressDso", OptionalDictOnly);
    public PlankColumn<byte> AddressSymBinding { get; } = new("addressSymBinding", DictOnly);
    public PlankColumn<byte> AddressIs64Bit { get; } = new("addressIs64Bit", DictOnly);
    public PlankColumn<byte> AddressIsKernelIp { get; } = new("addressIsKernelIp", DictOnly);
    public PlankColumn<ReadOnlyMemory<byte>?> AddressBuildId { get; } = new("addressBuildId", PlainOnly);
    public PlankColumn<byte> AddressFiltered { get; } = new("addressFiltered", DictOnly);
    public PlankColumn<ReadOnlyMemory<byte>?> AddressComm { get; } = new("addressComm", OptionalDictOnly);

    void WriteTo(ParquetWriter writer)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var groupWriter = writer.StartRowGroup();

        Id.Write(groupWriter);
        PerfId.Write(groupWriter);
        Pid.Write(groupWriter);
        Tid.Write(groupWriter);
        Time.Write(groupWriter);
        Cpu.Write(groupWriter);
        Flags.Write(groupWriter);
        Ip.Write(groupWriter);
        Addr.Write(groupWriter);
        Period.Write(groupWriter);
        InsnCnt.Write(groupWriter);
        CycCnt.Write(groupWriter);
        Weight.Write(groupWriter);
        Cpumode.Write(groupWriter);
        AddrCorrelatesSym.Write(groupWriter);
        Event.Write(groupWriter);
        MachinePid.Write(groupWriter);
        Vcpu.Write(groupWriter);
        SourceFileName.Write(groupWriter);
        SourceLineNumber.Write(groupWriter);
        IpSymoff.Write(groupWriter);
        IpSym.Write(groupWriter);
        IpSymStart.Write(groupWriter);
        IpSymEnd.Write(groupWriter);
        IpDso.Write(groupWriter);
        IpSymBinding.Write(groupWriter);
        IpIs64Bit.Write(groupWriter);
        IpIsKernelIp.Write(groupWriter);
        IpBuildId.Write(groupWriter);
        IpFiltered.Write(groupWriter);
        IpComm.Write(groupWriter);
        HaveAddress.Write(groupWriter);
        AddressSymoff.Write(groupWriter);
        AddressSym.Write(groupWriter);
        AddressSymStart.Write(groupWriter);
        AddressSymEnd.Write(groupWriter);
        AddressDso.Write(groupWriter);
        AddressSymBinding.Write(groupWriter);
        AddressIs64Bit.Write(groupWriter);
        AddressIsKernelIp.Write(groupWriter);
        AddressBuildId.Write(groupWriter);
        AddressFiltered.Write(groupWriter);
        AddressComm.Write(groupWriter);

        Console.Error.WriteLine($"FLUSH_TIMING|RowGroup written in {sw.ElapsedMilliseconds}ms|{Id.ActiveLength} rows");
    }

    public PlankParquetFileWriter CreateWriter(Stream stream)
        => PlankParquetFileWriter.Create(stream, Schema);

    public void Writer(PlankParquetFileWriter writer)
    {
        WriteTo(writer.Writer);
    }

    public IAsyncEnumerable<TraceEntry> ReadAll(string path)
        => throw new NotSupportedException("Parquet reading needs a Plank reader; Parquet.Net usage has been removed.");

    public void Resize(int newSize)
    {
        Id.Resize(newSize);
        PerfId.Resize(newSize);
        Pid.Resize(newSize);
        Tid.Resize(newSize);
        Time.Resize(newSize);
        Cpu.Resize(newSize);
        Flags.Resize(newSize);
        Ip.Resize(newSize);
        Addr.Resize(newSize);
        Period.Resize(newSize);
        InsnCnt.Resize(newSize);
        CycCnt.Resize(newSize);
        Weight.Resize(newSize);
        Cpumode.Resize(newSize);
        AddrCorrelatesSym.Resize(newSize);
        Event.Resize(newSize);
        MachinePid.Resize(newSize);
        Vcpu.Resize(newSize);
        SourceFileName.Resize(newSize);
        SourceLineNumber.Resize(newSize);
        IpSymoff.Resize(newSize);
        IpSym.Resize(newSize);
        IpSymStart.Resize(newSize);
        IpSymEnd.Resize(newSize);
        IpDso.Resize(newSize);
        IpSymBinding.Resize(newSize);
        IpIs64Bit.Resize(newSize);
        IpIsKernelIp.Resize(newSize);
        IpBuildId.Resize(newSize);
        IpFiltered.Resize(newSize);
        IpComm.Resize(newSize);
        HaveAddress.Resize(newSize);
        AddressSymoff.Resize(newSize);
        AddressSym.Resize(newSize);
        AddressSymStart.Resize(newSize);
        AddressSymEnd.Resize(newSize);
        AddressDso.Resize(newSize);
        AddressSymBinding.Resize(newSize);
        AddressIs64Bit.Resize(newSize);
        AddressIsKernelIp.Resize(newSize);
        AddressBuildId.Resize(newSize);
        AddressFiltered.Resize(newSize);
        AddressComm.Resize(newSize);
    }

    public ParquetSchema Schema { get; }
}
