using Machina.Core.Lowering;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using Xunit;
using StandardText = Machina.Standard.Text.Text;

namespace Machina.Standard.Tests;

public sealed class StandardAuthoringMeasurementTests
{
    [Fact]
    public void PositionedLabelHonorsExplicitTextSizeWithoutChangingExistingDefault()
    {
        var lowered = UiLowerer.Lower(StandardUI.Label("Name", id: "name"));
        Assert.Equal(TextSize.Md, lowered.TextStyles[new NodeId("name")].Size);
        var positioned = UiLowerer.Lower(StandardUI.Label("Name", 0, 0, 200,
            id: "name", size: TextSize.Sm));
        Assert.Equal(TextSize.Sm, positioned.TextStyles[new NodeId("name")].Size);
    }

    [Theory]
    [InlineData(MachinaTextVerticalAlign.Top)]
    [InlineData(MachinaTextVerticalAlign.Center)]
    [InlineData(MachinaTextVerticalAlign.Bottom)]
    public void MeasurementIgnoresVerticalAlignmentAndUsesSuppliedGlyphMetrics(MachinaTextVerticalAlign align)
    {
        var measurer = new VariableWidthMeasurer();
        var wide = StandardText.Plain("WW WW WW", verticalAlign: align);
        var narrow = StandardText.Plain("ii ii ii", verticalAlign: align);
        var wideSize = MachinaTextLayoutEngine.Measure(wide, 50, measurer);
        var narrowSize = MachinaTextLayoutEngine.Measure(narrow, 50, measurer);

        Assert.True(wideSize.Height > narrowSize.Height);
        var layout = MachinaTextLayoutEngine.Layout(wide,
            new MachinaTextBox(0, 0, 50, wideSize.Height), measurer);
        Assert.False(layout.HasOverflow);
        Assert.Equal(wideSize.Height, layout.ContentBounds.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ParagraphRejectsInvalidMeasurementWidth(double width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StandardUI.Paragraph("Body", width));
    }

    private sealed class VariableWidthMeasurer : IMachinaTextMeasurer
    {
        public MachinaTextSize Measure(string text, MachinaTextVariant variant, MachinaTextRunStyle style)
        {
            double width = 0;
            foreach (char character in text)
            {
                width += character == 'W' ? 20 : 2;
            }
            return new MachinaTextSize(width, 14);
        }
    }
}
