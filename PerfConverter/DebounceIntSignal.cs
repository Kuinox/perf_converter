using System.Collections.Generic;

namespace PerfToPerfetto;

class DebounceSignal(int threshold)
{
    readonly TimeSpan _debounceTime = TimeSpan.FromMilliseconds(60);
    DateTime _lastSentTime = DateTime.MinValue;
    int _lastSent;


    public void Debounce(string prefix, int value)
    {
        if (!PerfConverter.Configuration.EnableProgressSignals)
            return;

        if ((Math.Abs(value - _lastSent) > threshold)
            || DateTime.UtcNow - _lastSentTime > _debounceTime)
        {
            const int stackBufferSize = 256;
            var requiredSize = prefix.Length + 20;

            Span<char> buffer = requiredSize <= stackBufferSize
                ? stackalloc char[stackBufferSize]
                : new char[requiredSize];

            prefix.AsSpan().CopyTo(buffer);
            if (value.TryFormat(buffer.Slice(prefix.Length), out int charsWritten))
            {
                Console.Error.WriteLine(buffer.Slice(0, prefix.Length + charsWritten));
            }
            _lastSent = value;
            _lastSentTime = DateTime.UtcNow;
        }
    }
}