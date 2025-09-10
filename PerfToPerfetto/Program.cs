using Google.Protobuf;
using Perfetto.Protos;

namespace PerfToPerfetto;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: PerfToPerfetto <inputFolder> <outputFile.fxt>");
            return 1;
        }

        var inputFolder = args[0];
        var outputFile = args[1];

        if (!Directory.Exists(inputFolder))
        {
            Console.Error.WriteLine($"Input folder does not exist: {inputFolder}");
            return 1;
        }

        try
        {
            var converter = new Processor();
            await converter.VisitRoot(inputFolder);
            using var fs = File.Create(outputFile);
            if(File.Exists(outputFile))
                File.Delete(outputFile);
            converter.Trace.WriteTo(fs);

            Console.WriteLine($"Successfully converted {inputFolder} to {outputFile}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}