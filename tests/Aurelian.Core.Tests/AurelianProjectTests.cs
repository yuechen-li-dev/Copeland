using Aurelian.Core;
using Xunit;

namespace Aurelian.Core.Tests;

public sealed class AurelianProjectTests
{
    [Fact]
    public void Name_IsAurelian()
    {
        Assert.Equal("Aurelian", AurelianProject.Name);
    }

    [Fact]
    public void EntityId_IsValueTypeIdentifier()
    {
        Assert.Equal(new EntityId(7), new EntityId(7));
    }
}
