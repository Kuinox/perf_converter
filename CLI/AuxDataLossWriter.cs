using Plank.Writing;
using Temp.Schema.Schema;

namespace CLI;

static class AuxDataLossWriter
{
    public static int Write(string inputPath, string outputPath)
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        using var output = File.Create(outputPath);
        var writer = AuxDataLossRowSchema.CreateRowWriter(
            output,
            new ParquetWriterOptions { Compression = CompressionKind.Snappy });

        var entryCount = 0L;
        var lossCount = 0;
        var id = 1UL;
        var lastUpdate = DateTime.UtcNow;

        AuxDataExtractor.Process(inputPath, entry =>
        {
            entryCount++;
            if (entry is { Flags: not 0 } lost)
            {
                var row = writer.GetRow();
                row.Id = id++;
                row.Time = lost.Time;
                row.Pid = lost.Pid;
                row.Tid = lost.Tid;
                row.Cpu = lost.Cpu;
                row.Flags = lost.Flags;
                writer.Next();
                lossCount++;
            }

            if (DateTime.UtcNow - lastUpdate > TimeSpan.FromSeconds(1))
            {
                Console.Error.WriteLine($"AUX scan: {entryCount:N0} entries, {lossCount:N0} losses");
                lastUpdate = DateTime.UtcNow;
            }
        });

        writer.Complete();
        Console.Error.WriteLine($"AUX scan complete: {entryCount:N0} entries, {lossCount:N0} losses");
        return lossCount;
    }
}
