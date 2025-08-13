namespace Temp.Schema.FuchsiaTraceFormat;

static class InternalStrings
{
    public static string Get(InternalString x) => x switch
    {
        InternalString.Empty => "",
        InternalString.Instructions => "Instructions",
        InternalString.Cycles => "Cycles",
        InternalString.Footprint => "Footprint",
        InternalString.Symbol => "Symbol",
        InternalString.Timespan => "Timespan",
        _ => ""
    };

    public static IEnumerable<InternalString> AllExceptEmpty() =>
        Enum.GetValues(typeof(InternalString)).Cast<InternalString>().Where(e => e != InternalString.Empty);
}
