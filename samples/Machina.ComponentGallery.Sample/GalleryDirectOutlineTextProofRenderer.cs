using Machina.Fonts;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Layout.Documents;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Surface;
using RasterRgba32 = Machina.Renderer.Raster.Colors.Rgba32;

namespace Machina.ComponentGallery.Sample;

public static class GalleryDirectOutlineTextProofRenderer
{
    private static readonly FontFaceId CrimsonFace = new("CrimsonText-Regular");
    private static readonly string[] ProofSampleLines =
    [
        "Hello Machina",
        "Machina UI",
        "Settings",
        "Direct outline static text",
        "AV To Ta Wa Yo",
        "Aa0 1234567890",
        "The quick brown fox jumps over the lazy dog.",
    ];

    private static readonly string[] ComparisonLines =
    [
        "Hello Machina",
        "AV To Ta Wa Yo",
        "Aa0 1234567890",
    ];

    private static readonly int[] ProofSizes = [16, 24, 32];
    private static readonly Rgba32 ProofForeground = new(248, 250, 252, 255);
    private static readonly Rgba32 ProofMuted = new(203, 213, 225, 255);
    private static readonly Rgba32 ProofBackground = new(15, 23, 42, 255);
    private static readonly Rgba32 ComparisonBackground = new(17, 24, 39, 255);
    private static readonly Rgba32 BaselineGuideColor = new(59, 130, 246, 255);

    public static GalleryDirectOutlineTextProofPlacement BlitProof(
        RasterFrame frame,
        ResolvedLayoutDocument resolved,
        bool includeMsdfExperimentalComparison)
    {
        if (!GalleryDirectOutlineTextProofLayout.TryGetProofImageSlotRect(resolved, out var proofRect))
        {
            throw new InvalidOperationException("Direct-outline proof slot was not found in the resolved gallery layout.");
        }

        if (!GalleryDirectOutlineTextProofLayout.TryGetComparisonDirectSlotRect(resolved, out var directRect))
        {
            throw new InvalidOperationException("Direct-outline comparison slot was not found in the resolved gallery layout.");
        }

        var proofPlacement = ToPlacement(proofRect);
        var directPlacement = ToPlacement(directRect);

        RgbaImage proofImage = RenderProofImageAsync(proofPlacement.Width, proofPlacement.Height).GetAwaiter().GetResult();
        RgbaImage directComparisonImage = RenderDirectComparisonImageAsync(directPlacement.Width, directPlacement.Height).GetAwaiter().GetResult();

        Blit(frame.Surface, proofImage, proofPlacement.X, proofPlacement.Y);
        Blit(frame.Surface, directComparisonImage, directPlacement.X, directPlacement.Y);

        GalleryMsdfFontProofPlacement? msdfPlacement = null;

        if (includeMsdfExperimentalComparison)
        {
            if (!GalleryDirectOutlineTextProofLayout.TryGetComparisonMsdfSlotRect(resolved, out var msdfRect))
            {
                throw new InvalidOperationException("MSDF comparison slot was not found in the resolved gallery layout.");
            }

            var msdfComparisonPlacement = ToPlacement(msdfRect);
            RgbaImage msdfComparisonImage = GalleryMsdfFontProofRenderer.RenderComparisonImageAsync(
                msdfComparisonPlacement.Width,
                msdfComparisonPlacement.Height,
                ComparisonLines,
                24,
                CrimsonFace,
                GalleryFontFixturePaths.ResolveCrimsonTextPath(),
                "Crimson Text").GetAwaiter().GetResult();
            Blit(frame.Surface, msdfComparisonImage, msdfComparisonPlacement.X, msdfComparisonPlacement.Y);
            msdfPlacement = new GalleryMsdfFontProofPlacement(
                msdfComparisonPlacement.X,
                msdfComparisonPlacement.Y,
                msdfComparisonPlacement.Width,
                msdfComparisonPlacement.Height);
        }

        return new GalleryDirectOutlineTextProofPlacement(
            proofPlacement.X,
            proofPlacement.Y,
            proofPlacement.Width,
            proofPlacement.Height,
            directPlacement.X,
            directPlacement.Y,
            directPlacement.Width,
            directPlacement.Height,
            msdfPlacement);
    }

    public static IReadOnlyList<string> GetProofSampleLines()
    {
        return ProofSampleLines;
    }

    public static IReadOnlyList<int> GetProofSizes()
    {
        return ProofSizes;
    }

    private static async Task<RgbaImage> RenderProofImageAsync(int width, int height)
    {
        DirectOutlineStaticTextRenderer renderer = CreateDirectRenderer();
        RgbaImage canvas = CreateFilledImage(width, height, ProofBackground);

        double currentY = 16d;

        foreach (int size in ProofSizes)
        {
            currentY = await RenderLineAsync(renderer, canvas, $"{size}px", size + 2d, currentY, ProofMuted, showBaseline: false);
            currentY += 4d;

            foreach (string line in ProofSampleLines)
            {
                currentY = await RenderLineAsync(renderer, canvas, line, size, currentY, ProofForeground, showBaseline: false);
            }

            currentY += 10d;
        }

        return canvas;
    }

    private static async Task<RgbaImage> RenderComparisonImageAsync(int width, int height, bool includeMsdfExperimentalComparison)
    {
        int columnCount = includeMsdfExperimentalComparison ? 3 : 2;
        int gap = 12;
        int columnWidth = (width - ((columnCount - 1) * gap)) / columnCount;
        RgbaImage image = CreateFilledImage(width, height, ComparisonBackground);
        RgbaImage directImage = await RenderDirectComparisonImageAsync(columnWidth, height);
        CopyImage(image, directImage, 0, 0);

        if (includeMsdfExperimentalComparison)
        {
            RgbaImage msdfImage = await GalleryMsdfFontProofRenderer.RenderComparisonImageAsync(
                columnWidth,
                height,
                ComparisonLines,
                24,
                CrimsonFace,
                GalleryFontFixturePaths.ResolveCrimsonTextPath(),
                "Crimson Text");
            CopyImage(image, msdfImage, columnWidth + gap, 0);
        }

        return image;
    }

    private static async Task<RgbaImage> RenderDirectComparisonImageAsync(int width, int height)
    {
        DirectOutlineStaticTextRenderer renderer = CreateDirectRenderer();
        RgbaImage canvas = CreateFilledImage(width, height, ComparisonBackground);
        double currentY = 14d;

        foreach (string line in ComparisonLines)
        {
            currentY = await RenderLineAsync(renderer, canvas, line, 24d, currentY, ProofForeground, showBaseline: false);
            currentY += 2d;
        }

        return canvas;
    }

    private static DirectOutlineStaticTextRenderer CreateDirectRenderer()
    {
        TypographyGlyphOutlineSource source = new(new Dictionary<FontFaceId, TypographyFontFaceSource>
        {
            [CrimsonFace] = new(CrimsonFace, GalleryFontFixturePaths.ResolveCrimsonTextPath()),
        });

        return new DirectOutlineStaticTextRenderer(source);
    }

    private static async Task<double> RenderLineAsync(
        DirectOutlineStaticTextRenderer renderer,
        RgbaImage canvas,
        string text,
        double emSize,
        double top,
        Rgba32 foreground,
        bool showBaseline)
    {
        double baseline = top + emSize;
        DirectOutlineTextRenderResult result = await renderer.RenderAsync(
            new DirectOutlineTextRenderOptions(
                text,
                CrimsonFace,
                emSize,
                canvas.Width,
                canvas.Height,
                foreground,
                new Rgba32(0, 0, 0, 0),
                12d,
                baseline,
                Supersample: 4,
                ShowBaselineGuide: showBaseline,
                BaselineGuideColor: showBaseline ? BaselineGuideColor : null));

        if (!result.Success || result.Image is null)
        {
            string message = string.Join(
                " | ",
                result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
            throw new InvalidOperationException($"Direct-outline proof rendering failed for '{text}'. {message}");
        }

        CompositeImage(canvas, result.Image);

        double consumedHeight = result.InkBounds?.Bottom is int bottom
            ? Math.Max(emSize + 4d, bottom - top + 8d)
            : emSize + 8d;

        return top + consumedHeight;
    }

    private static void CompositeImage(RgbaImage target, RgbaImage source)
    {
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Rgba32 pixel = source.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    continue;
                }

                target.SetPixel(x, y, pixel);
            }
        }
    }

    private static void CopyImage(RgbaImage target, RgbaImage source, int targetX, int targetY)
    {
        for (int y = 0; y < source.Height; y++)
        {
            int destinationY = targetY + y;
            if ((uint)destinationY >= (uint)target.Height)
            {
                continue;
            }

            for (int x = 0; x < source.Width; x++)
            {
                int destinationX = targetX + x;
                if ((uint)destinationX >= (uint)target.Width)
                {
                    continue;
                }

                target.SetPixel(destinationX, destinationY, source.GetPixel(x, y));
            }
        }
    }

    private static RgbaImage CreateFilledImage(int width, int height, Rgba32 color)
    {
        RgbaImage image = new(width, height);

        for (int index = 0; index < image.Pixels.Length; index++)
        {
            image.Pixels[index] = color;
        }

        return image;
    }

    private static void Blit(RasterSurface target, RgbaImage source, int targetX, int targetY)
    {
        for (int y = 0; y < source.Height; y++)
        {
            int destinationY = targetY + y;
            if ((uint)destinationY >= (uint)target.Height)
            {
                continue;
            }

            for (int x = 0; x < source.Width; x++)
            {
                int destinationX = targetX + x;
                if ((uint)destinationX >= (uint)target.Width)
                {
                    continue;
                }

                Rgba32 pixel = source.GetPixel(x, y);
                target.SetPixel(destinationX, destinationY, new RasterRgba32(pixel.R, pixel.G, pixel.B, pixel.A));
            }
        }
    }

    private static (int X, int Y, int Width, int Height) ToPlacement(Machina.Layout.Geometry.Rect rect)
    {
        return (
            (int)Math.Round(rect.X),
            (int)Math.Round(rect.Y),
            Math.Max(1, (int)Math.Round(rect.Width)),
            Math.Max(1, (int)Math.Round(rect.Height)));
    }
}
