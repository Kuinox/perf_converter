using Microsoft.LinuxTracepoints.Decode;
using Parquet;
using PerfMetadataExtract;
using System.CommandLine;
using Temp.Core;

class AuxDataLost
{
    private static void ExtractAuxDataLostEvents(string inputPath, Action<AuxDataLostEntry?> onProcess)
    {
        using var reader = new PerfDataFileReader();
        if (!reader.OpenFile(inputPath, PerfDataFileEventOrder.File))
            throw new InvalidDataException("Failed to open perf data file");

        ProcessEvents(reader, onProcess);
    }

    private static void ProcessEvents(PerfDataFileReader reader, Action<AuxDataLostEntry?> onProcess)
    {
        while (true)
        {
            var result = reader.ReadEvent(out var perfEvent);

            if (result != PerfDataFileResult.Ok)
            {
                if (result == PerfDataFileResult.EndOfFile)
                    break;
                throw new InvalidOperationException($"Error reading perf data file: {result}");
            }


            if (perfEvent.Header.Type != PerfEventHeaderType.Aux)
            {
                onProcess(null);
                return;
            }
            var eventResult = reader.GetNonSampleEventInfo(perfEvent, out var info);
            if (eventResult != PerfDataFileResult.Ok)
                throw new InvalidOperationException($"Error reading perf data file: {eventResult}");
            var flagBytes = info.BytesSpan.Slice(24, 8);

            onProcess(new AuxDataLostEntry
            {
                Time = info.Time,
                Pid = info.Pid,
                Tid = info.Tid,
                Cpu = info.Cpu,
                Flags = BitConverter.ToUInt64(flagBytes)
            });
        }
    }
}