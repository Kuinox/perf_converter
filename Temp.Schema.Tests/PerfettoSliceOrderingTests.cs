using PerfToPerfetto;

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
}
