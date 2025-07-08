using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Temp.Schema;

namespace CLI;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("PerfConverter CLI - Helper tool for running perf with PerfConverter DLFilter");

        var inputFileArgument = new Argument<FileInfo>(
            name: "input-file",
            description: "Path to the perf data file");

        var perfArgsOption = new Option<string>(
            ["--perf-args", "-p"],
            getDefaultValue: () => "-f --itrace=bei0ns",
            "Additional arguments to pass to perf script");

        var outputOption = new Option<DirectoryInfo>(
            ["--output", "-o"],
            getDefaultValue: () => new DirectoryInfo("parquet_output"),
            "Output directory for Parquet files");

        var dryRunOption = new Option<bool>(
            ["--dry-run", "-n"],
            "Show the command that would be executed without running it");

        rootCommand.AddArgument(inputFileArgument);
        rootCommand.AddOption(perfArgsOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(dryRunOption);

        rootCommand.SetHandler(async context =>
        {
            var inputFile = context.ParseResult.GetValueForArgument(inputFileArgument)!;
            var perfArgs = context.ParseResult.GetValueForOption(perfArgsOption)!;
            var outputDir = context.ParseResult.GetValueForOption(outputOption)!;
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);

            await RunPerfCommand(inputFile, perfArgs, outputDir, dryRun);
        });

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task<int> RunPerfCommand(FileInfo inputFile, string perfArgs, DirectoryInfo outputDir, bool dryRun)
    {
        if (!inputFile.Exists)
        {
            Console.Error.WriteLine($"Error: Input file '{inputFile.FullName}' does not exist.");
            return 1;
        }

        var dataLost = GetAuxDataLost(inputFile.FullName);


        var dlFilterPath = Path.Combine(AppContext.BaseDirectory, "PerfConverter.so");

        if (!File.Exists(dlFilterPath))
        {
            Console.Error.WriteLine($"Error: PerfConverter.so not found at '{dlFilterPath}'");
            return 1;
        }

        if (!outputDir.Exists)
            outputDir.Create();

        Environment.SetEnvironmentVariable("OUTPUT_DIRECTORY", outputDir.FullName);

        var perfCommand = $"perf script {perfArgs} -i {inputFile.FullName} --dlfilter {dlFilterPath}";
        var auxDataLoss = JsonSerializer.Serialize(dataLost);
        if (dryRun)
        {
            Console.WriteLine("Would execute:");
            Console.WriteLine($"export OUTPUT_DIRECTORY=\"{outputDir.FullName}\"");
            Console.WriteLine($"export AUX_DATA_LOSS='{auxDataLoss}'");
            Console.WriteLine(perfCommand);
            return 0;
        }

        Console.WriteLine($"Executing: {perfCommand}");
        Console.WriteLine($"Output directory: {outputDir.FullName}");

        // Execute the command
        var processInfo = new ProcessStartInfo
        {
            FileName = "perf",
            Arguments = $"script {perfArgs} -i {inputFile.FullName} --dlfilter {dlFilterPath}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        processInfo.Environment["OUTPUT_DIRECTORY"] = outputDir.FullName;
        processInfo.Environment["AUX_DATA_LOSS"] = auxDataLoss;

        try
        {
            return await RunPerf(processInfo);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error executing perf command: {ex.Message}");
            return 1;
        }
    }


    private static IReadOnlyCollection<AuxDataLost> GetAuxDataLost(string perfFilePath)
    {
        List<AuxDataLost> _dataLostTimes = [];
        var entryCount = 0L;
        var lastPrint = DateTime.MinValue;

        AuxDataExtractor.Process(perfFilePath, entry =>
        {
            entryCount++;
            if (entry.HasValue)
            {
                if (entry.Value.Flags != 0)
                    _dataLostTimes.Add(new AuxDataLost(entry.Value.Time, entry.Value.Tid, entry.Value.Pid));
            }

            if (DateTime.UtcNow - DateTime.MinValue > TimeSpan.FromMilliseconds(10))
                Console.Write($"\rProcessed {entryCount} entries, found {_dataLostTimes.Count} aux data loss events.        ");
        });
        Console.WriteLine();
        return _dataLostTimes;
    }

    private static async Task<int> RunPerf(ProcessStartInfo processInfo)
    {
        using var process = Process.Start(processInfo);
        if (process == null)
        {
            Console.Error.WriteLine("Failed to start perf process.");
            return 1;
        }

        // Read output and error streams line by line for real-time progress
        var outputTask = ReadStreamAsync(process.StandardOutput, false);
        var errorTask = ReadStreamAsync(process.StandardError, true);

        await process.WaitForExitAsync();

        // Wait for all output to be processed
        await Task.WhenAll(outputTask, errorTask);

        return process.ExitCode;
    }

    private static async Task ReadStreamAsync(StreamReader reader, bool isErrorStream)
    {
        string? line;
        while (true)
        {
            line = await reader.ReadLineAsync();
            if (line == null)
                break;
            
            if (!isErrorStream && line.StartsWith("PROGRESS:"))
            {
                // Parse and display progress information
                if (int.TryParse(line.Substring(9), out int eventCount))
                    Console.Write($"\rProcessing events: {eventCount:N0}");
            }
            else
                // Display other output normally
                if (isErrorStream)
                    Console.Error.WriteLine(line);
                else
                    Console.WriteLine(line);
        }
        
        // Add a newline after progress to clean up the display
        if (!isErrorStream)
            Console.WriteLine();
    }
}