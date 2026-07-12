using Machina.Core.Measurement;
using Machina.Core.Styling;
using Machina.Renderer.Raster.Text;
using Machina.Standard.Text;
using Xunit;

namespace Machina.Standard.Tests.Text;

public sealed class MachinaTextMeasurementAuditTests
{
    public static TheoryData<string, MachinaTextVariant, MachinaTextRunStyle, TextSize> RepresentativeCases => new()
    {
        { "Increment", MachinaTextVariant.Label, new MachinaTextRunStyle(MachinaTextVariant.Label, false, false, false, null), TextSize.Sm },
        { "Machina Presenter", MachinaTextVariant.Title, new MachinaTextRunStyle(MachinaTextVariant.Title, false, false, false, null), TextSize.H1 },
        { "Count: 12", MachinaTextVariant.Body, new MachinaTextRunStyle(MachinaTextVariant.Body, false, false, false, null), TextSize.Md },
        { "Email updates: on", MachinaTextVariant.Label, new MachinaTextRunStyle(MachinaTextVariant.Label, false, false, false, null), TextSize.Sm },
        { "Hello world", MachinaTextVariant.Body, new MachinaTextRunStyle(MachinaTextVariant.Body, false, false, false, null), TextSize.Md },
        { "Hello  world", MachinaTextVariant.Body, new MachinaTextRunStyle(MachinaTextVariant.Body, false, false, false, null), TextSize.Md },
        { "code_value", MachinaTextVariant.Mono, new MachinaTextRunStyle(MachinaTextVariant.Mono, false, false, true, null), TextSize.Sm },
        { "note.", MachinaTextVariant.Caption, new MachinaTextRunStyle(MachinaTextVariant.Caption, false, false, false, null), TextSize.Sm },
    };

    [Theory]
    [MemberData(nameof(RepresentativeCases))]
    public void TextMeasurement_AgreesForRepresentativeStrings(
        string text,
        MachinaTextVariant variant,
        MachinaTextRunStyle runStyle,
        TextSize expectedRendererSize)
    {
        var standardMeasured = MachinaTextMeasurers.Deterministic.Measure(text, variant, runStyle);
        var coreMeasured = MachinaTextMeasurers.FromCore(DeterministicTextMeasurer.Instance).Measure(text, variant, runStyle);
        var rasterMeasured = ReadableBitmapTextRasterizer.MeasureText(text, new TextStyle(Size: expectedRendererSize));

        Assert.Equal(coreMeasured.Width, standardMeasured.Width);
        Assert.Equal(coreMeasured.Height, standardMeasured.Height);
        Assert.Equal(rasterMeasured.Width, standardMeasured.Width);
        Assert.Equal(rasterMeasured.Height, standardMeasured.Height);
    }

    [Theory]
    [MemberData(nameof(RepresentativeCases))]
    public void TextLayout_UsesRendererMeasurementReality_ForRepresentativeStrings(
        string text,
        MachinaTextVariant variant,
        MachinaTextRunStyle runStyle,
        TextSize expectedRendererSize)
    {
        var spec = runStyle.Code
            ? Machina.Standard.Text.Text.Markup($"`{text}`", variant: variant, wrap: MachinaTextWrap.None)
            : Machina.Standard.Text.Text.Plain(text, variant: variant, wrap: MachinaTextWrap.None);
        var layout = MachinaTextLayoutEngine.Layout(spec, new MachinaTextBox(10, 20, 600, 120), MachinaTextMeasurers.Deterministic);
        var run = Assert.Single(layout.Runs);
        var rasterMeasured = ReadableBitmapTextRasterizer.MeasureText(text, new TextStyle(Size: expectedRendererSize));

        Assert.Equal(text, run.Text);
        Assert.Equal(rasterMeasured.Width, run.Bounds.Width);
        Assert.Equal(rasterMeasured.Width, layout.ContentBounds.Width);
        Assert.Equal(runStyle.Variant, run.Style.Variant);
        Assert.Equal(runStyle.Code, run.Style.Code);
    }
}
