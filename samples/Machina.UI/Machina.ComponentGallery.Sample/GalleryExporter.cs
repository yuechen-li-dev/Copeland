using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Machina.Fonts.ReferenceRendering;
using Machina.Pipeline;
using Machina.Standard.Theme;

namespace Machina.ComponentGallery.Sample;

public static class GalleryExporter
{
    public static GalleryExportResult Export(GalleryProgramOptions options)
    {
        return Export(
            options.InitialState,
            options.ExportDirectory,
            options.ExportName,
            new GalleryProofOptions(
                options.IncludeDirectOutlineTextProof,
                options.IncludeDirectOutlineRenderBridgeProof,
                options.IncludeDirectOutlineTextLayoutProof,
                options.IncludeMsdfFontProof));
    }

    public static GalleryExportResult Export(
        GalleryState state,
        string outputDirectory,
        string exportName,
        bool includeMsdfFontProof = false)
    {
        return Export(state, outputDirectory, exportName, new GalleryProofOptions(IncludeMsdfFontProof: includeMsdfFontProof));
    }

    public static GalleryExportResult Export(
        GalleryState state,
        string outputDirectory,
        string exportName,
        GalleryProofOptions proofOptions)
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);

        var outputPath = BuildOutputPath(fullOutputDirectory, exportName);
        var galleryHeight = GalleryScreen.GetHeight(proofOptions);
        var frame = MachinaAurelianCpuRasterComposition.Render(
            GalleryScreen.Build(state, proofOptions, StandardTheme.Default),
            GalleryScreen.Width,
            galleryHeight);
        GalleryDirectOutlineTextProofPlacement? directOutlineProofPlacement = null;
        GalleryDirectOutlineRenderBridgeProofPlacement? directOutlineRenderBridgeProofPlacement = null;
        GalleryDirectOutlineTextLayoutProofPlacement? directOutlineTextLayoutProofPlacement = null;
        GalleryMsdfFontProofPlacement? msdfProofPlacement = null;

        if (proofOptions.IncludeDirectOutlineTextProof)
        {
            directOutlineProofPlacement = GalleryDirectOutlineTextProofRenderer.BlitProof(
                frame.RasterFrame,
                frame.Resolved,
                proofOptions.IncludeMsdfFontProof);
        }

        if (proofOptions.IncludeDirectOutlineTextLayoutProof)
        {
            directOutlineTextLayoutProofPlacement = GalleryDirectOutlineTextLayoutProofRenderer.BlitProof(
                frame.RasterFrame,
                frame.Resolved);
        }

        if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            directOutlineRenderBridgeProofPlacement = GalleryDirectOutlineRenderBridgeProofRenderer.BlitProof(
                frame.RasterFrame,
                frame.Resolved);
        }

        if (proofOptions.IncludeMsdfFontProof)
        {
            msdfProofPlacement = GalleryMsdfFontProofRenderer.BlitProof(frame.RasterFrame, frame.Resolved);
        }

        GalleryPngWriter.Write(outputPath, frame.RasterFrame);

        GalleryDirectOutlineTextProofArtifacts? directOutlineArtifacts = null;
        if (proofOptions.IncludeDirectOutlineTextProof)
        {
            string standaloneProofPath = GalleryExportContract.GetDirectOutlineStandaloneOutputPath(fullOutputDirectory);
            string comparisonPath = GalleryExportContract.GetTextBackendComparisonOutputPath(fullOutputDirectory);

            GalleryPngWriter.Write(
                standaloneProofPath,
                Crop(frame.RasterFrame, directOutlineProofPlacement!.ProofX, directOutlineProofPlacement.ProofY, directOutlineProofPlacement.ProofWidth, directOutlineProofPlacement.ProofHeight));

            if (!GalleryDirectOutlineTextProofLayout.TryGetComparisonSurfaceRect(frame.Resolved, out var comparisonRect))
            {
                throw new InvalidOperationException("Direct-outline comparison surface was not found in the resolved gallery layout.");
            }

            GalleryPngWriter.Write(comparisonPath, Crop(frame.RasterFrame, comparisonRect));

            directOutlineArtifacts = new GalleryDirectOutlineTextProofArtifacts(standaloneProofPath, comparisonPath);
        }

        GalleryDirectOutlineTextLayoutProofArtifacts? directOutlineTextLayoutArtifacts = null;
        if (proofOptions.IncludeDirectOutlineTextLayoutProof)
        {
            string standaloneLayoutProofPath = GalleryExportContract.GetDirectOutlineTextBoxLayoutOutputPath(fullOutputDirectory);
            string alignmentGridPath = GalleryExportContract.GetDirectOutlineTextAlignmentGridOutputPath(fullOutputDirectory);

            GalleryPngWriter.Write(
                standaloneLayoutProofPath,
                Crop(
                    frame.RasterFrame,
                    directOutlineTextLayoutProofPlacement!.ProofX,
                    directOutlineTextLayoutProofPlacement.ProofY,
                    directOutlineTextLayoutProofPlacement.ProofWidth,
                    directOutlineTextLayoutProofPlacement.ProofHeight));

            GalleryPngWriter.Write(
                alignmentGridPath,
                Crop(
                    frame.RasterFrame,
                    directOutlineTextLayoutProofPlacement.AlignmentGridX,
                    directOutlineTextLayoutProofPlacement.AlignmentGridY,
                    directOutlineTextLayoutProofPlacement.AlignmentGridWidth,
                    directOutlineTextLayoutProofPlacement.AlignmentGridHeight));

            directOutlineTextLayoutArtifacts = new GalleryDirectOutlineTextLayoutProofArtifacts(
                standaloneLayoutProofPath,
                alignmentGridPath);
        }

        GalleryDirectOutlineRenderBridgeProofArtifacts? directOutlineRenderBridgeArtifacts = null;
        if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            string standaloneBridgeProofPath = GalleryExportContract.GetDirectOutlineRenderBridgeOutputPath(fullOutputDirectory);
            string bridgeLayoutGridPath = GalleryExportContract.GetDirectOutlineRenderBridgeLayoutGridOutputPath(fullOutputDirectory);

            GalleryPngWriter.Write(
                standaloneBridgeProofPath,
                Crop(
                    frame.RasterFrame,
                    directOutlineRenderBridgeProofPlacement!.ProofX,
                    directOutlineRenderBridgeProofPlacement.ProofY,
                    directOutlineRenderBridgeProofPlacement.ProofWidth,
                    directOutlineRenderBridgeProofPlacement.ProofHeight));

            GalleryPngWriter.Write(
                bridgeLayoutGridPath,
                Crop(
                    frame.RasterFrame,
                    directOutlineRenderBridgeProofPlacement.AlignmentGridX,
                    directOutlineRenderBridgeProofPlacement.AlignmentGridY,
                    directOutlineRenderBridgeProofPlacement.AlignmentGridWidth,
                    directOutlineRenderBridgeProofPlacement.AlignmentGridHeight));

            directOutlineRenderBridgeArtifacts = new GalleryDirectOutlineRenderBridgeProofArtifacts(
                standaloneBridgeProofPath,
                bridgeLayoutGridPath);
        }

        return new GalleryExportResult(
            OutputPath: outputPath,
            Width: frame.RasterFrame.Width,
            Height: frame.RasterFrame.Height,
            ProofOptions: proofOptions,
            DirectOutlineProofPlacement: directOutlineProofPlacement,
            DirectOutlineRenderBridgeProofPlacement: directOutlineRenderBridgeProofPlacement,
            DirectOutlineTextLayoutProofPlacement: directOutlineTextLayoutProofPlacement,
            MsdfProofPlacement: msdfProofPlacement,
            DirectOutlineArtifacts: directOutlineArtifacts,
            DirectOutlineRenderBridgeArtifacts: directOutlineRenderBridgeArtifacts,
            DirectOutlineTextLayoutArtifacts: directOutlineTextLayoutArtifacts);
    }

    public static WriteableBitmap ToBitmap(RasterFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var locked = bitmap.Lock();
        var pixelBytes = ToRgbaBytes(frame);
        System.Runtime.InteropServices.Marshal.Copy(pixelBytes, 0, locked.Address, pixelBytes.Length);

        return bitmap;
    }

    private static string BuildOutputPath(string outputDirectory, string exportName)
    {
        var fileName = exportName;
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(extension))
        {
            fileName += ".png";
        }
        else if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Gallery export names must omit the extension or end with .png.");
        }

        return Path.Combine(outputDirectory, fileName);
    }

    private static byte[] ToRgbaBytes(RasterFrame frame)
    {
        var width = frame.Surface.Width;
        var height = frame.Surface.Height;
        var bytes = new byte[width * height * 4];
        var index = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = frame.Surface.GetPixel(x, y);
                bytes[index++] = pixel.R;
                bytes[index++] = pixel.G;
                bytes[index++] = pixel.B;
                bytes[index++] = pixel.A;
            }
        }

        return bytes;
    }

    private static RgbaImage Crop(RasterFrame frame, Machina.Layout.Geometry.Rect rect)
    {
        return Crop(
            frame,
            (int)Math.Round(rect.X),
            (int)Math.Round(rect.Y),
            Math.Max(1, (int)Math.Round(rect.Width)),
            Math.Max(1, (int)Math.Round(rect.Height)));
    }

    private static RgbaImage Crop(RasterFrame frame, int x, int y, int width, int height)
    {
        RgbaImage image = new(width, height);

        for (int row = 0; row < height; row++)
        {
            int sourceY = y + row;
            for (int column = 0; column < width; column++)
            {
                int sourceX = x + column;
                var pixel = frame.Surface.GetPixel(sourceX, sourceY);
                image.SetPixel(column, row, new Rgba32(pixel.R, pixel.G, pixel.B, pixel.A));
            }
        }

        return image;
    }

}

public sealed record GalleryExportResult(
    string OutputPath,
    int Width,
    int Height,
    GalleryProofOptions ProofOptions,
    GalleryDirectOutlineTextProofPlacement? DirectOutlineProofPlacement,
    GalleryDirectOutlineRenderBridgeProofPlacement? DirectOutlineRenderBridgeProofPlacement,
    GalleryDirectOutlineTextLayoutProofPlacement? DirectOutlineTextLayoutProofPlacement,
    GalleryMsdfFontProofPlacement? MsdfProofPlacement,
    GalleryDirectOutlineTextProofArtifacts? DirectOutlineArtifacts,
    GalleryDirectOutlineRenderBridgeProofArtifacts? DirectOutlineRenderBridgeArtifacts,
    GalleryDirectOutlineTextLayoutProofArtifacts? DirectOutlineTextLayoutArtifacts);

public sealed record GalleryDirectOutlineTextProofArtifacts(
    string StandaloneProofPath,
    string ComparisonPath);

public sealed record GalleryDirectOutlineTextLayoutProofArtifacts(
    string StandaloneProofPath,
    string AlignmentGridPath);

public sealed record GalleryDirectOutlineRenderBridgeProofArtifacts(
    string StandaloneProofPath,
    string AlignmentGridPath);
