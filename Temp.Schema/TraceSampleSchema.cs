using Parquet;
using Parquet.Schema;
using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using System.Security.Cryptography;

namespace Temp.Schema;

public class TraceSampleSchema
{
    public TraceSampleSchema()
    {
        Schema = new ParquetSchema(
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


    public async IAsyncEnumerable<TraceEntry> ReadAll(ParquetReader reader)
    {
        foreach (var groupReader in reader.RowGroups)
            await foreach (var entry in ReadRowGroup(groupReader))
                yield return entry;
    }

    public async IAsyncEnumerable<TraceEntry> ReadRowGroup(IParquetRowGroupReader groupReader)
    {
        var id = await groupReader.ReadColumnAsync(Id.Field);
        var perfId = await groupReader.ReadColumnAsync(PerfId.Field);
        var pid = await groupReader.ReadColumnAsync(Pid.Field);
        var tid = await groupReader.ReadColumnAsync(Tid.Field);
        var time = await groupReader.ReadColumnAsync(Time.Field);
        var cpu = await groupReader.ReadColumnAsync(Cpu.Field);
        var flags = await groupReader.ReadColumnAsync(Flags.Field);
        var ip = await groupReader.ReadColumnAsync(Ip.Field);
        var addr = await groupReader.ReadColumnAsync(Addr.Field);
        var period = await groupReader.ReadColumnAsync(Period.Field);
        var insnCnt = await groupReader.ReadColumnAsync(InsnCnt.Field);
        var cycCnt = await groupReader.ReadColumnAsync(CycCnt.Field);
        var weight = await groupReader.ReadColumnAsync(Weight.Field);
        var cpumode = await groupReader.ReadColumnAsync(Cpumode.Field);
        var addrCorrelatesSym = await groupReader.ReadColumnAsync(AddrCorrelatesSym.Field);
        var @event = await groupReader.ReadColumnAsync(Event.Field);
        var machinePid = await groupReader.ReadColumnAsync(MachinePid.Field);
        var vcpu = await groupReader.ReadColumnAsync(Vcpu.Field);
        var ipSymoff = await groupReader.ReadColumnAsync(IpSymoff.Field);
        var ipSym = await groupReader.ReadColumnAsync(IpSym.Field);
        var ipSymStart = await groupReader.ReadColumnAsync(IpSymStart.Field);
        var ipSymEnd = await groupReader.ReadColumnAsync(IpSymEnd.Field);
        var ipDso = await groupReader.ReadColumnAsync(IpDso.Field);
        var ipSymBinding = await groupReader.ReadColumnAsync(IpSymBinding.Field);
        var ipIs64Bit = await groupReader.ReadColumnAsync(IpIs64Bit.Field);
        var ipIsKernelIp = await groupReader.ReadColumnAsync(IpIsKernelIp.Field);
        var ipBuildId = await groupReader.ReadColumnAsync(IpBuildId.Field);
        var ipFiltered = await groupReader.ReadColumnAsync(IpFiltered.Field);
        var ipComm = await groupReader.ReadColumnAsync(IpComm.Field);
        var haveAddress = await groupReader.ReadColumnAsync(HaveAddress.Field);
        var addressSymoff = await groupReader.ReadColumnAsync(AddressSymoff.Field);
        var addressSym = await groupReader.ReadColumnAsync(AddressSym.Field);
        var addressSymStart = await groupReader.ReadColumnAsync(AddressSymStart.Field);
        var addressSymEnd = await groupReader.ReadColumnAsync(AddressSymEnd.Field);
        var addressDso = await groupReader.ReadColumnAsync(AddressDso.Field);
        var addressSymBinding = await groupReader.ReadColumnAsync(AddressSymBinding.Field);
        var addressIs64Bit = await groupReader.ReadColumnAsync(AddressIs64Bit.Field);
        var addressIsKernelIp = await groupReader.ReadColumnAsync(AddressIsKernelIp.Field);
        var addressBuildId = await groupReader.ReadColumnAsync(AddressBuildId.Field);
        var addressFiltered = await groupReader.ReadColumnAsync(AddressFiltered.Field);
        var addressComm = await groupReader.ReadColumnAsync(AddressComm.Field);


        for (var i = 0; i < id.Data.Length; i++)
        {
            yield return new TraceEntry()
            {
                Id = id.AsSpan<ulong>()[i],
                PerfId = perfId.AsSpan<ulong>()[i],
                Pid = pid.AsSpan<uint>()[i],
                Tid = tid.AsSpan<uint>()[i],
                Time = time.AsSpan<ulong>()[i],
                Cpu = cpu.AsSpan<uint>()[i],
                Flags = flags.AsSpan<DLFilterFlag>()[i],
                IpAddress = ip.AsSpan<ulong>()[i],
                AddressAddress = addr.AsSpan<ulong>()[i],
                Period = period.AsSpan<ulong>()[i],
                InsnCnt = insnCnt.AsSpan<ulong>()[i],
                CycCnt = cycCnt.AsSpan<ulong>()[i],
                Weight = weight.AsSpan<ulong>()[i],
                Cpumode = cpumode.AsSpan<byte>()[i],
                AddrCorrelatesSym = addrCorrelatesSym.AsSpan<byte>()[i],
                Event = @event.AsSpan<string>()[i],
                MachinePid = machinePid.AsSpan<uint>()[i],
                Vcpu = vcpu.AsSpan<uint>()[i],
                IpSymoff = ipSymoff.AsSpan<uint>()[i],
                IpSym = ipSym.AsSpan<string>()[i],
                IpSymStart = ipSymStart.AsSpan<ulong>()[i],
                IpSymEnd = ipSymEnd.AsSpan<ulong>()[i],
                IpDso = ipDso.AsSpan<string>()[i],
                IpSymBinding = ipSymBinding.AsSpan<byte>()[i],
                IpIs64Bit = ipIs64Bit.AsSpan<byte>()[i],
                IpIsKernelIp = ipIsKernelIp.AsSpan<byte>()[i],
                IpBuildId = ipBuildId.AsSpan<byte[]>()[i],
                IpFiltered = ipFiltered.AsSpan<byte>()[i],
                IpComm = ipComm.AsSpan<string>()[i],
                HaveAddress = haveAddress.AsSpan<bool>()[i],
                AddressSymoff = addressSymoff.AsSpan<uint>()[i],
                AddressSym = addressSym.AsSpan<string>()[i],
                AddressSymStart = addressSymStart.AsSpan<ulong>()[i],
                AddressSymEnd = addressSymEnd.AsSpan<ulong>()[i],
                AddressDso = addressDso.AsSpan<string>()[i],
                AddressSymBinding = addressSymBinding.AsSpan<byte>()[i],
                AddressIs64Bit = addressIs64Bit.AsSpan<byte>()[i],
                AddressIsKernelIp = addressIsKernelIp.AsSpan<byte>()[i],
                AddressBuildId = addressBuildId.AsSpan<byte[]>()[i],
                AddressFiltered = addressFiltered.AsSpan<byte>()[i],
                AddressComm = addressComm.AsSpan<string>()[i]
            };
        }
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

    public ParquetSchema Schema { get; }
}