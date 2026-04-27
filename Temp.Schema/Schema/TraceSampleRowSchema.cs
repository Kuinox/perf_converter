using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class TraceSampleRowSchema
{
    [ParquetColumn("id", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Id { get; set; }

    [ParquetColumn("perfId", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong PerfId { get; set; }

    [ParquetColumn("pid", Encodings = [EncodingKind.RleDictionary])]
    public uint Pid { get; set; }

    [ParquetColumn("tid", Encodings = [EncodingKind.RleDictionary])]
    public uint Tid { get; set; }

    [ParquetColumn("time", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Time { get; set; }

    [ParquetColumn("cpu", Encodings = [EncodingKind.RleDictionary])]
    public uint Cpu { get; set; }

    [ParquetColumn("flags", Encodings = [EncodingKind.RleDictionary])]
    public uint Flags { get; set; }

    [ParquetColumn("ip", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Ip { get; set; }

    [ParquetColumn("addr", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Addr { get; set; }

    [ParquetColumn("period", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Period { get; set; }

    [ParquetColumn("insnCnt", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong InsnCnt { get; set; }

    [ParquetColumn("cycCnt", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong CycCnt { get; set; }

    [ParquetColumn("weight", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong Weight { get; set; }

    [ParquetColumn("cpumode", Encodings = [EncodingKind.RleDictionary])]
    public byte Cpumode { get; set; }

    [ParquetColumn("addrCorrelatesSym", Encodings = [EncodingKind.RleDictionary])]
    public byte AddrCorrelatesSym { get; set; }

    [ParquetColumn("event", Encodings = [EncodingKind.RleDictionary])]
    public string? Event { get; set; }

    [ParquetColumn("machinePid", Encodings = [EncodingKind.RleDictionary])]
    public uint MachinePid { get; set; }

    [ParquetColumn("vcpu", Encodings = [EncodingKind.RleDictionary])]
    public uint Vcpu { get; set; }

    [ParquetColumn("srcFileName", Encodings = [EncodingKind.RleDictionary])]
    public string SourceFileName { get; set; } = string.Empty;

    [ParquetColumn("srcLineNumber", Encodings = [EncodingKind.RleDictionary])]
    public uint SourceLineNumber { get; set; }

    [ParquetColumn("ipSymoff", Encodings = [EncodingKind.Plain])]
    public uint IpSymoff { get; set; }

    [ParquetColumn("ipSym", Encodings = [EncodingKind.RleDictionary])]
    public string? IpSym { get; set; }

    [ParquetColumn("ipSymStart", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong IpSymStart { get; set; }

    [ParquetColumn("ipSymEnd", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong IpSymEnd { get; set; }

    [ParquetColumn("ipDso", Encodings = [EncodingKind.RleDictionary])]
    public string? IpDso { get; set; }

    [ParquetColumn("ipSymBinding", Encodings = [EncodingKind.RleDictionary])]
    public byte IpSymBinding { get; set; }

    [ParquetColumn("ipIs64Bit", Encodings = [EncodingKind.RleDictionary])]
    public byte IpIs64Bit { get; set; }

    [ParquetColumn("ipIsKernelIp", Encodings = [EncodingKind.RleDictionary])]
    public byte IpIsKernelIp { get; set; }

    [ParquetColumn("ipBuildId", Encodings = [EncodingKind.Plain])]
    public byte[] IpBuildId { get; set; } = [];

    [ParquetColumn("ipFiltered", Encodings = [EncodingKind.RleDictionary])]
    public byte IpFiltered { get; set; }

    [ParquetColumn("ipComm", Encodings = [EncodingKind.RleDictionary])]
    public string? IpComm { get; set; }

    [ParquetColumn("haveAddress", Encodings = [EncodingKind.RleDictionary])]
    public bool HaveAddress { get; set; }

    [ParquetColumn("addressSymoff", Encodings = [EncodingKind.Plain])]
    public uint AddressSymoff { get; set; }

    [ParquetColumn("addressSym", Encodings = [EncodingKind.RleDictionary])]
    public string? AddressSym { get; set; }

    [ParquetColumn("addressSymStart", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong AddressSymStart { get; set; }

    [ParquetColumn("addressSymEnd", Encodings = [EncodingKind.DeltaBinaryPacked])]
    public ulong AddressSymEnd { get; set; }

    [ParquetColumn("addressDso", Encodings = [EncodingKind.RleDictionary])]
    public string? AddressDso { get; set; }

    [ParquetColumn("addressSymBinding", Encodings = [EncodingKind.RleDictionary])]
    public byte AddressSymBinding { get; set; }

    [ParquetColumn("addressIs64Bit", Encodings = [EncodingKind.RleDictionary])]
    public byte AddressIs64Bit { get; set; }

    [ParquetColumn("addressIsKernelIp", Encodings = [EncodingKind.RleDictionary])]
    public byte AddressIsKernelIp { get; set; }

    [ParquetColumn("addressBuildId", Encodings = [EncodingKind.Plain])]
    public byte[]? AddressBuildId { get; set; }

    [ParquetColumn("addressFiltered", Encodings = [EncodingKind.RleDictionary])]
    public byte AddressFiltered { get; set; }

    [ParquetColumn("addressComm", Encodings = [EncodingKind.RleDictionary])]
    public string? AddressComm { get; set; }
}
