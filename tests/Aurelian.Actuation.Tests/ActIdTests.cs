using Aurelian.Actuation;
using Xunit;

namespace Aurelian.Actuation.Tests;

public sealed class ActIdTests
{
    [Fact]
    public void ActId_IsValueTypeIdentifier()
    {
        Assert.Equal(new ActId(3, 5), new ActId(3, 5));
    }
}
