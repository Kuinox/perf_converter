using PerfToPerfetto.Tests;

namespace PerfToPerfetto.Tests;

/// <summary>
/// Demo program showing how to use the TraceValidationHelper
/// This is not a test itself, but a demonstration of the validation capabilities
/// </summary>
public class ValidationDemo
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("PerfToPerfetto Validation Demo");
        Console.WriteLine("==============================");

        if (args.Length < 1)
        {
            Console.WriteLine("Usage: ValidationDemo <trace-file.ftf> [trace_processor_shell_path]");
            Console.WriteLine("       ValidationDemo --test-conversion <input-parquet-dir> <output-trace-file>");
            return;
        }

        if (args[0] == "--test-conversion" && args.Length >= 3)
        {
            await DemoConversionValidation(args[1], args[2]);
        }
        else
        {
            var traceFile = args[0];
            var tracerPath = args.Length > 1 ? args[1] : null;
            await DemoBasicValidation(traceFile, tracerPath);
        }
    }

    private static async Task DemoBasicValidation(string traceFile, string? tracerPath)
    {
        Console.WriteLine($"Validating trace file: {traceFile}");
        Console.WriteLine();

        // Create validator with optional custom path
        var validator = new TraceValidationHelper(tracerPath);

        // Step 1: Basic validation
        Console.WriteLine("1. Basic Validation");
        Console.WriteLine("-------------------");
        var validationResult = await validator.ValidateTraceFileAsync(traceFile);
        
        if (validationResult.IsValid)
        {
            Console.WriteLine("✓ Trace file is valid and can be loaded by trace_processor_shell");
        }
        else
        {
            Console.WriteLine($"✗ Validation failed: {validationResult.ErrorMessage}");
            return; // Stop here if basic validation fails
        }
        Console.WriteLine();

        // Step 2: Get statistics
        Console.WriteLine("2. Trace Statistics");
        Console.WriteLine("-------------------");
        var stats = await validator.GetTraceStatisticsAsync(traceFile);
        Console.WriteLine($"Slices: {stats.SliceCount:N0}");
        Console.WriteLine($"Threads: {stats.ThreadCount:N0}");
        Console.WriteLine($"Processes: {stats.ProcessCount:N0}");
        Console.WriteLine($"Duration: {stats.DurationMs:F2} ms");
        Console.WriteLine();

        // Step 3: Custom queries
        Console.WriteLine("3. Sample Queries");
        Console.WriteLine("-----------------");

        // Query for process names
        await ExecuteAndDisplayQuery(validator, traceFile,
            "Process Names",
            "SELECT DISTINCT name FROM process WHERE name IS NOT NULL LIMIT 5;");

        // Query for thread names  
        await ExecuteAndDisplayQuery(validator, traceFile,
            "Thread Names",
            "SELECT DISTINCT name FROM thread WHERE name IS NOT NULL LIMIT 5;");

        // Query for top slice categories
        await ExecuteAndDisplayQuery(validator, traceFile,
            "Top Slice Categories",
            "SELECT name, COUNT(*) as count FROM slice WHERE name IS NOT NULL GROUP BY name ORDER BY count DESC LIMIT 5;");

        Console.WriteLine("Demo completed!");
    }

    private static async Task DemoConversionValidation(string inputDir, string outputFile)
    {
        Console.WriteLine($"Testing PerfToPerfetto conversion:");
        Console.WriteLine($"  Input: {inputDir}");
        Console.WriteLine($"  Output: {outputFile}");
        Console.WriteLine();

        var report = await ValidationSamples.ValidatePerfToPerfettoOutput(inputDir, outputFile);

        Console.WriteLine("Conversion Validation Report");
        Console.WriteLine("============================");
        Console.WriteLine($"Input Directory: {report.InputDirectory}");
        Console.WriteLine($"Output File: {report.OutputTraceFile}");
        Console.WriteLine($"Input Files: {report.InputFileCount}");
        Console.WriteLine($"Validation Time: {report.ValidationTime:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();

        if (report.IsValid)
        {
            Console.WriteLine("✓ Conversion validation PASSED");
            Console.WriteLine();
            Console.WriteLine("Statistics:");
            Console.WriteLine($"  Slices: {report.Statistics.SliceCount:N0}");
            Console.WriteLine($"  Threads: {report.Statistics.ThreadCount:N0}");
            Console.WriteLine($"  Processes: {report.Statistics.ProcessCount:N0}");
            Console.WriteLine($"  Duration: {report.Statistics.DurationMs:F2} ms");
        }
        else
        {
            Console.WriteLine("✗ Conversion validation FAILED");
            Console.WriteLine($"Error: {report.ErrorMessage}");
        }
    }

    private static async Task ExecuteAndDisplayQuery(TraceValidationHelper validator, string traceFile, string title, string query)
    {
        Console.WriteLine($"  {title}:");
        var result = await validator.ExecuteQueryAsync(traceFile, query);
        
        if (result.IsSuccessful)
        {
            var lines = result.Output?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            foreach (var line in lines.Take(3)) // Show first 3 results
            {
                Console.WriteLine($"    {line.Trim()}");
            }
            if (lines.Length > 3)
            {
                Console.WriteLine($"    ... ({lines.Length - 3} more)");
            }
        }
        else
        {
            Console.WriteLine($"    Error: {result.ErrorMessage}");
        }
        Console.WriteLine();
    }
}