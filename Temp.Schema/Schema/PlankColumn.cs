using Plank.Schema;
using Plank.Writing;

namespace PerfConverter.Schema;

public class PlankColumn<T>(string name, ColumnOptions? options = null)
{
    public T[] Buffer { get; private set; } = [];
    public Column Column { get; } = new(name, ResolvePhysicalType(), options);
    int _activeLength;
    public int ActiveLength => _activeLength;

    public void Write(RowGroupWriter writer)
    {
        var serialized = writer.CreateSerializedColumn<T>(Column);
        ReadOnlySpan<T> values = Buffer.AsSpan(0, _activeLength);
        serialized.Serialize(values);
        writer.Write(serialized);
    }

    public void Resize(int size)
    {
        _activeLength = size;
        // Only allocate if we need more space
        if (Buffer.Length < size)
            Buffer = new T[size];
    }

    static ParquetPhysicalType ResolvePhysicalType()
    {
        var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (type == typeof(bool))
            return ParquetPhysicalType.Boolean;
        if (type == typeof(byte) || type == typeof(int) || type == typeof(uint))
            return ParquetPhysicalType.Int32;
        if (type == typeof(long) || type == typeof(ulong))
            return ParquetPhysicalType.Int64;
        if (type == typeof(float))
            return ParquetPhysicalType.Float;
        if (type == typeof(double))
            return ParquetPhysicalType.Double;
        if (type == typeof(string) || type == typeof(byte[]))
            return ParquetPhysicalType.ByteArray;

        throw new NotSupportedException($"Unsupported parquet CLR type: {typeof(T)}.");
    }
}
