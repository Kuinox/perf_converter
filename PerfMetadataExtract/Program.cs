using Microsoft.LinuxTracepoints.Decode;
using Parquet;
using Parquet.Schema;
using PerfConverter.Persistence;
using PerfMetadataExtract;

var path = @"C:\Users\Kuinox\Documents\perfjit.data";
var basePath = "parquet_output";
using var fileStream = File.OpenWrite("aux_lost.parquet");

var persistence = await ParquetAuxLostPersistence.Create(basePath, 2_000_000, CompressionMethod.Snappy);
var batcher = Batcher<AuxDataLostEntry>.Create(persistence, 2_000_000, BatchingMode.OnFull);

using var reader = new PerfDataFileReader();
if (!reader.OpenFile(path, PerfDataFileEventOrder.File))
    throw new InvalidDataException();

while (true)
{
    var result = reader.ReadEvent(out var perfEvent);
    if (result != PerfDataFileResult.Ok)
    {
        if (result == PerfDataFileResult.EndOfFile)
            break;
        throw new InvalidOperationException($"Error reading perf data file: {result}");
    }
    if (perfEvent.Header.Type == PerfEventHeaderType.Aux)
    {
        var eventResult = reader.GetNonSampleEventInfo(perfEvent, out var infos);
        if (eventResult != PerfDataFileResult.Ok)
            throw new InvalidOperationException($"Error reading perf data file: {eventResult}");
        var flagBytes = infos.BytesSpan.Slice(24, 8);
        var flags = BitConverter.ToInt64(flagBytes);
        if(flags != 0)
        {

        }
    }
}