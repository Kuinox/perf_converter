using System.Diagnostics;

namespace PerfConverter.Fuchsia.Tests
{
    /// <summary>
    /// Helper class to run Perfetto Trace Processor on Fuchsia trace files
    /// </summary>
    public class PerfettoTraceProcessor
    {
        private readonly string _executablePath;
        private const ulong MAGIC_NUMBER = 0x0016547846040010UL;

        /// <summary>
        /// Creates a new instance of the PerfettoTraceProcessor
        /// </summary>
        /// <param name="executablePath">Path to the trace_processor_shell executable</param>
        public PerfettoTraceProcessor(string executablePath)
        {
            if (!File.Exists(executablePath))
                throw new FileNotFoundException($"Trace processor executable not found at: {executablePath}");

            _executablePath = executablePath;
        }

        /// <summary>
        /// Writes a collection of records to a trace file
        /// </summary>
        /// <param name="filePath">Path where the trace file will be saved</param>
        /// <param name="records">Collection of records to write to the file</param>
        public void WriteTraceFile(string filePath, IEnumerable<Record> records)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // Write magic number
            writer.Write(MAGIC_NUMBER);
            
            // Write all records
            foreach (var record in records)
            {
                record.Write(writer);
            }
            
            // Save to file
            File.WriteAllBytes(filePath, ms.ToArray());
        }

        /// <summary>
        /// Checks if the trace file can be loaded by the trace processor
        /// </summary>
        /// <param name="traceFilePath">Path to the trace file</param>
        /// <returns>True if the trace is valid, false otherwise</returns>
        public bool ValidateTrace(string traceFilePath)
        {
            if (!File.Exists(traceFilePath))
                throw new FileNotFoundException($"Trace file not found: {traceFilePath}");

            // Create process to run trace_processor_shell
            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = $"-Q \"SELECT 1\" {traceFilePath}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var exited = process.WaitForExit(5000);
            if (!exited)
                throw new TimeoutException("Trace processor timed out");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (error.Contains("Error", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            // If the process exits with code 0 and returns the expected result, 
            // then the trace file is valid
            return process.ExitCode == 0 && output.Contains("1");
        }
    }
}