using PerfToPerfetto;
using Temp.Schema;

namespace Temp.Schema.Tests;

public sealed class PerfettoSliceOrderingTests
{
    [Test]
    public void SortsSameTimestampEventsByTraceOrder()
    {
        var endpoints = new[]
        {
            new Processor.SliceEndpoint(Time: 10, Trace: 3, Depth: 2, FrameId: 2, IsBegin: true),
            new Processor.SliceEndpoint(Time: 20, Trace: 5, Depth: 2, FrameId: 2, IsBegin: false),
            new Processor.SliceEndpoint(Time: 20, Trace: 6, Depth: 2, FrameId: 3, IsBegin: true),
            new Processor.SliceEndpoint(Time: 30, Trace: 7, Depth: 2, FrameId: 3, IsBegin: false),
        };

        var sorted = endpoints.Order(Processor.SliceEndpointComparer.Instance).ToArray();

        Assert.That(sorted.Select(static endpoint => endpoint.FrameId), Is.EqualTo(new ulong[] { 2, 2, 3, 3 }));
        Assert.That(sorted.Select(static endpoint => endpoint.IsBegin), Is.EqualTo(new[] { true, false, true, false }));
    }

    [Test]
    public void UsesDepthOnlyWhenTimestampAndTraceAreTied()
    {
        var endpoints = new[]
        {
            new Processor.SliceEndpoint(Time: 10, Trace: 1, Depth: 1, FrameId: 1, IsBegin: true),
            new Processor.SliceEndpoint(Time: 11, Trace: 2, Depth: 2, FrameId: 2, IsBegin: true),
            new Processor.SliceEndpoint(Time: 20, Trace: 3, Depth: 1, FrameId: 1, IsBegin: false),
            new Processor.SliceEndpoint(Time: 20, Trace: 3, Depth: 2, FrameId: 2, IsBegin: false),
        };

        var sorted = endpoints.Order(Processor.SliceEndpointComparer.Instance).ToArray();

        Assert.That(sorted[2].FrameId, Is.EqualTo(2));
        Assert.That(sorted[3].FrameId, Is.EqualTo(1));
    }

    [Test]
    public void KeepsZeroDurationFrameBeginBeforeItsEnd()
    {
        var endpoints = new[]
        {
            new Processor.SliceEndpoint(Time: 10, Trace: 1, Depth: 1, FrameId: 1, IsBegin: true),
            new Processor.SliceEndpoint(Time: 10, Trace: 2, Depth: 1, FrameId: 1, IsBegin: false),
        };

        var sorted = endpoints.Order(Processor.SliceEndpointComparer.Instance).ToArray();

        Assert.That(sorted[0].IsBegin, Is.True);
        Assert.That(sorted[1].IsBegin, Is.False);
    }

    [Test]
    public void SortedEndpointsRemainBalancedForPerfettoStackMatcher()
    {
        var endpoints = new[]
        {
            new Processor.SliceEndpoint(Time: 10, Trace: 1, Depth: 0, FrameId: 1, IsBegin: true),
            new Processor.SliceEndpoint(Time: 11, Trace: 2, Depth: 1, FrameId: 2, IsBegin: true),
            new Processor.SliceEndpoint(Time: 20, Trace: 3, Depth: 1, FrameId: 2, IsBegin: false),
            new Processor.SliceEndpoint(Time: 20, Trace: 4, Depth: 1, FrameId: 3, IsBegin: true),
            new Processor.SliceEndpoint(Time: 20, Trace: 5, Depth: 1, FrameId: 3, IsBegin: false),
            new Processor.SliceEndpoint(Time: 20, Trace: 6, Depth: 0, FrameId: 1, IsBegin: false),
        };

        var stack = new Stack<ulong>();
        foreach (var endpoint in endpoints.Order(Processor.SliceEndpointComparer.Instance))
        {
            if (endpoint.IsBegin)
            {
                stack.Push(endpoint.FrameId);
                continue;
            }

            Assert.That(stack.TryPop(out var frameId), Is.True);
            Assert.That(frameId, Is.EqualTo(endpoint.FrameId));
        }

        Assert.That(stack, Is.Empty);
    }

    [Test]
    public void SortedEndpointsFollowStackDepths()
    {
        var frames = new[]
        {
            new Processor.StackFrameRow(
                FrameId: 1,
                Tid: 42,
                Depth: 0,
                StartTime: 10,
                EndTime: 30,
                StartTrace: 1,
                EndTrace: 6,
                LocationId: 100,
                StartReason: StackFrameBoundaryReason.Call,
                EndReason: StackFrameBoundaryReason.Return),
            new Processor.StackFrameRow(
                FrameId: 2,
                Tid: 42,
                Depth: 1,
                StartTime: 11,
                EndTime: 20,
                StartTrace: 2,
                EndTrace: 3,
                LocationId: 101,
                StartReason: StackFrameBoundaryReason.Call,
                EndReason: StackFrameBoundaryReason.Return),
            new Processor.StackFrameRow(
                FrameId: 3,
                Tid: 42,
                Depth: 1,
                StartTime: 21,
                EndTime: 29,
                StartTrace: 4,
                EndTrace: 5,
                LocationId: 102,
                StartReason: StackFrameBoundaryReason.Call,
                EndReason: StackFrameBoundaryReason.Return),
        };

        AssertPerfettoStackDepths(Processor.CreateStackFrameEndpoints(frames));
    }

    [Test]
    public void DetectsFrameBeginningAtImpossibleDepth()
    {
        var frames = new[]
        {
            new Processor.StackFrameRow(
                FrameId: 1,
                Tid: 42,
                Depth: 2,
                StartTime: 10,
                EndTime: 30,
                StartTrace: 1,
                EndTrace: 4,
                LocationId: 100,
                StartReason: StackFrameBoundaryReason.Call,
                EndReason: StackFrameBoundaryReason.Return),
            new Processor.StackFrameRow(
                FrameId: 2,
                Tid: 42,
                Depth: 4,
                StartTime: 11,
                EndTime: 20,
                StartTrace: 2,
                EndTrace: 3,
                LocationId: 101,
                StartReason: StackFrameBoundaryReason.Call,
                EndReason: StackFrameBoundaryReason.Return),
        };

        Assert.Throws<AssertionException>(() => AssertPerfettoStackDepths(Processor.CreateStackFrameEndpoints(frames)));
    }

    [Test]
    public void AllowsVisibleSegmentToStartAtNonZeroRawDepth()
    {
        var frame = new Processor.StackFrameRow(
            FrameId: 1,
            Tid: 42,
            Depth: 2,
            StartTime: 10,
            EndTime: 20,
            StartTrace: 1,
            EndTrace: 2,
            LocationId: 100,
            StartReason: StackFrameBoundaryReason.Call,
            EndReason: StackFrameBoundaryReason.Return);

        AssertPerfettoStackDepths(Processor.CreateStackFrameEndpoints([frame]));
    }

    [Test]
    public void SyntheticFrameEndIsLeftOpenForPerfetto()
    {
        var frame = new Processor.StackFrameRow(
            FrameId: 1,
            Tid: 42,
            Depth: 0,
            StartTime: 10,
            EndTime: 20,
            StartTrace: 1,
            EndTrace: 2,
            LocationId: 100,
            StartReason: StackFrameBoundaryReason.Call,
            EndReason: StackFrameBoundaryReason.TraceEnd);

        var endpoints = Processor.CreateStackFrameEndpoints([frame]);

        Assert.That(endpoints, Has.Count.EqualTo(1));
        Assert.That(endpoints[0].IsBegin, Is.True);
    }

    [Test]
    public void TraceResumeFrameDoesNotEmitDuplicateBegin()
    {
        var frame = new Processor.StackFrameRow(
            FrameId: 2,
            Tid: 42,
            Depth: 0,
            StartTime: 30,
            EndTime: 40,
            StartTrace: 3,
            EndTrace: 4,
            LocationId: 100,
            StartReason: StackFrameBoundaryReason.TraceResume,
            EndReason: StackFrameBoundaryReason.Return);

        var endpoints = Processor.CreateStackFrameEndpoints([frame]);

        Assert.That(endpoints, Has.Count.EqualTo(1));
        Assert.That(endpoints[0].IsBegin, Is.False);
    }

    [Test]
    public void RealReturnEmitsBeginAndEnd()
    {
        var frame = new Processor.StackFrameRow(
            FrameId: 3,
            Tid: 42,
            Depth: 0,
            StartTime: 10,
            EndTime: 20,
            StartTrace: 1,
            EndTrace: 2,
            LocationId: 100,
            StartReason: StackFrameBoundaryReason.Call,
            EndReason: StackFrameBoundaryReason.Return);

        var endpoints = Processor.CreateStackFrameEndpoints([frame]);

        Assert.That(endpoints, Has.Count.EqualTo(2));
        Assert.That(endpoints[0].IsBegin, Is.True);
        Assert.That(endpoints[1].IsBegin, Is.False);
    }

    [Test]
    public void AuxLossMarkerNameIncludesThreadCpuAndFlags()
    {
        var row = new Processor.AuxLossRow(
            Id: 1,
            Time: 100,
            Pid: 200,
            Tid: 300,
            Cpu: 4,
            Flags: 0x20);

        var name = Processor.GetAuxLossMarkerName(row);

        Assert.That(name, Is.EqualTo("AUX loss tid=300 cpu=4 flags=0x20"));
    }

    static void AssertPerfettoStackDepths(IReadOnlyList<Processor.StackFrameEndpoint> endpoints)
    {
        var stack = new Stack<ulong>();
        uint? visibleDepthBase = null;
        foreach (var endpoint in endpoints)
        {
            if (stack.Count == 0 && endpoint.IsBegin)
                visibleDepthBase = endpoint.Depth;

            var visibleDepth = visibleDepthBase.HasValue && endpoint.Depth >= visibleDepthBase.Value
                ? endpoint.Depth - visibleDepthBase.Value
                : endpoint.Depth;

            if (endpoint.IsBegin)
            {
                Assert.That(visibleDepth, Is.EqualTo((uint)stack.Count));
                stack.Push(endpoint.Frame.FrameId);
                continue;
            }

            Assert.That(stack, Is.Not.Empty);
            Assert.That(visibleDepth, Is.EqualTo((uint)(stack.Count - 1)));
            Assert.That(stack.Pop(), Is.EqualTo(endpoint.Frame.FrameId));
            if (stack.Count == 0)
                visibleDepthBase = null;
        }

        Assert.That(stack, Is.Empty);
    }
}
