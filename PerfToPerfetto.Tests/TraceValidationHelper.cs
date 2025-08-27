using System.Diagnostics;
using System.Text.Json;

namespace PerfToPerfetto.Tests;

/// <summary>
/// Helper class for validating Fuchsia Trace Format (.ftf) files using trace_processor_shell
/// </summary>
public class TraceValidationHelper
{
    private readonly string? _traceProcessorShellPath;

    public TraceValidationHelper(string? traceProcessorShellPath = null)
    {
        _traceProcessorShellPath = traceProcessorShellPath ?? FindTraceProcessorShell();
    }

    /// <summary>
    /// Validates that a trace file can be loaded by trace_processor_shell
    /// </summary>
    /// <param name="traceFilePath">Path to the .ftf trace file</param>
    /// <returns>Validation result containing success status and any error messages</returns>
    public async Task<TraceValidationResult> ValidateTraceFileAsync(string traceFilePath)
    {
        if (!File.Exists(traceFilePath))
        {
            return new TraceValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Trace file does not exist: {traceFilePath}"
            };
        }

        if (_traceProcessorShellPath == null)
        {
            return new TraceValidationResult
            {
                IsValid = false,
                ErrorMessage = "trace_processor_shell not found. Please ensure it's installed and in PATH, or specify the path in constructor."
            };
        }

        try
        {
            var result = await RunTraceProcessorShellCommandAsync(traceFilePath, "SELECT COUNT(*) as trace_count FROM slice;");
            return result;
        }
        catch (Exception ex)
        {
            return new TraceValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Exception during validation: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Executes a SQL query against the trace file using trace_processor_shell
    /// </summary>
    /// <param name="traceFilePath">Path to the .ftf trace file</param>
    /// <param name="sqlQuery">SQL query to execute</param>
    /// <returns>Query result containing success status and output</returns>
    public async Task<TraceQueryResult> ExecuteQueryAsync(string traceFilePath, string sqlQuery)
    {
        if (!File.Exists(traceFilePath))
        {
            return new TraceQueryResult
            {
                IsSuccessful = false,
                ErrorMessage = $"Trace file does not exist: {traceFilePath}"
            };
        }

        if (_traceProcessorShellPath == null)
        {
            return new TraceQueryResult
            {
                IsSuccessful = false,
                ErrorMessage = "trace_processor_shell not found"
            };
        }

        try
        {
            var validationResult = await RunTraceProcessorShellCommandAsync(traceFilePath, sqlQuery);
            return new TraceQueryResult
            {
                IsSuccessful = validationResult.IsValid,
                Output = validationResult.Output,
                ErrorMessage = validationResult.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new TraceQueryResult
            {
                IsSuccessful = false,
                ErrorMessage = $"Exception during query execution: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Gets basic statistics about the trace file
    /// </summary>
    public async Task<TraceStatistics> GetTraceStatisticsAsync(string traceFilePath)
    {
        var stats = new TraceStatistics();

        // Query for slice count
        var sliceResult = await ExecuteQueryAsync(traceFilePath, "SELECT COUNT(*) as count FROM slice;");
        if (sliceResult.IsSuccessful)
        {
            stats.SliceCount = ParseCountFromOutput(sliceResult.Output);
        }

        // Query for thread count
        var threadResult = await ExecuteQueryAsync(traceFilePath, "SELECT COUNT(DISTINCT utid) as count FROM thread;");
        if (threadResult.IsSuccessful)
        {
            stats.ThreadCount = ParseCountFromOutput(threadResult.Output);
        }

        // Query for process count
        var processResult = await ExecuteQueryAsync(traceFilePath, "SELECT COUNT(DISTINCT upid) as count FROM process;");
        if (processResult.IsSuccessful)
        {
            stats.ProcessCount = ParseCountFromOutput(processResult.Output);
        }

        // Query for duration
        var durationResult = await ExecuteQueryAsync(traceFilePath, 
            "SELECT (MAX(ts) - MIN(ts)) / 1000000.0 as duration_ms FROM slice WHERE ts > 0;");
        if (durationResult.IsSuccessful)
        {
            stats.DurationMs = ParseDoubleFromOutput(durationResult.Output);
        }

        return stats;
    }

    private async Task<TraceValidationResult> RunTraceProcessorShellCommandAsync(string traceFilePath, string query)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _traceProcessorShellPath!,
            Arguments = $"--query \"{query}\" {traceFilePath}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        
        var outputBuilder = new List<string>();
        var errorBuilder = new List<string>();

        process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.Add(e.Data); };
        process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.Add(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        var output = string.Join("\n", outputBuilder);
        var error = string.Join("\n", errorBuilder);

        var isValid = process.ExitCode == 0 && !output.Contains("ERROR") && !error.Contains("ERROR");

        return new TraceValidationResult
        {
            IsValid = isValid,
            Output = output,
            ErrorMessage = isValid ? null : $"Exit code: {process.ExitCode}, Error: {error}",
            ExitCode = process.ExitCode
        };
    }

    private static string? FindTraceProcessorShell()
    {
        // Try common locations and PATH
        var possiblePaths = new[]
        {
            "trace_processor_shell",
            "/usr/local/bin/trace_processor_shell",
            "/usr/bin/trace_processor_shell",
            "/opt/trace_processor_shell",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/bin/trace_processor_shell")
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--help",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    process.WaitForExit(5000); // 5 second timeout
                    if (process.ExitCode == 0 || process.ExitCode == 1) // Help command typically returns 0 or 1
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // Continue to next path
            }
        }

        return null;
    }

    private static long ParseCountFromOutput(string? output)
    {
        if (string.IsNullOrEmpty(output)) return 0;

        // Look for numeric value in the output
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (long.TryParse(line.Trim(), out var count))
            {
                return count;
            }
        }

        return 0;
    }

    private static double ParseDoubleFromOutput(string? output)
    {
        if (string.IsNullOrEmpty(output)) return 0.0;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (double.TryParse(line.Trim(), out var value))
            {
                return value;
            }
        }

        return 0.0;
    }
}

/// <summary>
/// Result of trace file validation
/// </summary>
public record TraceValidationResult
{
    public bool IsValid { get; init; }
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public int ExitCode { get; init; }
}

/// <summary>
/// Result of executing a query against a trace file
/// </summary>
public record TraceQueryResult
{
    public bool IsSuccessful { get; init; }
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Basic statistics about a trace file
/// </summary>
public record TraceStatistics
{
    public long SliceCount { get; set; }
    public long ThreadCount { get; set; }
    public long ProcessCount { get; set; }
    public double DurationMs { get; set; }
}