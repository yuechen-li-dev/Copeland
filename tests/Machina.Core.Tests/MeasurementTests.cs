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
            new IntrinsicSize(35, 16),
            measurer.MeasureText("Hello", new TextStyle(Size: TextSize.Sm)));

        Assert.Equal(
            new IntrinsicSize(40, 20),
            measurer.MeasureText("Hello", new TextStyle(Size: TextSize.Md)));

        Assert.Equal(
            new IntrinsicSize(70, 36),
            measurer.MeasureText("Hello", new TextStyle(Size: TextSize.H1)));
    }

    [Fact]
    public void DeterministicTextMeasurerGivesEmptyTextZeroWidth()
    {
        var measurer = new DeterministicTextMeasurer();

        var size = measurer.MeasureText(string.Empty, new TextStyle(Size: TextSize.Md));

        Assert.Equal(new IntrinsicSize(0, 20), size);
    }
}
