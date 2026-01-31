using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace PerfConverter.Schema;

public class ParquetColumn<T>(string name)
{
    public T[] Buffer { get; private set; } = [];
    public DataField<T> Field { get; } = new DataField<T>(name);
    int _activeLength;

    public async Task Write(ParquetRowGroupWriter writer)
    {
        // Use a slice if buffer is larger than needed
        var data = _activeLength == Buffer.Length
            ? Buffer
            : Buffer.AsSpan(0, _activeLength).ToArray();
        var dataColumn = new DataColumn(Field, data);
        await writer.WriteColumnAsync(dataColumn);
    }

    public void Resize(int size)
    {
        _activeLength = size;
        // Only allocate if we need more space
        if (Buffer.Length < size)
            Buffer = new T[size];
    }
}
