namespace Temp.Schema.FuchsiaTraceFormat;

readonly record struct EventHeader(
    string Name,
    string Category,
    (ulong pid, ulong tid) PidTid,
    ulong Timestamp,
    byte NArgs,
    byte EType,
    int ExtraDataSize
);
