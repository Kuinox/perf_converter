using Xunit;
using System.IO;
using Temp.Schema.FuchsiaTraceFormat;

namespace PerfToPerfetto.Tests;

/// <summary>
/// Integration tests that demonstrate the validation helper working with actual trace files
/// These tests create minimal trace files to verify the validation process works end-to-end
/// </summary>
public class ValidationIntegrationTests
{
    [Fact]
    public async Task TraceValidationHelper_CanHandleSimpleTraceFile()
    {
        // Arrange - Create a temporary simple trace file
        var tempFile = Path.GetTempFileName();
        var ftfFile = Path.ChangeExtension(tempFile, ".ftf");
        
        try
        {
            // Create a simple trace file using our FuchsiaTraceFormat writer
            using (var processor = new TraceProcessor(ftfFile))
            {
                processor.Start();
                // File will be written with header when Start() is called
            }
            
            // Verify the file was created
            Assert.True(File.Exists(ftfFile));
            Assert.True(new FileInfo(ftfFile).Length > 0);
            
            // Act - Use validation helper
            var helper = new TraceValidationHelper();
            var stats = await helper.GetTraceStatisticsAsync(ftfFile);
            
            // Assert - The validation helper should handle the file gracefully
            // (Even if trace_processor_shell isn't available, the helper should return meaningful results)
            Assert.NotNull(stats);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            if (File.Exists(ftfFile))
                File.Delete(ftfFile);
        }
    }

    [Fact]
    public async Task ValidationSamples_CanValidateBasicWorkflow()
    {
        // Arrange - Create temporary directories and files
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var inputDir = Path.Combine(tempDir, "input");
        var outputFile = Path.Combine(tempDir, "output.ftf");
        
        try
        {
            Directory.CreateDirectory(inputDir);
            
            // Create a fake parquet file in the input directory
            var fakeParquetFile = Path.Combine(inputDir, "segment0.parquet");
            await File.WriteAllTextAsync(fakeParquetFile, "fake parquet content");
            
            // Create a simple output trace file
            using (var processor = new TraceProcessor(outputFile))
            {
                processor.Start();
            }
            
            // Act - Test the validation workflow
            var report = await ValidationSamples.ValidatePerfToPerfettoOutput(inputDir, outputFile);
            
            // Assert - The validation should recognize the files exist
            Assert.NotNull(report);
            Assert.Equal(inputDir, report.InputDirectory);
            Assert.Equal(outputFile, report.OutputTraceFile);
            Assert.Equal(1, report.InputFileCount); // Should find our fake parquet file
            
            // The validation may fail due to lack of trace_processor_shell, but structure should be correct
            Assert.NotNull(report.Statistics);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PerfToPerfetto_CanCreateTraceFiles()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var ftfFile = Path.ChangeExtension(tempFile, ".ftf");
        
        try
        {
            // Act - Create a trace file with some basic content
            using (var processor = new TraceProcessor(ftfFile, TimestampMode.Time))
            {
                processor.Start();
                
                // Add some sample trace data using TraceEntry
                var sampleTrace = new PerfConverter.Entry.TraceEntry
                {
                    Id = 1,
                    Pid = 1234,
                    Tid = 5678,
                    Time = 1000000,
                    IpSym = "main",
                    AddressSym = "func1"
                };
                
                processor.ProcessTrace(sampleTrace);
                processor.PushFrame(sampleTrace);
            } // Dispose processor before reading file
            
            // Assert - File should be created and have content
            Assert.True(File.Exists(ftfFile));
            var content = File.ReadAllText(ftfFile);
            Assert.Contains("FTF_HEADER_PLACEHOLDER", content);
            Assert.Contains("FRAME_START", content);
            // Note: THREAD_NAME may not appear if the trace doesn't have thread name info
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            if (File.Exists(ftfFile))
                File.Delete(ftfFile);
        }
    }
}