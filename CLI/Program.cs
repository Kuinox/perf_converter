using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CLI
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("PerfConverter CLI - Helper tool for running perf with PerfConverter DLFilter");

            var inputFileOption = new Option<FileInfo>(
                ["--input", "-i"],
                "Path to the perf data file")
            {
                IsRequired = true
            };


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

            rootCommand.AddOption(inputFileOption);
            rootCommand.AddOption(perfArgsOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(dryRunOption);

            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                var inputFile = context.ParseResult.GetValueForOption(inputFileOption)!;
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

            var dlFilterPath = Path.Combine(AppContext.BaseDirectory, "PerfConverter.so");
            
            if (!File.Exists(dlFilterPath))
            {
                Console.Error.WriteLine($"Error: PerfConverter.so not found at '{dlFilterPath}'");
                return 1;
            }

            // Ensure output directory exists
            if (!outputDir.Exists)
                outputDir.Create();
                
            // Set environment variable for output directory
            Environment.SetEnvironmentVariable("OUTPUT_DIRECTORY", outputDir.FullName);

            // Build the perf command
            var perfCommand = $"perf script {perfArgs} -i {inputFile.FullName} --dlfilter {dlFilterPath}";

            if (dryRun)
            {
                Console.WriteLine("Would execute:");
                Console.WriteLine(perfCommand);
                Console.WriteLine($"\nWith OUTPUT_DIRECTORY={outputDir.FullName}");
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

            try
            {
                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    Console.Error.WriteLine("Failed to start perf process.");
                    return 1;
                }

                // Read output asynchronously
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                var output = await outputTask;
                var error = await errorTask;

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Console.WriteLine(output);
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.Error.WriteLine(error);
                }

                return process.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error executing perf command: {ex.Message}");
                return 1;
            }
        }

    }
}