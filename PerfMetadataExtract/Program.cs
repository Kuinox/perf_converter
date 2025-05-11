using Microsoft.LinuxTracepoints.Decode;
using Parquet;
using PerfConverter.Persistence;
using PerfMetadataExtract;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: PerfMetadataExtract <inputFile> <outputFilePath> [compression] [batchSize]");
            Console.Error.WriteLine("  inputFile:   Path to the input perf data file");
            Console.Error.WriteLine("  outputFilePath: Path to the output file");
            Console.Error.WriteLine("  compression: Compression method (None, Gzip, Snappy) (default: Snappy)");
            Console.Error.WriteLine("  batchSize:   Batch size for processing (default: 2,000,000)");
            return 1;
        }

        string inputPath = args[0];
        string outputPath = args[1];

        // Parse optional arguments
        CompressionMethod compressionMethod = CompressionMethod.Snappy;
        if (args.Length > 2 && Enum.TryParse<CompressionMethod>(args[2], true, out var parsedCompression))
        {
            compressionMethod = parsedCompression;
        }

        int batchSize = 2_000_000;
        if (args.Length > 3 && int.TryParse(args[3], out var parsedBatchSize))
        {
            batchSize = parsedBatchSize;
        }

        try
        {
            return await ExtractAuxDataLostEvents(inputPath, outputPath, compressionMethod, batchSize);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during extraction: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ExtractAuxDataLostEvents(string inputPath, string outputPath, CompressionMethod compressionMethod, int batchSize)
    {
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            return 1;
        }

        // Create output directory if it doesn't exist
        string outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        Console.WriteLine($"Processing {inputPath}");
        Console.WriteLine($"Output file: {outputPath}");
        Console.WriteLine($"Compression: {compressionMethod}");
        Console.WriteLine($"Batch size: {batchSize}");

        var persistence = await ParquetAuxLostPersistence.Create(outputPath, compressionMethod);
        var batcher = Batcher<AuxDataLostEntry>.Create(persistence, batchSize, BatchingMode.OnFull);

        using var reader = new PerfDataFileReader();
        if (!reader.OpenFile(inputPath, PerfDataFileEventOrder.File))
        {
            Console.Error.WriteLine("Failed to open perf data file");
            return 1;
        }

        Console.WriteLine("Extracting auxiliary data lost events...");
        int eventsProcessed = 0;
        int auxEventsFound = 0;

        // Process events non-async to handle PerfEventBytes properly
        ProcessEvents(reader, batcher, ref eventsProcessed, ref auxEventsFound);

        // Cleanup
        await batcher.DisposeAsync();

        Console.WriteLine($"Extraction completed: processed {eventsProcessed:N0} events, found {auxEventsFound:N0} aux events");
        return 0;
    }

    private static void ProcessEvents(PerfDataFileReader reader, Batcher<AuxDataLostEntry> batcher, ref int eventsProcessed, ref int auxEventsFound)
    {
        while (true)
        {
            var result = reader.ReadEvent(out var perfEvent);
            if (result != PerfDataFileResult.Ok)
            {
                if (result == PerfDataFileResult.EndOfFile)
                    break;
                throw new InvalidOperationException($"Error reading perf data file: {result}");
            }

            eventsProcessed++;
            if (eventsProcessed % 1_000_000 == 0)
                Console.WriteLine($"Processed {eventsProcessed:N0} events, found {auxEventsFound:N0} aux events");

            if (perfEvent.Header.Type != PerfEventHeaderType.Aux)
                continue;
            
            auxEventsFound++;
            var eventResult = reader.GetNonSampleEventInfo(perfEvent, out var info);
            if (eventResult != PerfDataFileResult.Ok)
                throw new InvalidOperationException($"Error reading perf data file: {eventResult}");

            var flagBytes = info.BytesSpan.Slice(24, 8);
            var flags = BitConverter.ToUInt64(flagBytes);

            var entry = new AuxDataLostEntry
            {
                Time = info.Time,
                Pid = info.Pid,
                Tid = info.Tid,
                Cpu = info.Cpu,
                Flags = flags
            };

            batcher.Persist(entry);
        }
    }
}