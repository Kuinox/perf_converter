using System.Globalization;

namespace PerfCapture;

public static class PerfCapturePlanBuilder
{
    public static PerfCapturePlan Build(PerfCaptureSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var recordArguments = new List<string> { "record" };
        var requirements = new HashSet<PerfCaptureRequirement> { PerfCaptureRequirement.PerfInstalled };
        var warnings = new List<string>();

        AddRecordOptions(spec, recordArguments, requirements, warnings);
        AddTarget(spec, recordArguments, warnings);

        return new PerfCapturePlan
        {
            RecordCommand = new PerfCommandPlan
            {
                FileName = "perf",
                Arguments = recordArguments,
                Environment = GetRecordEnvironment(spec),
                WorkingDirectory = spec is CommandCaptureSpec commandSpec
                    ? commandSpec.Target.WorkingDirectory
                    : null
            },
            PostProcessingCommands = BuildPostProcessingCommands(spec),
            Requirements = requirements.ToArray(),
            Warnings = warnings
        };
    }

    static void AddRecordOptions(
        PerfCaptureSpec spec,
        List<string> arguments,
        HashSet<PerfCaptureRequirement> requirements,
        List<string> warnings)
    {
        arguments.Add("-o");
        arguments.Add(spec.OutputPath.Value);

        if (spec.OverwriteOutput)
            arguments.Add("--force");

        foreach (var eventSpec in new[] { spec.Event }.Concat(spec.AdditionalEvents))
        {
            arguments.Add("-e");
            arguments.Add(FormatEvent(eventSpec, requirements));
        }

        if (spec.SampleFrequency is { } sampleFrequency)
        {
            arguments.Add("-F");
            arguments.Add(sampleFrequency.ToString(CultureInfo.InvariantCulture));
        }

        if (spec.CallGraph is { } callGraph)
        {
            arguments.Add("-g");
            arguments.Add("--call-graph");
            arguments.Add(callGraph.Value);
        }

        if (spec.UseKernelCore)
        {
            arguments.Add("--kcore");
            requirements.Add(PerfCaptureRequirement.KernelCoreAccessLikelyRequired);
            requirements.Add(PerfCaptureRequirement.ElevatedPrivilegesLikelyRequired);
        }

        if (spec.StartDisabled)
            arguments.Add("--delay=-1");

        if (spec.Control is { } control)
        {
            arguments.Add($"--control={FormatControlChannel(control)}");
            requirements.Add(PerfCaptureRequirement.PerfRecordControlAvailable);
        }

        if (spec.StartDisabled && spec.Control is null)
            warnings.Add("Capture starts disabled but no perf control channel is configured.");

        AddBuffer(spec.Buffer, arguments);

        foreach (var filter in spec.AddressFilters)
        {
            arguments.Add("--filter");
            arguments.Add(FormatAddressFilter(filter));
            requirements.Add(PerfCaptureRequirement.HardwareTraceAddressFilteringAvailable);
        }
        AddAddressFilterWarnings(spec, warnings);

        foreach (var extraArgument in spec.ExtraPerfRecordArguments)
            arguments.Add(extraArgument);

        if (spec.Event is IntelPtEventSpec { PrivilegeLevel: IntelPtPrivilegeLevel.None })
            warnings.Add("Intel PT privilege level is None; perf will likely reject the event.");
    }

    static void AddAddressFilterWarnings(PerfCaptureSpec spec, List<string> warnings)
    {
        if (spec.Event is not IntelPtEventSpec)
            return;

        if (spec.AddressFilters.Any(static filter => filter.Kind == PerfAddressFilterKind.Start))
            warnings.Add("Linux Intel PT rejects start address filters; use filter, stop with a range, or tracestop.");

        if (spec.AddressFilters.Any(static filter => filter.Kind == PerfAddressFilterKind.Stop && filter.Range.Size is null))
            warnings.Add("Linux Intel PT stop address filters require a range size.");
    }

    static void AddTarget(PerfCaptureSpec spec, List<string> arguments, List<string> warnings)
    {
        switch (spec)
        {
            case CommandCaptureSpec commandSpec:
                AddCommandLifetime(commandSpec, warnings);
                arguments.Add("--");
                arguments.Add(commandSpec.Target.FileName.Value);
                arguments.AddRange(commandSpec.Target.Arguments);
                break;

            case AttachCaptureSpec attachSpec:
                AddAttachedTarget(attachSpec.Target, arguments);
                AddExternalLifetime(attachSpec.Lifetime, arguments);
                break;

            default:
                throw new NotSupportedException($"Unsupported capture spec type: {spec.GetType().FullName}");
        }
    }

    static void AddCommandLifetime(CommandCaptureSpec spec, List<string> warnings)
    {
        if (spec.Lifetime is DurationLifetime or SignalLifetime or ManualStopLifetime)
        {
            warnings.Add("Command capture lifetime is controlled by the target command; non-target-exit lifetimes require runner support.");
        }
    }

    static void AddAttachedTarget(AttachedPerfCaptureTarget target, List<string> arguments)
    {
        switch (target)
        {
            case ProcessTarget processTarget:
                arguments.Add("-p");
                arguments.Add(processTarget.ProcessId.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case ThreadTarget threadTarget:
                arguments.Add("-t");
                arguments.Add(threadTarget.ThreadId.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case SystemWideTarget systemWideTarget:
                arguments.Add("-a");
                if (!string.IsNullOrWhiteSpace(systemWideTarget.CpuList))
                {
                    arguments.Add("-C");
                    arguments.Add(systemWideTarget.CpuList);
                }
                break;

            default:
                throw new NotSupportedException($"Unsupported capture target type: {target.GetType().FullName}");
        }
    }

    static void AddExternalLifetime(IExternalCaptureLifetime lifetime, List<string> arguments)
    {
        if (lifetime is not DurationLifetime durationLifetime)
            return;

        arguments.Add("--");
        arguments.Add("sleep");
        arguments.Add(FormatDurationSeconds(durationLifetime.Duration.Value));
    }

    static IReadOnlyDictionary<string, string?> GetRecordEnvironment(PerfCaptureSpec spec)
    {
        if (spec is CommandCaptureSpec commandSpec && commandSpec.Target.Environment is not null)
            return commandSpec.Target.Environment;

        return new Dictionary<string, string?>();
    }

    static IReadOnlyList<PerfCommandPlan> BuildPostProcessingCommands(PerfCaptureSpec spec)
    {
        var commands = new List<PerfCommandPlan>();

        foreach (var step in spec.PostProcessingSteps)
        {
            if (step is not PerfInjectJitSymbolsStep jitStep)
                throw new NotSupportedException($"Unsupported post-processing step type: {step.GetType().FullName}");

            var arguments = new List<string>
            {
                "inject",
                "-i",
                spec.OutputPath.Value,
                "--jit",
                "-o",
                jitStep.OutputPath.Value
            };

            if (jitStep.Force)
                arguments.Add("-f");

            commands.Add(new PerfCommandPlan
            {
                FileName = "perf",
                Arguments = arguments
            });
        }

        return commands;
    }

    static string FormatEvent(PerfEventSpec eventSpec, HashSet<PerfCaptureRequirement> requirements)
    {
        return eventSpec switch
        {
            NamedPerfEventSpec named => FormatNamedEvent(named),
            IntelPtEventSpec intelPt => FormatIntelPtEvent(intelPt, requirements),
            _ => throw new NotSupportedException($"Unsupported perf event type: {eventSpec.GetType().FullName}")
        };
    }

    static string FormatNamedEvent(NamedPerfEventSpec eventSpec)
    {
        if (eventSpec.Terms is not { Count: > 0 })
            return eventSpec.Name.Value;

        return $"{eventSpec.Name.Value}/{string.Join(',', eventSpec.Terms)}/";
    }

    static string FormatIntelPtEvent(IntelPtEventSpec eventSpec, HashSet<PerfCaptureRequirement> requirements)
    {
        requirements.Add(PerfCaptureRequirement.IntelPtAvailable);

        var terms = eventSpec.Terms.Count == 0
            ? string.Empty
            : string.Join(',', eventSpec.Terms);

        var privilege = eventSpec.PrivilegeLevel switch
        {
            IntelPtPrivilegeLevel.User => "u",
            IntelPtPrivilegeLevel.Kernel => "k",
            IntelPtPrivilegeLevel.UserAndKernel => string.Empty,
            IntelPtPrivilegeLevel.None => string.Empty,
            _ => string.Empty
        };

        if (terms.Length == 0 && privilege.Length == 0)
            return "intel_pt";

        return $"intel_pt/{terms}/{privilege}";
    }

    static void AddBuffer(PerfCaptureBufferPolicy buffer, List<string> arguments)
    {
        var value = buffer switch
        {
            GrowingBufferPolicy growing => FormatBuffer(growing.DataBuffer, growing.AuxBuffer),
            CircularBufferPolicy circular => FormatCircularBuffer(circular, arguments),
            _ => throw new NotSupportedException($"Unsupported buffer policy type: {buffer.GetType().FullName}")
        };

        if (value is null)
            return;

        arguments.Add("-m");
        arguments.Add(value);
    }

    static string? FormatCircularBuffer(CircularBufferPolicy circular, List<string> arguments)
    {
        arguments.Add(circular.SnapshotSize is { } snapshotSize
            ? $"--snapshot={FormatByteSize(snapshotSize)}"
            : "--snapshot");

        return FormatBuffer(circular.DataBuffer, circular.AuxBuffer);
    }

    static string? FormatBuffer(PerfBufferSize? dataBuffer, PerfBufferSize? auxBuffer)
    {
        if (dataBuffer is null && auxBuffer is null)
            return null;

        if (auxBuffer is null)
            return FormatBufferSize(dataBuffer!.Value);

        return $"{FormatBufferSize(dataBuffer ?? PerfBufferSize.Pages(1))},{FormatBufferSize(auxBuffer.Value)}";
    }

    static string FormatBufferSize(PerfBufferSize size)
    {
        if (size.Unit == PerfBufferSizeUnit.Pages)
            return size.Value.ToString(CultureInfo.InvariantCulture);

        const long mebibyte = 1024 * 1024;
        if (size.Value % mebibyte == 0)
            return $"{size.Value / mebibyte}M";

        return size.Value.ToString(CultureInfo.InvariantCulture);
    }

    static string FormatByteSize(ByteSize size)
    {
        const long mebibyte = 1024 * 1024;
        if (size.Value % mebibyte == 0)
            return $"{size.Value / mebibyte}M";

        return size.Value.ToString(CultureInfo.InvariantCulture);
    }

    static string FormatAddressFilter(PerfAddressFilter filter)
    {
        var operation = filter.Kind switch
        {
            PerfAddressFilterKind.Filter => "filter",
            PerfAddressFilterKind.Start => "start",
            PerfAddressFilterKind.Stop => "stop",
            PerfAddressFilterKind.TraceStop => "tracestop",
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter.Kind, "Unknown address filter kind.")
        };

        var range = filter.Range.Start.Value;
        if (filter.Range.Size is { } size)
            range += $" / {size.Value.ToString(CultureInfo.InvariantCulture)}";

        if (filter.Range.ObjectFile is { } objectFile)
            range += $" @{objectFile.Value}";

        return $"{operation} {range}";
    }

    static string FormatControlChannel(PerfControlChannel control)
    {
        return control switch
        {
            FileDescriptorPerfControlChannel fileDescriptor => FormatFileDescriptorControl(fileDescriptor),
            FifoPerfControlChannel fifo => FormatFifoControl(fifo),
            _ => throw new NotSupportedException($"Unsupported perf control channel type: {control.GetType().FullName}")
        };
    }

    static string FormatFileDescriptorControl(FileDescriptorPerfControlChannel control)
    {
        var value = $"fd:{control.ControlFileDescriptor.Value.ToString(CultureInfo.InvariantCulture)}";
        if (control.AcknowledgementFileDescriptor is { } acknowledgementFileDescriptor)
            value += $",{acknowledgementFileDescriptor.Value.ToString(CultureInfo.InvariantCulture)}";

        return value;
    }

    static string FormatFifoControl(FifoPerfControlChannel control)
    {
        var value = $"fifo:{control.ControlPath.Value}";
        if (control.AcknowledgementPath is { } acknowledgementPath)
            value += $",{acknowledgementPath.Value}";

        return value;
    }

    static string FormatDurationSeconds(TimeSpan duration)
    {
        return duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
