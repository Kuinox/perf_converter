using Parquet;
using Parquet.Schema;

namespace Temp.Schema;

public class TraceSampleSchema
{
    public ParquetColumn<ulong> Id { get; } = new("id");
    public ParquetColumn<ulong> PerfId { get; } = new("perfId");
    public ParquetColumn<uint> Pid { get; } = new("pid");
    public ParquetColumn<uint> Tid { get; } = new("tid");
    public ParquetColumn<ulong> Time { get; } = new("time");
    public ParquetColumn<uint> Cpu { get; } = new("cpu");
    public ParquetColumn<uint> Flags { get; } = new("flags");
    public ParquetColumn<ulong> Ip { get; } = new("ip");
    public ParquetColumn<ulong> Addr { get; } = new("addr");
    public ParquetColumn<ulong> Period { get; } = new("period");
    public ParquetColumn<ulong> InsnCnt { get; } = new("insnCnt");
    public ParquetColumn<ulong> CycCnt { get; } = new("cycCnt");
    public ParquetColumn<ulong> Weight { get; } = new("weight");
    public ParquetColumn<byte> Cpumode { get; } = new("cpumode");
    public ParquetColumn<byte> AddrCorrelatesSym { get; } = new("addrCorrelatesSym");
    public ParquetColumn<string?> Event { get; } = new("event");
    public ParquetColumn<uint> MachinePid { get; } = new("machinePid");
    public ParquetColumn<uint> Vcpu { get; } = new("vcpu");
    public ParquetColumn<uint> IpSymoff { get; } = new("ipSymoff");
    public ParquetColumn<string?> IpSym { get; } = new("ipSym");
    public ParquetColumn<ulong> IpSymStart { get; } = new("ipSymStart");
    public ParquetColumn<ulong> IpSymEnd { get; } = new("ipSymEnd");
    public ParquetColumn<string?> IpDso { get; } = new("ipDso");
    public ParquetColumn<byte> IpSymBinding { get; } = new("ipSymBinding");
    public ParquetColumn<byte> IpIs64Bit { get; } = new("ipIs64Bit");
    public ParquetColumn<byte> IpIsKernelIp { get; } = new("ipIsKernelIp");
    public ParquetColumn<byte[]> IpBuildId { get; } = new("ipBuildId");
    public ParquetColumn<byte> IpFiltered { get; } = new("ipFiltered");
    public ParquetColumn<string?> IpComm { get; } = new("ipComm");
    public ParquetColumn<bool> HaveAddress { get; } = new("haveAddress");
    public ParquetColumn<uint> AddressSymoff { get; } = new("addressSymoff");
    public ParquetColumn<string?> AddressSym { get; } = new("addressSym");
    public ParquetColumn<ulong> AddressSymStart { get; } = new("addressSymStart");
    public ParquetColumn<ulong> AddressSymEnd { get; } = new("addressSymEnd");
    public ParquetColumn<string?> AddressDso { get; } = new("addressDso");
    public ParquetColumn<byte> AddressSymBinding { get; } = new("addressSymBinding");
    public ParquetColumn<byte> AddressIs64Bit { get; } = new("addressIs64Bit");
    public ParquetColumn<byte> AddressIsKernelIp { get; } = new("addressIsKernelIp");
    public ParquetColumn<byte[]> AddressBuildId { get; } = new("addressBuildId");
    public ParquetColumn<byte> AddressFiltered { get; } = new("addressFiltered");
    public ParquetColumn<string?> AddressComm { get; } = new("addressComm");


    public async Task Writer(ParquetWriter writer)
    {
        using var groupWriter = writer.CreateRowGroup();
        await Id.Write(groupWriter);
        await PerfId.Write(groupWriter);
        await Pid.Write(groupWriter);
        await Tid.Write(groupWriter);
        await Time.Write(groupWriter);
        await Cpu.Write(groupWriter);
        await Flags.Write(groupWriter);
        await Ip.Write(groupWriter);
        await Addr.Write(groupWriter);
        await Period.Write(groupWriter);
        await InsnCnt.Write(groupWriter);
        await CycCnt.Write(groupWriter);
        await Weight.Write(groupWriter);
        await Cpumode.Write(groupWriter);
        await AddrCorrelatesSym.Write(groupWriter);
        await Event.Write(groupWriter);
        await MachinePid.Write(groupWriter);
        await Vcpu.Write(groupWriter);
        await IpSymoff.Write(groupWriter);
        await IpSym.Write(groupWriter);
        await IpSymStart.Write(groupWriter);
        await IpSymEnd.Write(groupWriter);
        await IpDso.Write(groupWriter);
        await IpSymBinding.Write(groupWriter);
        await IpIs64Bit.Write(groupWriter);
        await IpIsKernelIp.Write(groupWriter);
        await IpBuildId.Write(groupWriter);
        await IpFiltered.Write(groupWriter);
        await IpComm.Write(groupWriter);
        await HaveAddress.Write(groupWriter);
        await AddressSymoff.Write(groupWriter);
        await AddressSym.Write(groupWriter);
        await AddressSymStart.Write(groupWriter);
        await AddressSymEnd.Write(groupWriter);
        await AddressDso.Write(groupWriter);
        await AddressSymBinding.Write(groupWriter);
        await AddressIs64Bit.Write(groupWriter);
        await AddressIsKernelIp.Write(groupWriter);
        await AddressBuildId.Write(groupWriter);
        await AddressFiltered.Write(groupWriter);
        await AddressComm.Write(groupWriter);
    }


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

    public ParquetSchema Schema => new ParquetSchema(
        Id.Field,
        PerfId.Field,
        Pid.Field,
        Tid.Field,
        Time.Field,
        Cpu.Field,
        Flags.Field,
        Ip.Field,
        Addr.Field,
        Period.Field,
        InsnCnt.Field,
        CycCnt.Field,
        Weight.Field,
        Cpumode.Field,
        AddrCorrelatesSym.Field,
        Event.Field,
        MachinePid.Field,
        Vcpu.Field,
        IpSymoff.Field,
        IpSym.Field,
        IpSymStart.Field,
        IpSymEnd.Field,
        IpDso.Field,
        IpSymBinding.Field,
        IpIs64Bit.Field,
        IpIsKernelIp.Field,
        IpBuildId.Field,
        IpFiltered.Field,
        IpComm.Field,
        HaveAddress.Field,
        AddressSymoff.Field,
        AddressSym.Field,
        AddressSymStart.Field,
        AddressSymEnd.Field,
        AddressDso.Field,
        AddressSymBinding.Field,
        AddressIs64Bit.Field,
        AddressIsKernelIp.Field,
        AddressBuildId.Field,
        AddressFiltered.Field,
        AddressComm.Field
    );
}