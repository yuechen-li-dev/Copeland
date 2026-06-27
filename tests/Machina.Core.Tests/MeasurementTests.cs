using Machina.Core.Measurement;
using Machina.Core.Styling;
using Xunit;

namespace Machina.Core.Tests;

public sealed class MeasurementTests
{
    [Fact]
    public void DeterministicTextMeasurerSizesByTextSize()
    {
        var measurer = new DeterministicTextMeasurer();

        Assert.Equal(
            new IntrinsicSize(29, 7),
            measurer.MeasureText("Hello", new TextStyle(Size: TextSize.Sm)));

        Assert.Equal(
            new IntrinsicSize(58, 14),
            measurer.MeasureText("Hello", new TextStyle(Size: TextSize.Md)));

        Assert.Equal(
            new IntrinsicSize(87, 21),
            measurer.MeasureText("Hello", new TextStyle(Size: TextSize.H1)));
    }

    [Fact]
    public void DeterministicTextMeasurerGivesEmptyTextZeroWidth()
    {
        var measurer = new DeterministicTextMeasurer();

        var size = measurer.MeasureText(string.Empty, new TextStyle(Size: TextSize.Md));

        Assert.Equal(new IntrinsicSize(0, 0), size);
    }

    [Fact]
    public void DeterministicTextMeasurerMatchesBitmapAdvanceFormula()
    {
        var measurer = new DeterministicTextMeasurer();
        var size = measurer.MeasureText("Increment", new TextStyle(Size: TextSize.Md));

        Assert.Equal(new IntrinsicSize(106, 14), size);
    }
}
