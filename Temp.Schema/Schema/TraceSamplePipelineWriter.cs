using Plank.Writing;

namespace Temp.Schema.Schema;

public sealed class TraceSamplePipelineWriter : RowWriterBase<TraceSampleRowSchema.BufferSlot>
{
    readonly int _rowBatchSize;
    TraceSampleRowSchema.BufferSlot _active;
    bool _completed;

    public TraceSamplePipelineWriter(Stream stream, int rowBatchSize, uint maxParallelism, ParquetWriterOptions? options = null)
        : base(stream, TraceSampleRowSchema.Schema, maxParallelism, options ?? ParquetWriterOptions.Default)
    {
        if (rowBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowBatchSize), rowBatchSize, "Row batch size must be greater than zero.");

        _rowBatchSize = rowBatchSize;
        InitializeSlots();
        _active = TakeInitialSlot();
    }

    protected override TraceSampleRowSchema.BufferSlot CreateSlot(ParquetWriter writer)
        => new(writer, _rowBatchSize);

    protected override void SerializeSlot(TraceSampleRowSchema.BufferSlot slot)
        => slot.SerializeColumns();

    protected override void WriteSerializedSlot(TraceSampleRowSchema.BufferSlot slot, RowGroupWriter rowGroupWriter)
        => slot.WriteSerialized(rowGroupWriter);

    protected override void ResetSlotForReuse(TraceSampleRowSchema.BufferSlot slot)
        => slot.ResetForReuse();

    public TraceSampleRowSchema.Row GetRow()
    {
        ThrowIfFaulted();
        if (_completed)
            throw new InvalidOperationException("Pipeline writer is already completed.");

        return _active.GetRow();
    }

    public void Next()
    {
        ThrowIfFaulted();
        if (_completed)
            throw new InvalidOperationException("Pipeline writer is already completed.");

        _active.Next();
        if (!_active.IsFull)
            return;

        _active = EnqueueAndTakeFree(_active);
    }

    public void Complete()
    {
        ThrowIfFaulted();
        if (_completed)
            return;

        Complete(_active, !_active.IsEmpty);
        _completed = true;
    }
}
