using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Toml;
using Machina.Layout.Documents;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Surface;
using RasterRgba32 = Machina.Renderer.Raster.Colors.Rgba32;

namespace Machina.ComponentGallery.Sample;

public static class GalleryMsdfFontProofRenderer
{
    private static readonly FontFaceId Face = new("SpaceMono-Regular");
    private static readonly string[] ProofLines =
    [
        "Machina",
        "Aa0",
        "Hello Machina",
    ];

    private static readonly Machina.Fonts.ReferenceRendering.Rgba32 ProofForeground = new(248, 250, 252, 255);
    private static readonly Machina.Fonts.ReferenceRendering.Rgba32 ProofBackground = new(17, 24, 39, 255);

    public static GalleryMsdfFontProofPlacement BlitProof(RasterFrame frame, ResolvedLayoutDocument resolved)
    {
        if (!GalleryMsdfFontProofLayout.TryGetImageSlotRect(resolved, out var slotRect))
        {
            throw new InvalidOperationException("MSDF proof slot was not found in the resolved gallery layout.");
        }

        int targetX = (int)Math.Round(slotRect.X);
        int targetY = (int)Math.Round(slotRect.Y);
        int targetWidth = Math.Max(1, (int)Math.Round(slotRect.Width));
        int targetHeight = Math.Max(1, (int)Math.Round(slotRect.Height));

        RgbaImage proofImage = RenderCompositeProofAsync(targetWidth, targetHeight).GetAwaiter().GetResult();
        Blit(frame.Surface, proofImage, targetX, targetY);

        return new GalleryMsdfFontProofPlacement(targetX, targetY, targetWidth, targetHeight);
    }

    private static async Task<RgbaImage> RenderCompositeProofAsync(int width, int height)
    {
        RgbaImage image = CreateFilledImage(width, height, ProofBackground);

        string tempRoot = Path.Combine(Path.GetTempPath(), "machina-component-gallery-msdf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            DistanceFieldTextPipeline pipeline = CreatePipeline();
            int lineHeight = 34;
            int top = 10;
            int gap = 5;

            for (int index = 0; index < ProofLines.Length; index++)
            {
                DistanceFieldTextPipelineResult lineResult = await pipeline.RenderTextAsync(
                    ProofLines[index],
                    CreateLineOptions(width, lineHeight),
                    Path.Combine(tempRoot, $"line-{index}"));

                if (!lineResult.Success || lineResult.Image is null)
                {
                    string message = string.Join(
                        " | ",
                        lineResult.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
                    throw new InvalidOperationException($"MSDF proof rendering failed for '{ProofLines[index]}'. {message}");
                }

                CopyLine(image, lineResult.Image, top + ((lineHeight + gap) * index));
            }

            return image;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static DistanceFieldTextPipeline CreatePipeline()
    {
        string fontPath = ResolveFixtureFontPath();
        TypographyGlyphOutlineSource outlineSource = new(
            new Dictionary<FontFaceId, TypographyFontFaceSource>
            {
                [Face] = new(Face, fontPath),
            });

        return new DistanceFieldTextPipeline(
            outlineSource,
            new MsdfSharpDistanceFieldGenerator(),
            new FontAtlasTomlExportMetadata(
                "component-gallery-msdf-proof",
                "msdf",
                "Space Mono",
                "Regular",
                fontPath,
                "sha256-space-mono",
                "OFL-1.1",
                new FontAtlasMetricsToml
                {
                    EmSize = 24,
                    UnitsPerEm = 1000,
                    Ascent = 19.2,
                    Descent = -4.8,
                    LineGap = 0,
                    LineHeight = 24,
                },
                new FontAtlasMsdfToml
                {
                    Range = 4,
                    Scale = 1,
                    EdgeColoring = "simple",
                    MiterLimit = 2,
                }));
    }

    private static DistanceFieldTextRenderOptions CreateLineOptions(int width, int height)
    {
        return new DistanceFieldTextRenderOptions(
            OutputWidth: width,
            OutputHeight: height,
            Face: Face,
            EmSize: 24,
            Weight: MachinaFontWeight.Regular,
            Slant: MachinaFontSlant.Upright,
            Kind: DistanceFieldKind.Msdf,
            FieldWidth: 32,
            FieldHeight: 32,
            PixelRange: 4d,
            Foreground: ProofForeground,
            Background: ProofBackground,
            X: 12d,
            BaselineY: 26d,
            FlipY: true,
            PageWidth: 128,
            PageHeight: 128,
            PagePadding: 2);
    }

    private static RgbaImage CreateFilledImage(int width, int height, Machina.Fonts.ReferenceRendering.Rgba32 color)
    {
        RgbaImage image = new(width, height);

        for (int i = 0; i < image.Pixels.Length; i++)
        {
            image.Pixels[i] = color;
        }

        return image;
    }

    private static void CopyLine(RgbaImage target, RgbaImage lineImage, int targetY)
    {
        for (int y = 0; y < lineImage.Height; y++)
        {
            int destinationY = targetY + y;
            if ((uint)destinationY >= (uint)target.Height)
            {
                continue;
            }

            for (int x = 0; x < lineImage.Width; x++)
            {
                target.SetPixel(x, destinationY, lineImage.GetPixel(x, y));
            }
        }
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

                Machina.Fonts.ReferenceRendering.Rgba32 pixel = source.GetPixel(x, y);
                target.SetPixel(destinationX, destinationY, new RasterRgba32(pixel.R, pixel.G, pixel.B, pixel.A));
            }
        }
    }

    private static string ResolveFixtureFontPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "SpaceMono-Regular.ttf");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The MSDF gallery proof font fixture was not found. Expected SpaceMono-Regular.ttf in the sample output.",
                path);
        }

        return path;
    }
}
