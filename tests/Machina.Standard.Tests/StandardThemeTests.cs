using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardThemeTests
{
    [Fact]
    public void DefaultThemeContainsDeterministicTokens()
    {
        var theme = StandardTheme.Default;

        Assert.NotNull(theme);
        Assert.NotEqual(theme.Colors.Background, theme.Colors.Foreground);
        Assert.NotEqual(theme.Colors.Primary, theme.Colors.PrimaryForeground);
        Assert.NotEqual(theme.Colors.Destructive, theme.Colors.DestructiveForeground);
        Assert.True(theme.Spacing.Xs > 0);
        Assert.True(theme.Spacing.Sm > theme.Spacing.Xs);
        Assert.True(theme.Spacing.Md > theme.Spacing.Sm);
        Assert.True(theme.Spacing.Lg > theme.Spacing.Md);
        Assert.True(theme.Spacing.Xl > theme.Spacing.Lg);
        Assert.True(theme.Radius.Sm > 0);
        Assert.True(theme.Radius.Md > theme.Radius.Sm);
        Assert.True(theme.Radius.Lg > theme.Radius.Md);
    }
}
