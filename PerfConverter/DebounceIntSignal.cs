using System.Collections.Generic;

namespace PerfToPerfetto;

class DebounceSignal(int threshold)
{
    readonly TimeSpan _debounceTime = TimeSpan.FromMilliseconds(60);
    DateTime _lastSentTime = DateTime.MinValue;
    int _lastSent;


    public void Debounce(FormattableString message, int value)
    {
        if ((Math.Abs(value - _lastSent) > threshold)
            || DateTime.UtcNow - _lastSentTime > _debounceTime)
        {
            Console.Error.WriteLine(message);
            _lastSent = value;
            _lastSentTime = DateTime.UtcNow;
        }
    }
}