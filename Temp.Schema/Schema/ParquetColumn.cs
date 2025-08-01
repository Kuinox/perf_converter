using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace PerfConverter.Schema;

public class ParquetColumn<T>(string name)
{
    public T[] Buffer { get; private set; } = [];
    public DataField<T> Field { get; } = new DataField<T>(name);

    public async Task Write(ParquetRowGroupWriter writer)
    {
        var dataColumn = new DataColumn(Field, Buffer);
        await writer.WriteColumnAsync(dataColumn);
    }

    public void Resize(int size)
    {
        Buffer = new T[size];
    }
}
