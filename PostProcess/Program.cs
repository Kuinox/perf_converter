using Parquet;
using PerfConverter.Entry;
using System.Collections;
using PerfConverter.PerfStructs;
using PerfConverter.Persistence.ParquetDotNet.Schemas;
using System.Diagnostics;

namespace PostProcess;

class Program
{
    static async Task Main(string[] args)
    {
        // Hardcoded path for now
        string basePath = @"C:\Users\Kuinox\Documents\parquet_output";

        if (OperatingSystem.IsLinux() && basePath.StartsWith("C:"))
        {
            basePath = "/mnt/c" + basePath.Substring(2).Replace('\\', '/');
        }

        Console.WriteLine($"Reading parquet files from: {basePath}");

        string tracesPath = Path.Combine(basePath, "18461/18461/tracesamples.parquet");
        string addressesPath = Path.Combine(basePath, "addresses.parquet");

        if (!File.Exists(tracesPath))
        {
            Console.WriteLine($"Trace file not found: {tracesPath}");
            return;
        }

        if (!File.Exists(addressesPath))
        {
            Console.WriteLine($"Addresses file not found: {addressesPath}");
            return;
        }

        await ProcessTracesAndAddresses(tracesPath, addressesPath);
    }

    static int ip0Count;
    static int stackDepth;
    static int maxDepth;
    static async Task ProcessTracesAndAddresses(string tracesPath, string addressesPath)
    {
        Console.WriteLine("Processing traces and addresses...");

        try
        {
            // Load the symbol dictionary
            string symbolsPath = Path.Combine(Path.GetDirectoryName(addressesPath)!, "symbols.parquet");
            if (!File.Exists(symbolsPath))
            {
                Console.WriteLine($"Symbols file not found: {symbolsPath}");
                return;
            }
            using var symbolsReader = await ParquetReader.CreateAsync(File.OpenRead(symbolsPath));
            var symbolDict = await ReadDictAsync(symbolsReader);
            symbolDict.Add(ulong.MaxValue, "???");
            symbolDict.Add(0, "null");

            using var traceReader = await ParquetReader.CreateAsync(File.OpenRead(tracesPath));
            using var addressReader = await ParquetReader.CreateAsync(File.OpenRead(addressesPath));

            var addrEnum = ReadAllAddressesAsync(addressReader).GetAsyncEnumerator();

            var hasAddr = await addrEnum.MoveNextAsync();

            var stack = new Stack<(ulong, ulong?)>(); // Stack of symbol string IDs

            await foreach (var trace in ReadAllTracesAsync(traceReader))
            {
                var (ip, address) = await GetIPAndAddress(addrEnum);
                var isCall = trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_CALL);
                var isRet = trace.Flags.HasFlag(DLFilterFlag.PERF_DLFILTER_FLAG_RETURN);
                if (isCall)
                {
                    stack.Push((ip.SymStrId, address?.SymStrId));
                    var currentSymbolId = stack.Peek();

                }
                if (isRet)
                {
                    stack.Pop();
                }

                if (stack.Count == 0)
                {
                    Console.WriteLine("stack empty");
                    continue;
                }
                (ulong ipId, ulong? addrId) = stack.Peek();
                if (ipId == 0 && addrId == 0) continue;
                symbolDict.TryGetValue(ipId, out var ipName);
                string? currentSymbol = null;
                if(addrId.HasValue)  symbolDict.TryGetValue(addrId.Value, out currentSymbol);
                if(currentSymbol != null) {
                    currentSymbol = $"({currentSymbol})";
                }
                var line = $"{currentSymbol}{ipName}";
                Console.WriteLine("".PadLeft(stack.Count, ' ') + line);

            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing parquet files: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    async static ValueTask<(AddressEntry, AddressEntry?)> GetIPAndAddress(IAsyncEnumerator<AddressEntry> addrEnum)
    {
        var ipEntry = addrEnum.Current;
        await addrEnum.MoveNextAsync();
        AddressEntry? addressEntry = default;
        if (!addrEnum.Current.IsIp)
        {
            addressEntry = addrEnum.Current;
            await addrEnum.MoveNextAsync();
        }
        Debug.Assert(ipEntry.IsIp);
        if(addressEntry.HasValue) Debug.Assert(!addressEntry.Value.IsIp);
        return (ipEntry, addressEntry);
    }

    /// <summary>
    /// Read all trace samples from the parquet file.
    /// </summary>
    /// <param name="reader">The parquet reader.</param>
    /// <returns>An async enumerable of trace sample entries.</returns>
    public static async IAsyncEnumerable<TraceSampleEntry> ReadAllTracesAsync(ParquetReader reader)
    {
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);

            // Use the schema fields from TraceSampleSchema
            var idColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Id);
            var perfIdColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.PerfId);
            var pidColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Pid);
            var tidColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Tid);
            var timeColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Time);
            var cpuColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Cpu);
            var flagsColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Flags);
            var ipColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Ip);
            var addrColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Addr);
            var periodColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Period);
            var insnCntColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.InsnCnt);
            var cycCntColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.CycCnt);
            var weightColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Weight);
            var cpumodeColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Cpumode);
            var addrCorrelatesSymColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.AddrCorrelatesSym);
            var eventColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.EventId);
            var machinePidColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.MachinePid);
            var vcpuColumn = await rowGroup.ReadColumnAsync(TraceSampleSchema.Vcpu);

            for (int j = 0; j < rowGroup.RowCount; j++)
            {
                yield return new TraceSampleEntry
                {
                    Id = (ulong)((IList)idColumn.Data)[j]!,
                    PerfId = (ulong)((IList)perfIdColumn.Data)[j]!,
                    Pid = (uint)((IList)pidColumn.Data)[j]!,
                    Tid = (uint)((IList)tidColumn.Data)[j]!,
                    Time = (ulong)((IList)timeColumn.Data)[j]!,
                    Cpu = (uint)((IList)cpuColumn.Data)[j]!,
                    Flags = (DLFilterFlag)((IList)flagsColumn.Data)[j]!,
                    Ip = (ulong)((IList)ipColumn.Data)[j]!,
                    Addr = (ulong)((IList)addrColumn.Data)[j]!,
                    Period = (ulong)((IList)periodColumn.Data)[j]!,
                    InsnCnt = (ulong)((IList)insnCntColumn.Data)[j]!,
                    CycCnt = (ulong)((IList)cycCntColumn.Data)[j]!,
                    Weight = (ulong)((IList)weightColumn.Data)[j]!,
                    Cpumode = (byte)((IList)cpumodeColumn.Data)[j]!,
                    AddrCorrelatesSym = (byte)((IList)addrCorrelatesSymColumn.Data)[j]!,
                    EventId = (ulong)((IList)eventColumn.Data)[j]!,
                    MachinePid = (uint)((IList)machinePidColumn.Data)[j]!,
                    Vcpu = (uint)((IList)vcpuColumn.Data)[j]!
                };
            }
        }
    }

    /// <summary>
    /// Read all address entries from the parquet file.
    /// </summary>
    /// <param name="reader">The parquet reader.</param>
    /// <returns>An async enumerable of address entries.</returns>
    public static async IAsyncEnumerable<AddressEntry> ReadAllAddressesAsync(ParquetReader reader)
    {
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);

            // Use the schema fields from AddressSchema
            var idColumn = await rowGroup.ReadColumnAsync(AddressSchema.Id);
            var traceIdColumn = await rowGroup.ReadColumnAsync(AddressSchema.TraceId);
            var addressColumn = await rowGroup.ReadColumnAsync(AddressSchema.Address);
            var pidColumn = await rowGroup.ReadColumnAsync(AddressSchema.Pid);
            var isIpColumn = await rowGroup.ReadColumnAsync(AddressSchema.IsIp);
            var sizeColumn = await rowGroup.ReadColumnAsync(AddressSchema.Size);
            var symoffColumn = await rowGroup.ReadColumnAsync(AddressSchema.Symoff);
            var symStrIdColumn = await rowGroup.ReadColumnAsync(AddressSchema.SymStrId);
            var symStartColumn = await rowGroup.ReadColumnAsync(AddressSchema.SymStart);
            var symEndColumn = await rowGroup.ReadColumnAsync(AddressSchema.SymEnd);
            var dsoColumn = await rowGroup.ReadColumnAsync(AddressSchema.DsoStrId);
            var symBindingColumn = await rowGroup.ReadColumnAsync(AddressSchema.SymBinding);
            var is64BitColumn = await rowGroup.ReadColumnAsync(AddressSchema.Is64Bit);
            var isKernelIpColumn = await rowGroup.ReadColumnAsync(AddressSchema.IsKernelIp);
            //var buildIdColumn = await rowGroup.ReadColumnAsync(AddressSchema.BuildId);
            var filteredColumn = await rowGroup.ReadColumnAsync(AddressSchema.Filtered);
            var commStrIdColumn = await rowGroup.ReadColumnAsync(AddressSchema.CommStrId);
            var privColumn = await rowGroup.ReadColumnAsync(AddressSchema.Priv);

            // Yield each row
            for (int j = 0; j < rowGroup.RowCount; j++)
            {
                yield return new AddressEntry
                {
                    Id = (ulong)((IList)idColumn.Data)[j]!,
                    TraceId = (ulong)((IList)traceIdColumn.Data)[j]!,
                    Address = (ulong)((IList)addressColumn.Data)[j]!,
                    Pid = (uint)((IList)pidColumn.Data)[j]!,
                    IsIp = (bool)((IList)isIpColumn.Data)[j]!,
                    Size = (uint)((IList)sizeColumn.Data)[j]!,
                    Symoff = (uint)((IList)symoffColumn.Data)[j]!,
                    SymStrId = (ulong)((IList)symStrIdColumn.Data)[j]!,
                    SymStart = (ulong)((IList)symStartColumn.Data)[j]!,
                    SymEnd = (ulong)((IList)symEndColumn.Data)[j]!,
                    Dso = (ulong)((IList)dsoColumn.Data)[j]!,
                    SymBinding = (byte)((IList)symBindingColumn.Data)[j]!,
                    Is64Bit = (byte)((IList)is64BitColumn.Data)[j]!,
                    IsKernelIp = (byte)((IList)isKernelIpColumn.Data)[j]!,
                    //BuildId = (byte[])((IList)buildIdColumn.Data)[j]!,
                    Filtered = (byte)((IList)filteredColumn.Data)[j]!,
                    CommStrId = (ulong)((IList)commStrIdColumn.Data)[j]!,
                    Priv = (ulong)((IList)privColumn.Data)[j]!
                };
            }
        }
    }

    /// <summary>
    /// Read all dictionary entries from a parquet file and return them as a dictionary.
    /// </summary>
    /// <param name="reader">The parquet reader.</param>
    /// <returns>A dictionary mapping string IDs to string values.</returns>
    public static async Task<Dictionary<ulong, string>> ReadDictAsync(ParquetReader reader)
    {
        var dict = new Dictionary<ulong, string>();
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);
            var idColumn = await rowGroup.ReadColumnAsync(DictionarySchema.Id);
            var symbolColumn = await rowGroup.ReadColumnAsync(DictionarySchema.Symbol);
            for (int j = 0; j < rowGroup.RowCount; j++)
            {
                dict.Add((ulong)((IList)idColumn.Data)[j]!, (string)((IList)symbolColumn.Data)[j]!);
            }
        }
        return dict;
    }
}