using System.Text.Json;
using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;
using Xunit;

namespace Machina.Fonts.Tests.Rendering;

[Collection("EnvironmentVariable")]
public sealed class ThreeWayShapeDiffWorkflowTests
{
    [Fact]
    public void OutlineFlattening_LineSegment_IsStable()
    {
        GlyphContour contour = new([
            new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(4, 0)),
        ]);

        FlattenedGlyphContour flattened = OutlineFlattening.FlattenContour(contour, new OutlineFlatteningOptions(4));

        Assert.Equal(
            [new GlyphPoint(0, 0), new GlyphPoint(4, 0)],
            flattened.Points);
    }

    [Fact]
    public void OutlineFlattening_QuadraticSegment_IsStable()
    {
        GlyphContour contour = new([
            new GlyphQuadraticSegment(
                new GlyphPoint(0, 0),
                new GlyphPoint(2, 4),
                new GlyphPoint(4, 0)),
        ]);

        FlattenedGlyphContour flattened = OutlineFlattening.FlattenContour(contour, new OutlineFlatteningOptions(4));

        Assert.Equal(5, flattened.Points.Count);
        AssertPoint(flattened.Points[0], 0, 0);
        AssertPoint(flattened.Points[1], 1, 1.5);
        AssertPoint(flattened.Points[2], 2, 2);
        AssertPoint(flattened.Points[3], 3, 1.5);
        AssertPoint(flattened.Points[4], 4, 0);
    }

    [Fact]
    public void OutlineFlattening_CubicSegment_IsStable()
    {
        GlyphContour contour = new([
            new GlyphCubicSegment(
                new GlyphPoint(0, 0),
                new GlyphPoint(0, 4),
                new GlyphPoint(4, 4),
                new GlyphPoint(4, 0)),
        ]);

        FlattenedGlyphContour flattened = OutlineFlattening.FlattenContour(contour, new OutlineFlatteningOptions(4));

        Assert.Equal(5, flattened.Points.Count);
        AssertPoint(flattened.Points[0], 0, 0);
        AssertPoint(flattened.Points[1], 0.625, 2.25);
        AssertPoint(flattened.Points[2], 2, 3);
        AssertPoint(flattened.Points[3], 3.375, 2.25);
        AssertPoint(flattened.Points[4], 4, 0);
    }

    [Fact]
    public void DirectOutlineMask_Rectangle_FillsExpectedArea()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("test"), 'A', 32);
        GlyphOutline outline = CreateRectangleOutline(key, 4, 3);
        DistanceFieldTextLayoutResult layout = CreateLayout(key, outline.Metrics, 2, 8);
        InkMask mask = DirectOutlineMaskRenderer.RenderMask(
            new Dictionary<GlyphKey, GlyphOutline> { [key] = outline },
            layout,
            new DirectOutlineMaskRenderOptions(12, 12, Rgba32.White, Rgba32.Black, 2, 8, Supersample: 1));

        Assert.Equal(12, CountInk(mask));
        Assert.True(mask.IsInk(2, 5));
        Assert.True(mask.IsInk(5, 7));
    }

    [Fact]
    public void DirectOutlineMask_Hole_UsesDocumentedFillRule()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("test"), 'O', 32);
        GlyphOutline outline = CreateHoledRectangleOutline(key);
        DistanceFieldTextLayoutResult layout = CreateLayout(key, outline.Metrics, 2, 10);
        InkMask mask = DirectOutlineMaskRenderer.RenderMask(
            new Dictionary<GlyphKey, GlyphOutline> { [key] = outline },
            layout,
            new DirectOutlineMaskRenderOptions(16, 16, Rgba32.White, Rgba32.Black, 2, 10, Supersample: 1));

        Assert.True(mask.IsInk(2, 4));
        Assert.False(mask.IsInk(5, 7));
    }

    [Fact]
    public void DirectOutlineMask_IsDeterministic()
    {
        GlyphKey key = GlyphKey.FromChar(new FontFaceId("test"), 'A', 32);
        GlyphOutline outline = CreateRectangleOutline(key, 5, 4);
        DistanceFieldTextLayoutResult layout = CreateLayout(key, outline.Metrics, 3, 9);

        InkMask left = DirectOutlineMaskRenderer.RenderMask(
            new Dictionary<GlyphKey, GlyphOutline> { [key] = outline },
            layout,
            new DirectOutlineMaskRenderOptions(16, 16, Rgba32.White, Rgba32.Black, 3, 9, Supersample: 4));

        InkMask right = DirectOutlineMaskRenderer.RenderMask(
            new Dictionary<GlyphKey, GlyphOutline> { [key] = outline },
            layout,
            new DirectOutlineMaskRenderOptions(16, 16, Rgba32.White, Rgba32.Black, 3, 9, Supersample: 4));

        Assert.Equal(ToCoverageBytes(left), ToCoverageBytes(right));
    }

    [Fact]
    public void InkMask_IgnoresBaselineGuideColor()
    {
        RgbaImage image = CreateFilledImage(8, 8, new Rgba32(16, 16, 24, 255));
        FillRect(image, 0, 4, 7, 4, new Rgba32(255, 0, 0, 255));
        image.SetPixel(5, 2, new Rgba32(240, 240, 240, 255));

        InkMask mask = InkMask.FromImage(
            image,
            new InkMaskExtractionOptions(new Rgba32(16, 16, 24, 255), new Rgba32(255, 0, 0, 255)));

        Assert.True(mask.IsInk(5, 2));
        Assert.False(mask.IsInk(0, 4));
    }

    [Fact]
    public void InkMask_ComputesBounds()
    {
        InkMask mask = new(10, 10);
        for (int y = 3; y <= 7; y++)
        {
            for (int x = 2; x <= 6; x++)
            {
                mask.SetCoverage(x, y, 1f);
            }
        }

        Assert.Equal(new InkMaskBounds(2, 3, 6, 7), mask.ComputeBounds());
    }

    [Fact]
    public void EdgeExtraction_ExtractsBoundary()
    {
        InkMask mask = new(6, 6);
        for (int y = 1; y <= 3; y++)
        {
            for (int x = 1; x <= 3; x++)
            {
                mask.SetCoverage(x, y, 1f);
            }
        }

        IReadOnlyList<InkMaskPoint> edges = mask.ExtractEdges();

        Assert.Equal(8, edges.Count);
        Assert.DoesNotContain(edges, static point => point.X == 2 && point.Y == 2);
    }

    [Fact]
    public void ShapeDiff_IdenticalMasks_PerfectIoU()
    {
        InkMask left = CreateRectMask(8, 8, 1, 1, 3, 3);
        InkMask right = CreateRectMask(8, 8, 1, 1, 3, 3);

        ShapeDiffMetrics metrics = InkMaskDiff.Compare(left, right, baselineY: 6);

        Assert.Equal(1d, metrics.IntersectionOverUnion);
        Assert.Equal(0d, metrics.MeanEdgeDistance);
        Assert.Equal(0, metrics.LeftOnlyArea);
        Assert.Equal(0, metrics.RightOnlyArea);
    }

    [Fact]
    public void ShapeDiff_ShiftedMasks_ReportsDistance()
    {
        InkMask left = CreateRectMask(8, 8, 1, 1, 3, 3);
        InkMask right = CreateRectMask(8, 8, 2, 1, 4, 3);

        ShapeDiffMetrics metrics = InkMaskDiff.Compare(left, right, baselineY: 6);

        Assert.Equal(1, metrics.DeltaLeft);
        Assert.True(metrics.MeanEdgeDistance > 0d);
        Assert.True(metrics.P95EdgeDistance >= 1d);
    }

    [Fact]
    public void ShapeDiff_ExtraLowerPixels_ReportsBelowBaselineExtraArea()
    {
        InkMask left = CreateRectMask(8, 8, 1, 1, 3, 3);
        InkMask right = CreateRectMask(8, 8, 1, 1, 3, 4);

        ShapeDiffMetrics metrics = InkMaskDiff.Compare(left, right, baselineY: 3.5);

        Assert.True(metrics.BelowBaselineExtraArea > 0);
        Assert.Equal(0, metrics.AboveBaselineExtraArea);
    }

    [Fact]
    public async Task ThreeWayShapeDiffWorkflow_WritesReport()
    {
        string directory = CreateDirectory();
        string capturePath = WriteSyntheticBrowserCaptureJson(directory);
        string? previous = Environment.GetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable, capturePath);

            ThreeWayShapeDiffExportResult result = await ThreeWayShapeDiffWorkflow.ExportAsync(directory);

            Assert.True(File.Exists(result.ReportJsonPath));
            Assert.True(File.Exists(result.ReportTextPath));
            Assert.Contains("overallFinding", File.ReadAllText(result.ReportTextPath));
            Assert.Contains("\"OverallFinding\"", File.ReadAllText(result.ReportJsonPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task ThreeWayShapeDiffWorkflow_WritesArtifacts()
    {
        string directory = CreateDirectory();
        string capturePath = WriteSyntheticBrowserCaptureJson(directory);
        string? previous = Environment.GetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable, capturePath);

            await ThreeWayShapeDiffWorkflow.ExportAsync(directory);

            foreach (ShapeDiffCanvasDefinition canvas in ThreeWayShapeDiffWorkflow.CanvasDefinitions)
            {
                string sizeDirectory = Path.Combine(directory, canvas.SizeDirectoryName);
                foreach (ShapeDiffTextDefinition definition in ThreeWayShapeDiffWorkflow.TextDefinitions)
                {
                    Assert.True(File.Exists(Path.Combine(sizeDirectory, definition.BrowserPngFileName)));
                    Assert.True(File.Exists(Path.Combine(sizeDirectory, definition.DirectOutlinePngFileName)));
                    Assert.True(File.Exists(Path.Combine(sizeDirectory, definition.MsdfPngFileName)));
                    Assert.True(File.Exists(Path.Combine(sizeDirectory, definition.BrowserVsDirectDiffPngFileName)));
                    Assert.True(File.Exists(Path.Combine(sizeDirectory, definition.DirectVsMsdfDiffPngFileName)));
                    Assert.True(File.Exists(Path.Combine(sizeDirectory, definition.BrowserVsMsdfDiffPngFileName)));
                    Assert.True(File.Exists(Path.Combine(sizeDirectory, definition.ThreeWayOverlayPngFileName)));
                    Assert.True(File.Exists(Path.Combine(sizeDirectory, definition.WireframePngFileName)));
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task ThreeWayShapeDiffWorkflow_RunsForMultipleFontSizes()
    {
        string directory = CreateDirectory();
        string capturePath = WriteSyntheticBrowserCaptureJson(directory);
        string? previous = Environment.GetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable, capturePath);

            ThreeWayShapeDiffExportResult result = await ThreeWayShapeDiffWorkflow.ExportAsync(directory);

            Assert.Equal(new[] { 32, 48, 64 }, result.Report.FontSizes);
            Assert.All(ThreeWayShapeDiffWorkflow.CanvasDefinitions, canvas =>
            {
                Assert.True(Directory.Exists(Path.Combine(directory, canvas.SizeDirectoryName)));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task ThreeWayShapeDiffWorkflow_ScriptWorkflowExportsArtifacts()
    {
        string directory = ThreeWayShapeDiffWorkflow.GetRequestedOutputDirectoryOrCreateTemp();
        Directory.CreateDirectory(directory);

        string capturePath = Environment.GetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable)
            ?? WriteSyntheticBrowserCaptureJson(directory);
        string? previousCapture = Environment.GetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable, capturePath);

            ThreeWayShapeDiffExportResult result = await ThreeWayShapeDiffWorkflow.ExportAsync(directory);

            Assert.True(File.Exists(result.ReportJsonPath));
            Assert.True(File.Exists(result.ReportTextPath));
            Assert.True(File.Exists(Path.Combine(directory, "32", "overlay-three-way-machina.png")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ThreeWayShapeDiffWorkflow.BrowserCapturePathEnvironmentVariable, previousCapture);
        }
    }

    private static GlyphOutline CreateRectangleOutline(GlyphKey key, double width, double height)
    {
        GlyphMetrics metrics = new(width, 0, height, width, height);
        GlyphContour contour = new([
            new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(width, 0)),
            new GlyphLineSegment(new GlyphPoint(width, 0), new GlyphPoint(width, height)),
            new GlyphLineSegment(new GlyphPoint(width, height), new GlyphPoint(0, height)),
            new GlyphLineSegment(new GlyphPoint(0, height), new GlyphPoint(0, 0)),
        ]);

        return new GlyphOutline(key, metrics, new GlyphBounds(0, 0, width, height), [contour]);
    }

    private static GlyphOutline CreateHoledRectangleOutline(GlyphKey key)
    {
        GlyphMetrics metrics = new(6, 0, 6, 6, 6);
        GlyphContour outer = new([
            new GlyphLineSegment(new GlyphPoint(0, 0), new GlyphPoint(6, 0)),
            new GlyphLineSegment(new GlyphPoint(6, 0), new GlyphPoint(6, 6)),
            new GlyphLineSegment(new GlyphPoint(6, 6), new GlyphPoint(0, 6)),
            new GlyphLineSegment(new GlyphPoint(0, 6), new GlyphPoint(0, 0)),
        ]);
        GlyphContour inner = new([
            new GlyphLineSegment(new GlyphPoint(2, 2), new GlyphPoint(4, 2)),
            new GlyphLineSegment(new GlyphPoint(4, 2), new GlyphPoint(4, 4)),
            new GlyphLineSegment(new GlyphPoint(4, 4), new GlyphPoint(2, 4)),
            new GlyphLineSegment(new GlyphPoint(2, 4), new GlyphPoint(2, 2)),
        ]);

        return new GlyphOutline(key, metrics, new GlyphBounds(0, 0, 6, 6), [outer, inner]);
    }

    private static DistanceFieldTextLayoutResult CreateLayout(GlyphKey key, GlyphMetrics metrics, double x, double baselineY)
    {
        return new DistanceFieldTextLayoutResult(
            [new DistanceFieldGlyphPlacement(key, metrics, x, baselineY, 1d, false)],
            metrics.Advance,
            key.EmSize,
            []);
    }

    private static int CountInk(InkMask mask)
    {
        int count = 0;
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                if (mask.IsInk(x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static byte[] ToCoverageBytes(InkMask mask)
    {
        byte[] bytes = new byte[mask.Width * mask.Height];
        int index = 0;

        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                bytes[index++] = (byte)Math.Round(mask.GetCoverage(x, y) * 255f, MidpointRounding.AwayFromZero);
            }
        }

        return bytes;
    }

    private static InkMask CreateRectMask(int width, int height, int left, int top, int right, int bottom)
    {
        InkMask mask = new(width, height);
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                mask.SetCoverage(x, y, 1f);
            }
        }

        return mask;
    }

    private static string WriteSyntheticBrowserCaptureJson(string directory)
    {
        string path = Path.Combine(directory, ThreeWayShapeDiffWorkflow.BrowserCaptureFileName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, CreateSyntheticBrowserCaptureJson());
        return path;
    }

    private static string CreateSyntheticBrowserCaptureJson()
    {
        List<object> fixtures = [];
        int fixtureIndex = 0;

        foreach (ShapeDiffCanvasDefinition canvas in ThreeWayShapeDiffWorkflow.CanvasDefinitions)
        {
            foreach (ShapeDiffTextDefinition definition in ThreeWayShapeDiffWorkflow.TextDefinitions)
            {
                RgbaImage image = CreateFilledImage(canvas.Width, canvas.Height, new Rgba32(16, 16, 24, 255));
                int left = (int)canvas.OriginX + 4 + (fixtureIndex % 5);
                int top = Math.Max(2, (int)canvas.BaselineY - (canvas.SizePx / 2));
                int right = Math.Min(image.Width - 4, left + (canvas.SizePx + 18));
                int bottom = Math.Min(image.Height - 4, top + (canvas.SizePx / 2));

                FillRect(image, left, top, right, bottom, new Rgba32(240, 240, 240, 255));
                FillRect(image, 0, (int)canvas.BaselineY, image.Width - 1, (int)canvas.BaselineY, new Rgba32(255, 0, 0, 255));

                fixtures.Add(new
                {
                    id = definition.Id,
                    sizePx = canvas.SizePx,
                    text = definition.Text,
                    fontFamily = "CrimsonText-Regular",
                    fontSize = canvas.SizePx,
                    canvasWidth = canvas.Width,
                    canvasHeight = canvas.Height,
                    x = canvas.OriginX,
                    baselineY = canvas.BaselineY,
                    baselineGuideEnabled = true,
                    baselineGuideY = canvas.BaselineY,
                    baselineGuideColor = "#ff0000",
                    textBaseline = "alphabetic",
                    textAlign = "left",
                    metrics = new
                    {
                        width = canvas.SizePx + 18d,
                        actualBoundingBoxLeft = 0d,
                        actualBoundingBoxRight = canvas.SizePx + 18d,
                        actualBoundingBoxAscent = canvas.SizePx / 2d,
                        actualBoundingBoxDescent = 4d,
                        fontBoundingBoxAscent = (canvas.SizePx / 2d) + 2d,
                        fontBoundingBoxDescent = 6d,
                        emHeightAscent = (double?)null,
                        emHeightDescent = (double?)null,
                        alphabeticBaseline = 0d,
                        hangingBaseline = canvas.SizePx / 2d,
                        ideographicBaseline = -6d,
                    },
                    capture = new
                    {
                        width = image.Width,
                        height = image.Height,
                        pixelFormat = "rgba8",
                        rgbaBase64 = Convert.ToBase64String(ToRgbaBytes(image)),
                    },
                });

                fixtureIndex++;
            }
        }

        return JsonSerializer.Serialize(new
        {
            generatedAtUtc = "2026-06-28T00:00:00Z",
            browserPath = "synthetic",
            fixtureHtmlPath = "synthetic",
            fixtures,
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static byte[] ToRgbaBytes(RgbaImage image)
    {
        byte[] bytes = new byte[image.Width * image.Height * 4];
        int index = 0;

        foreach (Rgba32 pixel in image.Pixels)
        {
            bytes[index++] = pixel.R;
            bytes[index++] = pixel.G;
            bytes[index++] = pixel.B;
            bytes[index++] = pixel.A;
        }

        return bytes;
    }

    private static RgbaImage CreateFilledImage(int width, int height, Rgba32 color)
    {
        RgbaImage image = new(width, height);
        CpuDistanceFieldGlyphRenderer.Fill(image, color);
        return image;
    }

    private static void FillRect(RgbaImage image, int left, int top, int right, int bottom, Rgba32 color)
    {
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                image.SetPixel(x, y, color);
            }
        }
    }

    private static void AssertPoint(GlyphPoint point, double x, double y)
    {
        Assert.True(Math.Abs(point.X - x) < 0.0001d, $"Expected X={x}, actual={point.X}.");
        Assert.True(Math.Abs(point.Y - y) < 0.0001d, $"Expected Y={y}, actual={point.Y}.");
    }

    private static string CreateDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8s-tests", Guid.NewGuid().ToString("N"));
    }
}
