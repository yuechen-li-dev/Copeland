using Machina.Core.Styling;
using Xunit;

namespace Machina.Core.Tests;

public sealed class StyleRecordTests
{
    [Fact]
    public void UiStyleRecordsAreImmutableWithExpressions()
    {
        var baseStyle = new UiStyle(Background: ColorToken.White);

        var changed = baseStyle with
        {
            Background = ColorToken.Gray,
        };

        Assert.Equal(ColorToken.White, baseStyle.Background);
        Assert.Equal(ColorToken.Gray, changed.Background);
    }
}
