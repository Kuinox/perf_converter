using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class TraceSampleRowSchema
{
    [ParquetColumn("id", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong Id { get; set; }

    [ParquetColumn("perfId", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong PerfId { get; set; }

    [ParquetColumn("pid", Encodings = new[] { EncodingKind.RleDictionary })]
    public uint Pid { get; set; }

    [ParquetColumn("tid", Encodings = new[] { EncodingKind.RleDictionary })]
    public uint Tid { get; set; }

    [ParquetColumn("time", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong Time { get; set; }

    [ParquetColumn("cpu", Encodings = new[] { EncodingKind.RleDictionary })]
    public uint Cpu { get; set; }

    [ParquetColumn("flags", Encodings = new[] { EncodingKind.RleDictionary })]
    public uint Flags { get; set; }

    [ParquetColumn("ip", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong Ip { get; set; }

    [ParquetColumn("addr", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong Addr { get; set; }

    [ParquetColumn("period", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong Period { get; set; }

    [ParquetColumn("insnCnt", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong InsnCnt { get; set; }

    [ParquetColumn("cycCnt", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong CycCnt { get; set; }

    [ParquetColumn("weight", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong Weight { get; set; }

    [ParquetColumn("cpumode", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte Cpumode { get; set; }

    [ParquetColumn("addrCorrelatesSym", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte AddrCorrelatesSym { get; set; }

    [ParquetColumn("event", Encodings = new[] { EncodingKind.RleDictionary })]
    public ReadOnlyMemory<byte> Event { get; set; }

    [ParquetColumn("machinePid", Encodings = new[] { EncodingKind.RleDictionary })]
    public uint MachinePid { get; set; }

    [ParquetColumn("vcpu", Encodings = new[] { EncodingKind.RleDictionary })]
    public uint Vcpu { get; set; }

    [ParquetColumn("srcFileName", Encodings = new[] { EncodingKind.RleDictionary })]
    public ReadOnlyMemory<byte>? SourceFileName { get; set; }

    [ParquetColumn("srcLineNumber", Encodings = new[] { EncodingKind.RleDictionary })]
    public uint SourceLineNumber { get; set; }

    [ParquetColumn("ipSymoff", Encodings = new[] { EncodingKind.Plain })]
    public uint IpSymoff { get; set; }

    [ParquetColumn("ipSym", Encodings = new[] { EncodingKind.RleDictionary })]
    public ReadOnlyMemory<byte>? IpSym { get; set; }

    [ParquetColumn("ipSymStart", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong IpSymStart { get; set; }

    [ParquetColumn("ipSymEnd", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong IpSymEnd { get; set; }

    [ParquetColumn("ipDso", Encodings = new[] { EncodingKind.RleDictionary })]
    public ReadOnlyMemory<byte>? IpDso { get; set; }

    [ParquetColumn("ipSymBinding", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte IpSymBinding { get; set; }

    [ParquetColumn("ipIs64Bit", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte IpIs64Bit { get; set; }

    [ParquetColumn("ipIsKernelIp", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte IpIsKernelIp { get; set; }

    [ParquetColumn("ipBuildId", Encodings = new[] { EncodingKind.Plain })]
    public ReadOnlyMemory<byte> IpBuildId { get; set; }

    [ParquetColumn("ipFiltered", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte IpFiltered { get; set; }

    [ParquetColumn("ipComm", Encodings = new[] { EncodingKind.RleDictionary })]
    public ReadOnlyMemory<byte>? IpComm { get; set; }

    [ParquetColumn("haveAddress", Encodings = new[] { EncodingKind.RleDictionary })]
    public bool HaveAddress { get; set; }

    [ParquetColumn("addressSymoff", Encodings = new[] { EncodingKind.Plain })]
    public uint AddressSymoff { get; set; }

    [ParquetColumn("addressSym", Encodings = new[] { EncodingKind.RleDictionary })]
    public ReadOnlyMemory<byte>? AddressSym { get; set; }

    [ParquetColumn("addressSymStart", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong AddressSymStart { get; set; }

    [ParquetColumn("addressSymEnd", Encodings = new[] { EncodingKind.DeltaBinaryPacked })]
    public ulong AddressSymEnd { get; set; }

    [ParquetColumn("addressDso", Encodings = new[] { EncodingKind.RleDictionary })]
    public ReadOnlyMemory<byte>? AddressDso { get; set; }

    [ParquetColumn("addressSymBinding", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte AddressSymBinding { get; set; }

    [ParquetColumn("addressIs64Bit", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte AddressIs64Bit { get; set; }

    [ParquetColumn("addressIsKernelIp", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte AddressIsKernelIp { get; set; }

    [ParquetColumn("addressBuildId", Encodings = new[] { EncodingKind.Plain })]
    public ReadOnlyMemory<byte>? AddressBuildId { get; set; }

    [ParquetColumn("addressFiltered", Encodings = new[] { EncodingKind.RleDictionary })]
    public byte AddressFiltered { get; set; }

    [ParquetColumn("addressComm", Encodings = new[] { EncodingKind.RleDictionary })]
    public ReadOnlyMemory<byte>? AddressComm { get; set; }
}
