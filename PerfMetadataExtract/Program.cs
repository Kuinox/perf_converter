using Microsoft.LinuxTracepoints.Decode;
using Parquet;
using PerfConverter.Persistence;
using PerfMetadataExtract;
using System.CommandLine;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create options
        var inputOption = new Option<FileInfo>(
            name: "--input",
            description: "The input perf data file path")
        { 
            IsRequired = true 
        };
        inputOption.AddAlias("-i");

        var outputOption = new Option<DirectoryInfo>(
            name: "--output",
            description: "The output directory for parquet files")
        {
            IsRequired = true
        };
        outputOption.AddAlias("-o");

        var compressionOption = new Option<CompressionMethod>(
            name: "--compression",
            description: "Compression method to use",
            getDefaultValue: () => CompressionMethod.Snappy);
        compressionOption.AddAlias("-c");

        var batchSizeOption = new Option<int>(
            name: "--batch-size",
            description: "Batch size for processing",
            getDefaultValue: () => 2_000_000);
        batchSizeOption.AddAlias("-b");

        // Create command
        var rootCommand = new RootCommand("PerfMetadataExtract - Extract auxiliary data lost events from perf data files");
        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(compressionOption);
        rootCommand.AddOption(batchSizeOption);

        rootCommand.SetHandler((inputFile, outputDir, compression, batchSize) =>
        {
            try
            {
                return ExtractAuxDataLostEvents(inputFile.FullName, outputDir.FullName, compression, batchSize);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during extraction: {ex.Message}");
                return Task.FromResult(1);
            }
        }, inputOption, outputOption, compressionOption, batchSizeOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task<int> ExtractAuxDataLostEvents(string inputPath, string outputPath, CompressionMethod compressionMethod, int batchSize)
    {
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outputPath);
        Console.WriteLine($"Processing {inputPath}");
        Console.WriteLine($"Output directory: {outputPath}");
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

            batcher.Persit(entry);
        }
    }
}