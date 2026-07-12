using Machina.Core.Actions;
using Xunit;

namespace Machina.Core.Tests.Actions;

public sealed class UiActionIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyValue(string value)
    {
        Assert.Throws<ArgumentException>(() => new UiActionId(value));
    }

    [Fact]
    public void Equality_UsesValue()
    {
        var left = new UiActionId("counter.increment");
        var right = new UiActionId("counter.increment");

        Assert.Equal(left, right);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var id = new UiActionId("counter.increment");

        Assert.Equal("counter.increment", id.ToString());
    }

    [Fact]
    public void UiActionNamed_PreservesIdAndName()
    {
        var id = new UiActionId("counter.increment");
        var action = UiAction.Named(id);

        Assert.Equal(id, action.Id);
        Assert.Equal("counter.increment", action.Name);
    }
}
