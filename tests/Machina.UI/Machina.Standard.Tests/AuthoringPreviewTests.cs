using System.Buffers.Binary;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Measurement;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Testing;
using Xunit;

namespace Machina.Standard.Tests;

public sealed class AuthoringPreviewTests
{
    [Theory]
    [InlineData(TextAlignX.Left, TextAlignY.Top, 0, 23, 0, 15)]
    [InlineData(TextAlignX.Center, TextAlignY.Center, 35, 64, 13, 26)]
    [InlineData(TextAlignX.Right, TextAlignY.Bottom, 75, 99, 24, 39)]
    public void TextLineAlignsInsideItsBoxInAHeadlessPng(TextAlignX alignX, TextAlignY alignY,
        int left, int right, int top, int bottom)
    {
        const int width = 100;
        const int height = 40;
        var line = StandardUI.TextLine("UI", width, height, DeterministicTextMeasurer.Instance,
            color: ColorToken.Hex(0xffffffff), alignX: alignX, alignY: alignY, id: "line");
        var directory = Directory.CreateTempSubdirectory("machina-preview-");
        string path = Path.Combine(directory.FullName, "line.png");
        try
        {
            var surface = HeadlessPreview.WritePng(line, width, height, path);

            var ink = from y in Enumerable.Range(0, height)
                      from x in Enumerable.Range(0, width)
                      where surface.GetPixel(x, y).A > 0
                      select (X: x, Y: y);
            Assert.NotEmpty(ink);
            Assert.All(ink, point =>
            {
                Assert.InRange(point.X, left, right);
                Assert.InRange(point.Y, top, bottom);
            });

            byte[] png = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
            Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        }
        finally
        {
            File.Delete(path);
            directory.Delete();
        }
    }

    [Fact]
    public void DuplicateButtonLabelReportsBothParentsAndTheReservedId()
    {
        var declaration = UI.Layer(id: "root", children:
            [StandardUI.Button("Go", id: "go"), UI.Text("My label", id: "go.label")]);

        var error = Assert.Throws<UiLoweringError>(() => UiLowerer.Lower(declaration));

        Assert.Equal("DuplicateUiNodeId", error.Code);
        Assert.Contains("'go.label'", error.Message);
        Assert.Contains("TextNode under 'go.label-region'", error.Message);
        Assert.Contains("TextNode under 'root'", error.Message);
        Assert.Contains("reserve their generated child IDs", error.Message);
    }
}
