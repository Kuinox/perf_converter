using Plank.Schema;

namespace Temp.Schema.Schema;

[ParquetSchema]
public sealed partial class TraceSampleRowSchema
{
    [ParquetColumn("id")]
    public ulong Id { get; set; }

    [ParquetColumn("perfId")]
    public ulong PerfId { get; set; }

    [ParquetColumn("pid")]
    public uint Pid { get; set; }

    [ParquetColumn("tid")]
    public uint Tid { get; set; }

    [ParquetColumn("time")]
    public ulong Time { get; set; }

    [ParquetColumn("cpu")]
    public uint Cpu { get; set; }

    [ParquetColumn("flags")]
    public uint Flags { get; set; }

    [ParquetColumn("ip")]
    public ulong Ip { get; set; }

    [ParquetColumn("addr")]
    public ulong Addr { get; set; }

    [ParquetColumn("period")]
    public ulong Period { get; set; }

    [ParquetColumn("insnCnt")]
    public ulong InsnCnt { get; set; }

    [ParquetColumn("cycCnt")]
    public ulong CycCnt { get; set; }

    [ParquetColumn("weight")]
    public ulong Weight { get; set; }

    [ParquetColumn("cpumode")]
    public byte Cpumode { get; set; }

    [ParquetColumn("addrCorrelatesSym")]
    public byte AddrCorrelatesSym { get; set; }

    [ParquetColumn("event")]
    public string? Event { get; set; }

    [ParquetColumn("machinePid")]
    public uint MachinePid { get; set; }

    [ParquetColumn("vcpu")]
    public uint Vcpu { get; set; }

    [ParquetColumn("srcFileName")]
    public string SourceFileName { get; set; } = string.Empty;

    [ParquetColumn("srcLineNumber")]
    public uint SourceLineNumber { get; set; }

    [ParquetColumn("ipSymoff")]
    public uint IpSymoff { get; set; }

    [ParquetColumn("ipSym")]
    public string? IpSym { get; set; }

    [ParquetColumn("ipSymStart")]
    public ulong IpSymStart { get; set; }

    [ParquetColumn("ipSymEnd")]
    public ulong IpSymEnd { get; set; }

    [ParquetColumn("ipDso")]
    public string? IpDso { get; set; }

    [ParquetColumn("ipSymBinding")]
    public byte IpSymBinding { get; set; }

    [ParquetColumn("ipIs64Bit")]
    public byte IpIs64Bit { get; set; }

    [ParquetColumn("ipIsKernelIp")]
    public byte IpIsKernelIp { get; set; }

    [ParquetColumn("ipBuildId")]
    public byte[] IpBuildId { get; set; } = [];

    [ParquetColumn("ipFiltered")]
    public byte IpFiltered { get; set; }

    [ParquetColumn("ipComm")]
    public string? IpComm { get; set; }

    [ParquetColumn("haveAddress")]
    public bool HaveAddress { get; set; }

    [ParquetColumn("addressSymoff")]
    public uint AddressSymoff { get; set; }

    [ParquetColumn("addressSym")]
    public string? AddressSym { get; set; }

    [ParquetColumn("addressSymStart")]
    public ulong AddressSymStart { get; set; }

    [ParquetColumn("addressSymEnd")]
    public ulong AddressSymEnd { get; set; }

    [ParquetColumn("addressDso")]
    public string? AddressDso { get; set; }

    [ParquetColumn("addressSymBinding")]
    public byte AddressSymBinding { get; set; }

    [ParquetColumn("addressIs64Bit")]
    public byte AddressIs64Bit { get; set; }

    [ParquetColumn("addressIsKernelIp")]
    public byte AddressIsKernelIp { get; set; }

    [ParquetColumn("addressBuildId")]
    public byte[]? AddressBuildId { get; set; }

    [ParquetColumn("addressFiltered")]
    public byte AddressFiltered { get; set; }

    [ParquetColumn("addressComm")]
    public string? AddressComm { get; set; }
}
