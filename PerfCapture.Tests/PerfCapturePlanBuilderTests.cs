using PerfCapture;

namespace PerfCapture.Tests;

public sealed class PerfCapturePlanBuilderTests
{
    [Test]
    public void Build_DotNetJitCapturePlan_DoesNotSummonSudo()
    {
        var spec = new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            UseKernelCore = true,
            Event = IntelPtEventSpec.CycleAccurate(),
            Buffer = PerfCaptureBufferPolicy.Growing(
                PerfBufferSize.Mebibytes(128),
                PerfBufferSize.Mebibytes(512)),
            Target = new CommandTarget(
                "dotnet",
                ["HelloWorld.dll"],
                Environment: new DotNetPerfMapOptions().ToEnvironment()),
            PostProcessingSteps = [new PerfInjectJitSymbolsStep("perfjit.data")]
        };

        var plan = PerfCapturePlanBuilder.Build(spec);
        var recordCommand = PerfCommandLineFormatter.Format(plan.RecordCommand);
        var injectCommand = PerfCommandLineFormatter.Format(plan.PostProcessingCommands.Single());

        Assert.That(recordCommand, Does.Not.Contain("sudo"));
        Assert.That(recordCommand, Does.Contain("env DOTNET_PerfMapEnabled=1 DOTNET_PerfMapStubGranularity=2 DOTNET_EnableWriteXorExecute=0 perf record"));
        Assert.That(recordCommand, Does.Contain("--kcore"));
        Assert.That(recordCommand, Does.Contain("-m 128M,512M"));
        Assert.That(recordCommand, Does.Contain("-e intel_pt/cyc=1,cyc_thresh=4,mtc=1,psb_period=2/"));
        Assert.That(recordCommand, Does.Contain("-- dotnet HelloWorld.dll"));
        Assert.That(injectCommand, Is.EqualTo("perf inject -i perf.data --jit -o perfjit.data -f"));
        Assert.That(plan.Requirements, Does.Contain(PerfCaptureRequirement.ElevatedPrivilegesLikelyRequired));
        Assert.That(plan.Requirements, Does.Contain(PerfCaptureRequirement.KernelCoreAccessLikelyRequired));
        Assert.That(plan.Requirements, Does.Contain(PerfCaptureRequirement.IntelPtAvailable));
    }

    [Test]
    public void Build_AddressFilters_RendersPerfRecordFilters()
    {
        var spec = new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            Target = new CommandTarget("/bin/true", []),
            AddressFilters =
            [
                PerfAddressFilter.Filter("0x400000", 4096, "/tmp/app"),
                PerfAddressFilter.Start("begin_trace"),
                PerfAddressFilter.Stop("end_trace"),
                PerfAddressFilter.TraceStop("main")
            ]
        };

        var plan = PerfCapturePlanBuilder.Build(spec);

        Assert.That(plan.RecordCommand.Arguments, Does.Contain("--filter"));
        Assert.That(plan.RecordCommand.Arguments, Does.Contain("filter 0x400000 / 4096 @/tmp/app"));
        Assert.That(plan.RecordCommand.Arguments, Does.Contain("start begin_trace"));
        Assert.That(plan.RecordCommand.Arguments, Does.Contain("stop end_trace"));
        Assert.That(plan.RecordCommand.Arguments, Does.Contain("tracestop main"));
        Assert.That(plan.Requirements, Does.Contain(PerfCaptureRequirement.HardwareTraceAddressFilteringAvailable));
    }

    [Test]
    public void Build_AttachTargets_RenderExpectedArguments()
    {
        var processPlan = PerfCapturePlanBuilder.Build(new AttachCaptureSpec
        {
            OutputPath = "process.data",
            Target = new ProcessTarget(1234),
            Lifetime = PerfCaptureLifetime.ForDuration(TimeSpan.FromSeconds(1))
        });

        var threadPlan = PerfCapturePlanBuilder.Build(new AttachCaptureSpec
        {
            OutputPath = "thread.data",
            Target = new ThreadTarget(5678),
            Lifetime = PerfCaptureLifetime.ForDuration(TimeSpan.FromSeconds(2.5))
        });

        var systemWidePlan = PerfCapturePlanBuilder.Build(new AttachCaptureSpec
        {
            OutputPath = "system.data",
            Target = new SystemWideTarget("0-1"),
            Lifetime = PerfCaptureLifetime.ForDuration(TimeSpan.FromSeconds(3))
        });

        Assert.That(processPlan.RecordCommand.Arguments, Is.EqualTo(new[] { "record", "-o", "process.data", "-e", "cpu-clock", "-p", "1234", "--", "sleep", "1" }));
        Assert.That(threadPlan.RecordCommand.Arguments, Is.EqualTo(new[] { "record", "-o", "thread.data", "-e", "cpu-clock", "-t", "5678", "--", "sleep", "2.5" }));
        Assert.That(systemWidePlan.RecordCommand.Arguments, Is.EqualTo(new[] { "record", "-o", "system.data", "-e", "cpu-clock", "-a", "-C", "0-1", "--", "sleep", "3" }));
    }

    [Test]
    public void Build_BufferAndOverwriteOptions_RenderExpectedPerfArguments()
    {
        var plan = PerfCapturePlanBuilder.Build(new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            OverwriteOutput = true,
            Buffer = PerfCaptureBufferPolicy.Circular(
                PerfBufferSize.Pages(8),
                PerfBufferSize.Mebibytes(16),
                ByteSize.FromMebibytes(4)),
            Target = new CommandTarget("/bin/true", [])
        });

        Assert.That(plan.RecordCommand.Arguments, Does.Contain("--force"));
        Assert.That(plan.RecordCommand.Arguments, Does.Contain("--snapshot=4M"));
        Assert.That(plan.RecordCommand.Arguments, Does.Contain("-m"));
        Assert.That(plan.RecordCommand.Arguments, Does.Contain("8,16M"));
    }

    [Test]
    public void Build_ControlChannel_RendersPerfRecordControlOptions()
    {
        var standardInputPlan = PerfCapturePlanBuilder.Build(new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            StartDisabled = true,
            Control = PerfControlChannel.StandardInput(),
            Target = new CommandTarget("/bin/true", [])
        });

        var fifoPlan = PerfCapturePlanBuilder.Build(new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            Control = PerfControlChannel.Fifo("/tmp/perf_ctl.fifo", "/tmp/perf_ack.fifo"),
            Target = new CommandTarget("/bin/true", [])
        });

        var fileDescriptorPlan = PerfCapturePlanBuilder.Build(new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            Control = PerfControlChannel.FileDescriptor(3, 4),
            Target = new CommandTarget("/bin/true", [])
        });

        Assert.That(standardInputPlan.RecordCommand.Arguments, Does.Contain("--delay=-1"));
        Assert.That(standardInputPlan.RecordCommand.Arguments, Does.Contain("--control=fd:0"));
        Assert.That(standardInputPlan.Requirements, Does.Contain(PerfCaptureRequirement.PerfRecordControlAvailable));
        Assert.That(fifoPlan.RecordCommand.Arguments, Does.Contain("--control=fifo:/tmp/perf_ctl.fifo,/tmp/perf_ack.fifo"));
        Assert.That(fileDescriptorPlan.RecordCommand.Arguments, Does.Contain("--control=fd:3,4"));
    }

    [Test]
    public void Build_StartDisabledWithoutControl_AddsWarning()
    {
        var plan = PerfCapturePlanBuilder.Build(new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            StartDisabled = true,
            Target = new CommandTarget("/bin/true", [])
        });

        Assert.That(plan.RecordCommand.Arguments, Does.Contain("--delay=-1"));
        Assert.That(plan.Warnings, Does.Contain("Capture starts disabled but no perf control channel is configured."));
    }

    [Test]
    public void Build_IntelPtPrivilegeLevels_RenderExpectedEventSelectors()
    {
        AssertEvent("intel_pt//u", IntelPtPrivilegeLevel.User);
        AssertEvent("intel_pt//k", IntelPtPrivilegeLevel.Kernel);
        AssertEvent("intel_pt", IntelPtPrivilegeLevel.UserAndKernel);
    }

    [Test]
    public void Build_MixedIntelPtAndSampledCallGraphCapture_RendersAllEvents()
    {
        var plan = PerfCapturePlanBuilder.Build(new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            Event = IntelPtEventSpec.UserOnly(),
            AdditionalEvents = [PerfEventSpec.Named("cpu-clock:u")],
            SampleFrequency = 2000,
            CallGraph = PerfCallGraphMode.FramePointer(),
            Buffer = PerfCaptureBufferPolicy.Growing(auxBuffer: PerfBufferSize.Mebibytes(64)),
            Target = new CommandTarget("/bin/true", [])
        });

        Assert.That(
            plan.RecordCommand.Arguments,
            Is.EqualTo(new[]
            {
                "record", "-o", "perf.data",
                "-e", "intel_pt//u",
                "-e", "cpu-clock:u",
                "-F", "2000",
                "-g", "--call-graph", "fp",
                "-m", "1,64M",
                "--", "/bin/true"
            }));
    }

    [Test]
    public void Build_IntelPtStartAndStopFilters_AddLinuxSupportWarnings()
    {
        var plan = PerfCapturePlanBuilder.Build(new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            Event = IntelPtEventSpec.UserOnly(),
            AddressFilters =
            [
                PerfAddressFilter.Start("begin_trace"),
                PerfAddressFilter.Stop("end_trace")
            ],
            Target = new CommandTarget("/bin/true", [])
        });

        Assert.That(plan.Warnings, Does.Contain("Linux Intel PT rejects start address filters; use filter, stop with a range, or tracestop."));
        Assert.That(plan.Warnings, Does.Contain("Linux Intel PT stop address filters require a range size."));
    }

    [Test]
    public void Format_QuotesUnsafeArgumentsForDryRun()
    {
        var formatted = PerfCommandLineFormatter.Format(new PerfCommandPlan
        {
            FileName = "perf",
            Arguments = ["record", "--", "/tmp/my app", "it's quoted"]
        });

        Assert.That(formatted, Is.EqualTo("perf record -- '/tmp/my app' 'it'\\''s quoted'"));
    }

    static void AssertEvent(string expectedEvent, IntelPtPrivilegeLevel privilegeLevel)
    {
        var plan = PerfCapturePlanBuilder.Build(new CommandCaptureSpec
        {
            OutputPath = "perf.data",
            Event = new IntelPtEventSpec { PrivilegeLevel = privilegeLevel },
            Target = new CommandTarget("/bin/true", [])
        });

        Assert.That(plan.RecordCommand.Arguments, Does.Contain(expectedEvent));
        Assert.That(plan.Requirements, Does.Contain(PerfCaptureRequirement.IntelPtAvailable));
    }
}
