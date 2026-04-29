namespace CLI;

static class TraceTimestampFormatter
{
    public static string Format(long? timestampNs)
    {
        if (!timestampNs.HasValue)
            return "-";

        var value = timestampNs.Value;
        var seconds = Math.DivRem(value, 1_000_000_000, out var nanoseconds);
        if (nanoseconds < 0)
        {
            seconds -= 1;
            nanoseconds += 1_000_000_000;
        }

        return $"{seconds}.{nanoseconds:D9}s";
    }

    public static string FormatRange(long? firstTimestampNs, long? lastTimestampNs)
    {
        if (!firstTimestampNs.HasValue || !lastTimestampNs.HasValue)
            return "-";

        return Format(lastTimestampNs.Value - firstTimestampNs.Value);
    }
}
