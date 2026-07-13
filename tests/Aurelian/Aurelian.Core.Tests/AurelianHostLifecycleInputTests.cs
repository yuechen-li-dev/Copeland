using Aurelian.Core.Engine.Frames;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class AurelianHostLifecycleInputTests
{
    [Fact]
    public void FrameInput_DefaultsToNoHostLifecycleSignals()
    {
        AurelianHostLifecycleInput input = AurelianHostLifecycleInput.None;

        Assert.Null(input.HostExtent);
        Assert.False(input.CloseRequested);
    }

    [Fact]
    public void SurfaceSize_RejectsZeroDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AurelianHostExtent(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AurelianHostExtent(1, 0));
    }
}
