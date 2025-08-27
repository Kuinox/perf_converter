using Xunit;

namespace PerfToPerfetto.Tests;

/// <summary>
/// Basic tests for the TraceValidationHelper infrastructure
/// These tests verify the helper classes work correctly without requiring actual trace files
/// </summary>
public class TraceValidationHelperTests
{
    [Fact]
    public void TraceValidationHelper_CanBeCreated()
    {
        // Arrange & Act
        var helper = new TraceValidationHelper();
        
        // Assert
        Assert.NotNull(helper);
    }

    [Fact]
    public void TraceValidationHelper_CanBeCreatedWithCustomPath()
    {
        // Arrange & Act
        var customPath = "/custom/path/to/trace_processor_shell";
        var helper = new TraceValidationHelper(customPath);
        
        // Assert
        Assert.NotNull(helper);
    }

    [Fact]
    public async Task ValidateTraceFileAsync_ReturnsInvalidForNonexistentFile()
    {
        // Arrange
        var helper = new TraceValidationHelper();
        var nonexistentFile = "/path/that/does/not/exist.ftf";
        
        // Act
        var result = await helper.ValidateTraceFileAsync(nonexistentFile);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("does not exist", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteQueryAsync_ReturnsErrorForNonexistentFile()
    {
        // Arrange
        var helper = new TraceValidationHelper();
        var nonexistentFile = "/path/that/does/not/exist.ftf";
        var query = "SELECT COUNT(*) FROM slice;";
        
        // Act
        var result = await helper.ExecuteQueryAsync(nonexistentFile, query);
        
        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Contains("does not exist", result.ErrorMessage);
    }

    [Fact]
    public void TraceStatistics_CanBeCreated()
    {
        // Arrange & Act
        var stats = new TraceStatistics();
        
        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.SliceCount);
        Assert.Equal(0, stats.ThreadCount);
        Assert.Equal(0, stats.ProcessCount);
        Assert.Equal(0.0, stats.DurationMs);
    }

    [Fact]
    public void ValidationReport_CanBeCreated()
    {
        // Arrange & Act
        var report = new ValidationReport
        {
            InputDirectory = "/test/input",
            OutputTraceFile = "/test/output.ftf",
            ValidationTime = DateTime.UtcNow,
            IsValid = true
        };
        
        // Assert
        Assert.NotNull(report);
        Assert.Equal("/test/input", report.InputDirectory);
        Assert.Equal("/test/output.ftf", report.OutputTraceFile);
        Assert.True(report.IsValid);
    }

    [Fact]
    public async Task ValidationSamples_ValidatePerfToPerfettoOutput_HandlesNonexistentDirectory()
    {
        // Arrange
        var nonexistentDir = "/path/that/does/not/exist";
        var outputFile = "/test/output.ftf";
        
        // Act
        var report = await ValidationSamples.ValidatePerfToPerfettoOutput(nonexistentDir, outputFile);
        
        // Assert
        Assert.False(report.IsValid);
        Assert.Contains("does not exist", report.ErrorMessage);
    }
}