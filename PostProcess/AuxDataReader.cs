using Parquet;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Temp.Schema;

namespace PostProcess;

public static class AuxDataReader
{
    public static async Task<IReadOnlyDictionary<uint, IReadOnlyList<ulong>>> ReadAuxDataLossAsync(string auxPath)
    {
        var dict = new Dictionary<uint, List<ulong>>();
        using var reader = await ParquetReader.CreateAsync(File.OpenRead(auxPath));
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);
            var timeColumn = await rowGroup.ReadColumnAsync(AuxDataLostSchema.Time);
            var tidColumn = await rowGroup.ReadColumnAsync(AuxDataLostSchema.Tid);
            var flagsColumn = await rowGroup.ReadColumnAsync(AuxDataLostSchema.Flags);
            for (int j = 0; j < rowGroup.RowCount; j++)
            {
                var flags = (ulong)((IList)flagsColumn.Data)[j]!;
                if (flags == 0) continue; // only keep drops
                var tid = (uint)(ulong)((IList)tidColumn.Data)[j]!;
                var time = (ulong)((IList)timeColumn.Data)[j]!;
                if (!dict.TryGetValue(tid, out var list))
                {
                    list = [];
                    dict[tid] = list;
                }
                list.Add(time);
            }
        }
        foreach (var list in dict.Values)
        {
            list.Sort();
        }
        return dict.ToDictionary(x => x.Key, x => (IReadOnlyList<ulong>)x.Value);
    }
}
