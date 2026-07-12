using Aurelian.Rendering.Contracts;
using Xunit;

namespace Aurelian.Rendering.Contracts.Tests;

public sealed class RenderFrameIdTests
{
    [Fact]
    public void RenderFrameId_IsValueTypeIdentifier()
    {
        Assert.Equal(new RenderFrameId(13), new RenderFrameId(13));
    }
}
