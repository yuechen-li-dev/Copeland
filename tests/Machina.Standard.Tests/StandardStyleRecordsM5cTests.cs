using Machina.Core.Actions;
using Machina.Core.Lowering;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class StandardStyleRecordsM5cTests
{
    [Fact]
    public void StandardButton_ExplicitStyleOverridesDefault()
    {
        var style = StandardTheme.Default.Button.Default with
        {
            Background = Machina.Core.Styling.ColorToken.Hex(0xFF0000FF),
            Foreground = Machina.Core.Styling.ColorToken.Hex(0x00FF00FF),
            BorderColor = Machina.Core.Styling.ColorToken.Hex(0x0000FFFF),
            BorderThickness = 3,
            Width = 140,
            Height = 40,
            TextStyle = StandardTheme.Default.Button.Default.TextStyle with
            {
                Color = Machina.Core.Styling.ColorToken.Hex(0x00FF00FF),
                Size = Machina.Core.Styling.TextSize.Sm,
            },
        };

        var lowered = UiLowerer.Lower(StandardUI.Button("Go", id: "go", action: UiAction.Named("go"), style: style));

        Assert.Equal(style.Background, lowered.Styles[new NodeId("go")].Background);
        Assert.Equal(style.BorderColor, lowered.Styles[new NodeId("go")].BorderColor);
        Assert.Equal(style.BorderThickness, lowered.Styles[new NodeId("go")].BorderThickness);
        Assert.Equal(style.Foreground, lowered.TextStyles[new NodeId("go.label")].Color);
    }
}
