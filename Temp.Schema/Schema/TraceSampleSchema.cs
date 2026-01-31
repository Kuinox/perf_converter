using Parquet;
using Parquet.Data;
using Parquet.Schema;
using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using PerfConverter.Schema;
using System.Collections.Concurrent;

namespace Temp.Schema.Schema;

public class TraceSampleSchema
{
    // Encoding options for different column types
    static readonly ColumnEncodingOptions DeltaOnly = new() { UseDeltaBinaryPackedEncoding = true, UseDictionaryEncoding = false };
    static readonly ColumnEncodingOptions DictOnly = new() { UseDictionaryEncoding = true, UseDeltaBinaryPackedEncoding = false };
    static readonly ColumnEncodingOptions PlainOnly = new() { UseDictionaryEncoding = false, UseDeltaBinaryPackedEncoding = false };
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
            SourceFileName.Field,
            SourceLineNumber.Field,
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
    // Sequential IDs - delta encoding is perfect
    public ParquetColumn<ulong> Id { get; } = new("id", DeltaOnly);
    public ParquetColumn<ulong> PerfId { get; } = new("perfId", DeltaOnly);

    // Process/thread IDs - dictionary (few unique values)
    public ParquetColumn<uint> Pid { get; } = new("pid", DictOnly);
    public ParquetColumn<uint> Tid { get; } = new("tid", DictOnly);

    // Timestamps - delta encoding (monotonically increasing)
    public ParquetColumn<ulong> Time { get; } = new("time", DeltaOnly);

    // CPU/flags - dictionary (small set of values)
    public ParquetColumn<uint> Cpu { get; } = new("cpu", DictOnly);
    public ParquetColumn<uint> Flags { get; } = new("flags", DictOnly);

    // Addresses - plain (random values, no pattern)
    public ParquetColumn<ulong> Ip { get; } = new("ip", PlainOnly);
    public ParquetColumn<ulong> Addr { get; } = new("addr", PlainOnly);

    // Counters - delta encoding
    public ParquetColumn<ulong> Period { get; } = new("period", DeltaOnly);
    public ParquetColumn<ulong> InsnCnt { get; } = new("insnCnt", DeltaOnly);
    public ParquetColumn<ulong> CycCnt { get; } = new("cycCnt", DeltaOnly);
    public ParquetColumn<ulong> Weight { get; } = new("weight", DeltaOnly);

    // Small enum-like values - dictionary
    public ParquetColumn<byte> Cpumode { get; } = new("cpumode", DictOnly);
    public ParquetColumn<byte> AddrCorrelatesSym { get; } = new("addrCorrelatesSym", DictOnly);

    // Strings - dictionary (repeated values)
    public ParquetColumn<string?> Event { get; } = new("event", DictOnly);
    public ParquetColumn<uint> MachinePid { get; } = new("machinePid", DictOnly);
    public ParquetColumn<uint> Vcpu { get; } = new("vcpu", DictOnly);
    public ParquetColumn<string> SourceFileName { get; } = new("srcFileName", DictOnly);
    public ParquetColumn<uint> SourceLineNumber { get; } = new("srcLineNumber", DictOnly);
    public ParquetColumn<uint> IpSymoff { get; } = new("ipSymoff", PlainOnly);
    public ParquetColumn<string?> IpSym { get; } = new("ipSym", DictOnly);
    public ParquetColumn<ulong> IpSymStart { get; } = new("ipSymStart", PlainOnly);
    public ParquetColumn<ulong> IpSymEnd { get; } = new("ipSymEnd", PlainOnly);
    public ParquetColumn<string?> IpDso { get; } = new("ipDso", DictOnly);
    public ParquetColumn<byte> IpSymBinding { get; } = new("ipSymBinding", DictOnly);
    public ParquetColumn<byte> IpIs64Bit { get; } = new("ipIs64Bit", DictOnly);
    public ParquetColumn<byte> IpIsKernelIp { get; } = new("ipIsKernelIp", DictOnly);
    public ParquetColumn<byte[]> IpBuildId { get; } = new("ipBuildId", PlainOnly);
    public ParquetColumn<byte> IpFiltered { get; } = new("ipFiltered", DictOnly);
    public ParquetColumn<string?> IpComm { get; } = new("ipComm", DictOnly);
    public ParquetColumn<bool> HaveAddress { get; } = new("haveAddress", DictOnly);
    public ParquetColumn<uint> AddressSymoff { get; } = new("addressSymoff", PlainOnly);
    public ParquetColumn<string?> AddressSym { get; } = new("addressSym", DictOnly);
    public ParquetColumn<ulong> AddressSymStart { get; } = new("addressSymStart", PlainOnly);
    public ParquetColumn<ulong> AddressSymEnd { get; } = new("addressSymEnd", PlainOnly);
    public ParquetColumn<string?> AddressDso { get; } = new("addressDso", DictOnly);
    public ParquetColumn<byte> AddressSymBinding { get; } = new("addressSymBinding", DictOnly);
    public ParquetColumn<byte> AddressIs64Bit { get; } = new("addressIs64Bit", DictOnly);
    public ParquetColumn<byte> AddressIsKernelIp { get; } = new("addressIsKernelIp", DictOnly);
    public ParquetColumn<byte[]> AddressBuildId { get; } = new("addressBuildId", PlainOnly);
    public ParquetColumn<byte> AddressFiltered { get; } = new("addressFiltered", DictOnly);
    public ParquetColumn<string?> AddressComm { get; } = new("addressComm", DictOnly);


    public async Task Writer(ParquetWriter writer)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var groupWriter = writer.CreateRowGroup();

        // Parallelize column writes - Parquet.NET supports concurrent writes to the same row group
        await Task.WhenAll(
            Id.Write(groupWriter),
            PerfId.Write(groupWriter),
            Pid.Write(groupWriter),
            Tid.Write(groupWriter),
            Time.Write(groupWriter),
            Cpu.Write(groupWriter),
            Flags.Write(groupWriter),
            Ip.Write(groupWriter),
            Addr.Write(groupWriter),
            Period.Write(groupWriter),
            InsnCnt.Write(groupWriter),
            CycCnt.Write(groupWriter),
            Weight.Write(groupWriter),
            Cpumode.Write(groupWriter),
            AddrCorrelatesSym.Write(groupWriter),
            Event.Write(groupWriter),
            MachinePid.Write(groupWriter),
            Vcpu.Write(groupWriter),
            SourceFileName.Write(groupWriter),
            SourceLineNumber.Write(groupWriter),
            IpSymoff.Write(groupWriter),
            IpSym.Write(groupWriter),
            IpSymStart.Write(groupWriter),
            IpSymEnd.Write(groupWriter),
            IpDso.Write(groupWriter),
            IpSymBinding.Write(groupWriter),
            IpIs64Bit.Write(groupWriter),
            IpIsKernelIp.Write(groupWriter),
            IpBuildId.Write(groupWriter),
            IpFiltered.Write(groupWriter),
            IpComm.Write(groupWriter),
            HaveAddress.Write(groupWriter),
            AddressSymoff.Write(groupWriter),
            AddressSym.Write(groupWriter),
            AddressSymStart.Write(groupWriter),
            AddressSymEnd.Write(groupWriter),
            AddressDso.Write(groupWriter),
            AddressSymBinding.Write(groupWriter),
            AddressIs64Bit.Write(groupWriter),
            AddressIsKernelIp.Write(groupWriter),
            AddressBuildId.Write(groupWriter),
            AddressFiltered.Write(groupWriter),
            AddressComm.Write(groupWriter)
        );

        Console.Error.WriteLine($"FLUSH_TIMING|RowGroup written in {sw.ElapsedMilliseconds}ms|{Id.Buffer.Length} rows");
    }


    public async IAsyncEnumerable<TraceEntry> ReadAll(string path)
    {
        using var metaDataReader = await ParquetReader.CreateAsync(path);

        for (int i = 0; i < metaDataReader.RowGroupCount; i++)
            foreach (var entry in await ReadRowGroup(path, i))
                yield return entry;
    }


    IReadOnlyList<DataField> DataFields => [Id.Field,
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
        SourceFileName.Field,
        SourceLineNumber.Field,
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
        AddressComm.Field];


    static async Task<DataColumn> ReadParallel(string path, DataField dataField, int rowGroupId)
    {
        using var reader = await ParquetReader.CreateAsync(path);
        var groupReader = reader.RowGroups[rowGroupId];
        return await groupReader.ReadColumnAsync(dataField);
    }


    public async Task<IReadOnlyList<TraceEntry>> ReadRowGroup(string path, int rowGroupId)
    {
        var map = new ConcurrentDictionary<int, DataColumn>();
        await Parallel.ForEachAsync(DataFields.Select((item, i) => (item, i)), async (tuple, token) =>
        {
            var field = await ReadParallel(path, tuple.item, rowGroupId);
            map.TryAdd(tuple.i, field);
        });

        return SyncPart();
        IReadOnlyList<TraceEntry> SyncPart()
        {
            var results = map.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
            var id = results[0].AsSpan<ulong>();
            var perfId = results[1].AsSpan<ulong>();
            var pid = results[2].AsSpan<uint>();
            var tid = results[3].AsSpan<uint>();
            var time = results[4].AsSpan<ulong>();
            var cpu = results[5].AsSpan<uint>();
            var flags = results[6].AsSpan<DLFilterFlag>();
            var ipAddress = results[7].AsSpan<ulong>();
            var addressAddress = results[8].AsSpan<ulong>();
            var period = results[9].AsSpan<ulong>();
            var insnCnt = results[10].AsSpan<ulong>();
            var cycCnt = results[11].AsSpan<ulong>();
            var weight = results[12].AsSpan<ulong>();
            var cpumode = results[13].AsSpan<byte>();
            var addrCorrelatesSym = results[14].AsSpan<byte>();
            var @event = results[15].AsSpan<string>();
            var machinePid = results[16].AsSpan<uint>();
            var vcpu = results[17].AsSpan<uint>();
            var sourceFileName = results[18].AsSpan<string>();
            var sourceLineNumber = results[19].AsSpan<uint>();
            var ipSymoff = results[20].AsSpan<uint>();
            var ipSym = results[21].AsSpan<string>();
            var ipSymStart = results[22].AsSpan<ulong>();
            var ipSymEnd = results[23].AsSpan<ulong>();
            var ipDso = results[24].AsSpan<string>();
            var ipSymBinding = results[25].AsSpan<byte>();
            var ipIs64Bit = results[26].AsSpan<byte>();
            var ipIsKernelIp = results[27].AsSpan<byte>();
            var ipBuildId = results[28].AsSpan<byte[]>();
            var ipFiltered = results[29].AsSpan<byte>();
            var ipComm = results[30].AsSpan<string>();
            var haveAddress = results[31].AsSpan<bool>();
            var addressSymoff = results[32].AsSpan<uint>();
            var addressSym = results[33].AsSpan<string>();
            var addressSymStart = results[34].AsSpan<ulong>();
            var addressSymEnd = results[35].AsSpan<ulong>();
            var addressDso = results[36].AsSpan<string>();
            var addressSymBinding = results[37].AsSpan<byte>();
            var addressIs64Bit = results[38].AsSpan<byte>();
            var addressIsKernelIp = results[39].AsSpan<byte>();
            var addressBuildId = results[40].AsSpan<byte[]>();
            var addressFiltered = results[41].AsSpan<byte>();
            var addressComm = results[42].AsSpan<string>();

            var buffer = new TraceEntry[results[0].Data.Length];

            for (var i = 0; i < results[0].Data.Length; i++)
            {
                buffer[i] = new()
                {
                    Id = id[i],
                    PerfId = perfId[i],
                    Pid = pid[i],
                    Tid = tid[i],
                    Time = time[i],
                    Cpu = cpu[i],
                    Flags = flags[i],
                    IpAddress = ipAddress[i],
                    AddressAddress = addressAddress[i],
                    Period = period[i],
                    InsnCnt = insnCnt[i],
                    CycCnt = cycCnt[i],
                    Weight = weight[i],
                    Cpumode = cpumode[i],
                    AddrCorrelatesSym = addrCorrelatesSym[i],
                    Event = @event[i],
                    MachinePid = machinePid[i],
                    Vcpu = vcpu[i],
                    SourceFileName = sourceFileName[i],
                    SourceLineNumber = sourceLineNumber[i],
                    IpSymoff = ipSymoff[i],
                    IpSym = ipSym[i],
                    IpSymStart = ipSymStart[i],
                    IpSymEnd = ipSymEnd[i],
                    IpDso = ipDso[i],
                    IpSymBinding = ipSymBinding[i],
                    IpIs64Bit = ipIs64Bit[i],
                    IpIsKernelIp = ipIsKernelIp[i],
                    IpBuildId = ipBuildId[i],
                    IpFiltered = ipFiltered[i],
                    IpComm = ipComm[i],
                    HaveAddress = haveAddress[i],
                    AddressSymoff = addressSymoff[i],
                    AddressSym = addressSym[i],
                    AddressSymStart = addressSymStart[i],
                    AddressSymEnd = addressSymEnd[i],
                    AddressDso = addressDso[i],
                    AddressSymBinding = addressSymBinding[i],
                    AddressIs64Bit = addressIs64Bit[i],
                    AddressIsKernelIp = addressIsKernelIp[i],
                    AddressBuildId = addressBuildId[i],
                    AddressFiltered = addressFiltered[i],
                    AddressComm = addressComm[i],
                };
            }

            return buffer;
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