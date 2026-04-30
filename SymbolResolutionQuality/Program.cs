using System.Globalization;
using System.Text;
using Plank.Reading;
using Plank.Schema;
using Temp.Schema.Schema;

namespace SymbolResolutionQuality;

internal static class Program
{
    const byte HasDso = 1;
    const byte HasSymbol = 2;
    const byte HasSourceLine = 4;

    static int Main(string[] args)
    {
        if (args.Length == 0 || args.Any(static arg => arg is "-h" or "--help"))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var variants = ParseVariants(args);
        if (variants.Count == 0)
        {
            Console.Error.WriteLine("No input variants provided.");
            PrintUsage();
            return 1;
        }

        var results = new List<VariantResult>();
        foreach (var variant in variants)
        {
            if (!Directory.Exists(variant.Path))
            {
                Console.Error.WriteLine($"Input directory does not exist: {variant.Path}");
                return 1;
            }

            Console.Error.WriteLine($"Assessing {variant.Label}: {variant.Path}");
            results.Add(AssessVariant(variant));
        }

        WriteMarkdown(results);
        Console.WriteLine();
        WriteCsv(results);
        return 0;
    }

    static List<Variant> ParseVariants(string[] args)
    {
        var variants = new List<Variant>();
        foreach (var arg in args)
        {
            var splitIndex = arg.IndexOf('=', StringComparison.Ordinal);
            if (splitIndex < 0)
                splitIndex = arg.IndexOf(':', StringComparison.Ordinal);

            if (splitIndex <= 0 || splitIndex == arg.Length - 1)
            {
                var path = Path.GetFullPath(arg);
                variants.Add(new Variant(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), path));
                continue;
            }

            var label = arg[..splitIndex];
            var value = arg[(splitIndex + 1)..];
            variants.Add(new Variant(label, Path.GetFullPath(value)));
        }

        return variants;
    }

    static VariantResult AssessVariant(Variant variant)
    {
        var sourceLocationFile = Path.Combine(variant.Path, "source_locations.parquet");
        SourceStats sourceStats;
        Dictionary<ulong, byte> locations;
        if (File.Exists(sourceLocationFile))
        {
            locations = LoadSourceLocations(sourceLocationFile, out sourceStats);
        }
        else
        {
            sourceStats = default;
            locations = [];
        }

        var branchFiles = Directory.EnumerateFiles(variant.Path, "branches.parquet", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var branchStats = new BranchStats();
        var distinctAddressQuality = new Dictionary<ulong, byte>();
        foreach (var branchFile in branchFiles)
            ReadBranchFile(branchFile, locations, branchStats, distinctAddressQuality);

        foreach (var quality in distinctAddressQuality.Values)
        {
            branchStats.DistinctAddresses++;
            if ((quality & HasDso) != 0)
                branchStats.DistinctWithDso++;
            if ((quality & HasSymbol) != 0)
                branchStats.DistinctWithSymbol++;
            if ((quality & HasSourceLine) != 0)
                branchStats.DistinctWithSourceLine++;
        }

        return new VariantResult(variant, sourceStats, branchStats);
    }

    static Dictionary<ulong, byte> LoadSourceLocations(string path, out SourceStats stats)
    {
        var locations = new Dictionary<ulong, byte>();
        stats = new SourceStats();

        using var stream = File.OpenRead(path);
        using var reader = SourceLocationRowSchema.Schema.CreateReader(stream);

        foreach (var token in reader.EnumerateRowGroups())
        {
            using var rowGroup = reader.OpenRowGroup(stream, token);
            var ids = ReadColumn<ulong>(rowGroup, SourceLocationRowSchema.Schema.Columns[0]);
            var dsos = ReadColumn<byte[]>(rowGroup, SourceLocationRowSchema.Schema.Columns[2]);
            var symbols = ReadColumn<byte[]>(rowGroup, SourceLocationRowSchema.Schema.Columns[4]);
            var sourceFiles = ReadColumn<byte[]>(rowGroup, SourceLocationRowSchema.Schema.Columns[8]);
            var sourceLines = ReadColumn<uint>(rowGroup, SourceLocationRowSchema.Schema.Columns[9]);

            ValidateLengths(path, ids.Length, dsos.Length, symbols.Length, sourceFiles.Length, sourceLines.Length);

            for (var i = 0; i < ids.Length; i++)
            {
                stats.Total++;
                var quality = Quality(dsos[i], symbols[i], sourceFiles[i], sourceLines[i]);
                if ((quality & HasDso) != 0)
                    stats.WithDso++;
                if ((quality & HasSymbol) != 0)
                    stats.WithSymbol++;
                if ((quality & HasSourceLine) != 0)
                    stats.WithSourceLine++;

                locations[ids[i]] = quality;
            }
        }

        return locations;
    }

    static void ReadBranchFile(
        string path,
        IReadOnlyDictionary<ulong, byte> locations,
        BranchStats stats,
        Dictionary<ulong, byte> distinctAddressQuality)
    {
        stats.BranchFiles++;

        using var stream = File.OpenRead(path);
        using var reader = TraceSampleRowSchema.Schema.CreateReader(stream);

        foreach (var token in reader.EnumerateRowGroups())
        {
            using var rowGroup = reader.OpenRowGroup(stream, token);
            var ips = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[7]);
            var ipLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[8]);
            var addrs = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[9]);
            var addressLocationIds = ReadColumn<ulong>(rowGroup, TraceSampleRowSchema.Schema.Columns[10]);

            ValidateLengths(path, ips.Length, ipLocationIds.Length, addrs.Length, addressLocationIds.Length);

            stats.BranchRows += (ulong)ips.Length;
            for (var i = 0; i < ips.Length; i++)
            {
                AddEndpoint(ips[i], ipLocationIds[i], locations, stats, distinctAddressQuality);
                AddEndpoint(addrs[i], addressLocationIds[i], locations, stats, distinctAddressQuality);
            }
        }
    }

    static void AddEndpoint(
        ulong address,
        ulong locationId,
        IReadOnlyDictionary<ulong, byte> locations,
        BranchStats stats,
        Dictionary<ulong, byte> distinctAddressQuality)
    {
        if (address == 0)
            return;

        stats.AddressEndpoints++;
        var quality = (byte)0;
        if (locationId != 0)
        {
            stats.WithLocationId++;
            if (locations.TryGetValue(locationId, out quality))
            {
                if ((quality & HasDso) != 0)
                    stats.WithDso++;
                if ((quality & HasSymbol) != 0)
                    stats.WithSymbol++;
                if ((quality & HasSourceLine) != 0)
                    stats.WithSourceLine++;
            }
            else
            {
                stats.LocationIdMissingFromSourceTable++;
            }
        }

        if (distinctAddressQuality.TryGetValue(address, out var existingQuality))
            distinctAddressQuality[address] = (byte)(existingQuality | quality);
        else
            distinctAddressQuality[address] = quality;
    }

    static byte Quality(byte[]? dso, byte[]? symbol, byte[]? sourceFile, uint sourceLine)
    {
        var quality = (byte)0;
        if (!IsEmpty(dso))
            quality |= HasDso;
        if (!IsEmpty(symbol))
            quality |= HasSymbol;
        if (!IsEmpty(sourceFile) && sourceLine != 0)
            quality |= HasSourceLine;
        return quality;
    }

    static bool IsEmpty(byte[]? value)
        => value is null || value.Length == 0;

    static void WriteMarkdown(IReadOnlyList<VariantResult> results)
    {
        Console.WriteLine("| Variant | Source locs | Source lines | Source line % | Branch rows | Address endpoints | Endpoint symbols | Symbol % | Endpoint source lines | Source line % | Distinct addresses | Distinct symbols | Distinct source lines |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var result in results)
        {
            var source = result.SourceStats;
            var branches = result.BranchStats;
            Console.WriteLine(
                string.Create(CultureInfo.InvariantCulture,
                    $"| {result.Variant.Label} | {source.Total} | {source.WithSourceLine} | {Percent(source.WithSourceLine, source.Total)} | {branches.BranchRows} | {branches.AddressEndpoints} | {branches.WithSymbol} | {Percent(branches.WithSymbol, branches.AddressEndpoints)} | {branches.WithSourceLine} | {Percent(branches.WithSourceLine, branches.AddressEndpoints)} | {branches.DistinctAddresses} | {branches.DistinctWithSymbol} | {branches.DistinctWithSourceLine} |"));
        }
    }

    static void WriteCsv(IReadOnlyList<VariantResult> results)
    {
        Console.WriteLine("variant,path,source_locs,source_with_dso,source_with_symbol,source_with_source_line,branch_files,branch_rows,address_endpoints,endpoints_with_location_id,endpoints_with_dso,endpoints_with_symbol,endpoints_with_source_line,location_ids_missing_from_source_table,distinct_addresses,distinct_with_dso,distinct_with_symbol,distinct_with_source_line");
        foreach (var result in results)
        {
            var source = result.SourceStats;
            var branches = result.BranchStats;
            Console.WriteLine(
                string.Join(',',
                    Csv(result.Variant.Label),
                    Csv(result.Variant.Path),
                    source.Total,
                    source.WithDso,
                    source.WithSymbol,
                    source.WithSourceLine,
                    branches.BranchFiles,
                    branches.BranchRows,
                    branches.AddressEndpoints,
                    branches.WithLocationId,
                    branches.WithDso,
                    branches.WithSymbol,
                    branches.WithSourceLine,
                    branches.LocationIdMissingFromSourceTable,
                    branches.DistinctAddresses,
                    branches.DistinctWithDso,
                    branches.DistinctWithSymbol,
                    branches.DistinctWithSourceLine));
        }
    }

    static string Percent(ulong numerator, ulong denominator)
        => denominator == 0
            ? "0.00%"
            : string.Create(CultureInfo.InvariantCulture, $"{(double)numerator / denominator * 100:0.00}%");

    static string Csv(string value)
        => value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    static T[] ReadColumn<T>(RowGroupReader rowGroup, Column column)
    {
        var values = new List<T>();
        foreach (var page in rowGroup.Column<T>(column).Pages)
        {
            var span = page.Values.Span;
            for (var i = 0; i < span.Length; i++)
                values.Add(span[i]);
        }

        return [.. values];
    }

    static void ValidateLengths(string path, int expected, params int[] lengths)
    {
        foreach (var length in lengths)
        {
            if (length != expected)
                throw new InvalidDataException($"Column length mismatch in {path}: expected {expected}, got {length}.");
        }
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run --project SymbolResolutionQuality -- <label=parquet_output> [<label=parquet_output>...]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Counts branch IP and branch target endpoints from branches.parquet, using source_locations.parquet for quality.");
    }

    readonly record struct Variant(string Label, string Path);
    readonly record struct VariantResult(Variant Variant, SourceStats SourceStats, BranchStats BranchStats);

    struct SourceStats
    {
        public ulong Total;
        public ulong WithDso;
        public ulong WithSymbol;
        public ulong WithSourceLine;
    }

    sealed class BranchStats
    {
        public ulong BranchFiles;
        public ulong BranchRows;
        public ulong AddressEndpoints;
        public ulong WithLocationId;
        public ulong WithDso;
        public ulong WithSymbol;
        public ulong WithSourceLine;
        public ulong LocationIdMissingFromSourceTable;
        public ulong DistinctAddresses;
        public ulong DistinctWithDso;
        public ulong DistinctWithSymbol;
        public ulong DistinctWithSourceLine;
    }
}
