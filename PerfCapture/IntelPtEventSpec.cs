namespace PerfCapture;

public sealed record IntelPtEventSpec : PerfEventSpec
{
    public IntelPtPrivilegeLevel PrivilegeLevel { get; init; } = IntelPtPrivilegeLevel.User;

    public IReadOnlyList<string> Terms { get; init; } = [];

    public static IntelPtEventSpec UserOnly() => new();

    public static IntelPtEventSpec CycleAccurate() => new()
    {
        PrivilegeLevel = IntelPtPrivilegeLevel.UserAndKernel,
        Terms = ["cyc=1", "cyc_thresh=4", "mtc=1", "psb_period=2"]
    };
}
