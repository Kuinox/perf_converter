namespace PerfToPerfetto.Tests;

/// <summary>
/// Sample usage demonstrating how to use TraceValidationHelper to validate PerfToPerfetto output
/// </summary>
public static class ValidationSamples
{
    /// <summary>
    /// Example of validating a single trace file
    /// </summary>
    public static async Task<bool> ValidateSingleTraceFile(string traceFilePath)
    {
        var validator = new TraceValidationHelper();
        var result = await validator.ValidateTraceFileAsync(traceFilePath);
        
        if (result.IsValid)
        {
            Console.WriteLine($"✓ Trace file {traceFilePath} is valid");
            return true;
        }
        else
        {
            Console.WriteLine($"✗ Trace file {traceFilePath} is invalid: {result.ErrorMessage}");
            return false;
        }
    }

    /// <summary>
    /// Example of getting detailed statistics about a trace file
    /// </summary>
    public static async Task<TraceStatistics> AnalyzeTraceFile(string traceFilePath)
    {
        var validator = new TraceValidationHelper();
        var stats = await validator.GetTraceStatisticsAsync(traceFilePath);
        
        Console.WriteLine($"Trace Statistics for {traceFilePath}:");
        Console.WriteLine($"  Slices: {stats.SliceCount}");
        Console.WriteLine($"  Threads: {stats.ThreadCount}");
        Console.WriteLine($"  Processes: {stats.ProcessCount}");
        Console.WriteLine($"  Duration: {stats.DurationMs:F2} ms");
        
        return stats;
    }

    /// <summary>
    /// Example of executing custom queries against a trace file
    /// </summary>
    public static async Task<bool> RunCustomQueries(string traceFilePath)
    {
        var validator = new TraceValidationHelper();
        
        // Query for all process names
        var processQuery = "SELECT DISTINCT name FROM process WHERE name IS NOT NULL;";
        var processResult = await validator.ExecuteQueryAsync(traceFilePath, processQuery);
        
        if (processResult.IsSuccessful)
        {
            Console.WriteLine("Process names found in trace:");
            Console.WriteLine(processResult.Output);
        }
        else
        {
            Console.WriteLine($"Failed to query processes: {processResult.ErrorMessage}");
            return false;
        }

        // Query for thread names
        var threadQuery = "SELECT DISTINCT name FROM thread WHERE name IS NOT NULL LIMIT 10;";
        var threadResult = await validator.ExecuteQueryAsync(traceFilePath, threadQuery);
        
        if (threadResult.IsSuccessful)
        {
            Console.WriteLine("Thread names found in trace (first 10):");
            Console.WriteLine(threadResult.Output);
        }
        else
        {
            Console.WriteLine($"Failed to query threads: {threadResult.ErrorMessage}");
            return false;
        }

        // Query for slice names/categories
        var sliceQuery = @"
            SELECT name, COUNT(*) as count 
            FROM slice 
            WHERE name IS NOT NULL 
            GROUP BY name 
            ORDER BY count DESC 
            LIMIT 10;";
        var sliceResult = await validator.ExecuteQueryAsync(traceFilePath, sliceQuery);
        
        if (sliceResult.IsSuccessful)
        {
            Console.WriteLine("Top 10 slice names by count:");
            Console.WriteLine(sliceResult.Output);
        }
        else
        {
            Console.WriteLine($"Failed to query slices: {sliceResult.ErrorMessage}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Example of validating a trace file produced by PerfToPerfetto conversion
    /// </summary>
    public static async Task<ValidationReport> ValidatePerfToPerfettoOutput(string inputParquetDir, string outputTraceFile)
    {
        var report = new ValidationReport
        {
            InputDirectory = inputParquetDir,
            OutputTraceFile = outputTraceFile,
            ValidationTime = DateTime.UtcNow
        };

        // Check if input directory exists and has parquet files
        if (!Directory.Exists(inputParquetDir))
        {
            report.IsValid = false;
            report.ErrorMessage = $"Input directory does not exist: {inputParquetDir}";
            return report;
        }

        var parquetFiles = Directory.GetFiles(inputParquetDir, "*.parquet", SearchOption.AllDirectories);
        if (parquetFiles.Length == 0)
        {
            report.IsValid = false;
            report.ErrorMessage = $"No parquet files found in input directory: {inputParquetDir}";
            return report;
        }

        report.InputFileCount = parquetFiles.Length;

        // Check if output trace file exists
        if (!File.Exists(outputTraceFile))
        {
            report.IsValid = false;
            report.ErrorMessage = $"Output trace file does not exist: {outputTraceFile}";
            return report;
        }

        // Validate the trace file
        var validator = new TraceValidationHelper();
        var validationResult = await validator.ValidateTraceFileAsync(outputTraceFile);
        
        if (!validationResult.IsValid)
        {
            report.IsValid = false;
            report.ErrorMessage = $"Trace file validation failed: {validationResult.ErrorMessage}";
            return report;
        }

        // Get statistics
        report.Statistics = await validator.GetTraceStatisticsAsync(outputTraceFile);
        
        // Perform some basic sanity checks
        if (report.Statistics.SliceCount == 0)
        {
            report.IsValid = false;
            report.ErrorMessage = "Generated trace contains no slices";
            return report;
        }

        if (report.Statistics.ThreadCount == 0)
        {
            report.IsValid = false;
            report.ErrorMessage = "Generated trace contains no threads";
            return report;
        }

        report.IsValid = true;
        return report;
    }
}

/// <summary>
/// Comprehensive validation report for PerfToPerfetto output
/// </summary>
public record ValidationReport
{
    public string InputDirectory { get; init; } = string.Empty;
    public string OutputTraceFile { get; init; } = string.Empty;
    public DateTime ValidationTime { get; init; }
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public int InputFileCount { get; set; }
    public TraceStatistics Statistics { get; set; } = new();
}