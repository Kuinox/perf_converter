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
            Span<char> buffer = stackalloc char[16];
            if (value.TryFormat(buffer, out int charsWritten))
            {
                Console.Error.Write(prefix);
                Console.Error.WriteLine(buffer.Slice(0, charsWritten));
            }
            _lastSent = value;
            _lastSentTime = DateTime.UtcNow;
        }
    }
}