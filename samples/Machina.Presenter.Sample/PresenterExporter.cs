using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Machina.Pipeline;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class PresenterExporter
{
    public static PresenterExportResult Export(PresenterProgramOptions options)
    {
        return Export(DemoState.Default, options.OutputPath, options.ProofOptions, null);
    }

    public static PresenterExportResult Export(
        DemoState state,
        string outputPath,
        PresenterProofOptions proofOptions,
        StandardTheme? theme = null)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        StandardTheme effectiveTheme = theme ?? Program.AppTheme;
        var pipeline = new MachinaRasterPipeline();
        var document = SettingsScreen.Build(state, effectiveTheme, proofOptions);
        var frame = pipeline.Render(
            document,
            SettingsScreen.GetWidth(proofOptions),
            SettingsScreen.GetHeight(proofOptions));

        PresenterDirectOutlineRenderBridgeProofPlacement? placement = null;

        if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
        {
            placement = PresenterDirectOutlineRenderBridgeProofRenderer.BlitProof(frame.RasterFrame, frame.Resolved);
        }

        PresenterPngWriter.Write(fullOutputPath, frame.RasterFrame);

        return new PresenterExportResult(
            fullOutputPath,
            frame.RasterFrame.Width,
            frame.RasterFrame.Height,
            proofOptions,
            placement);
    }

    public static WriteableBitmap ToBitmap(RasterFrame frame)
    {
        WriteableBitmap bitmap = new(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using var locked = bitmap.Lock();
        byte[] pixelBytes = ToRgbaBytes(frame);
        System.Runtime.InteropServices.Marshal.Copy(pixelBytes, 0, locked.Address, pixelBytes.Length);

        return bitmap;
    }

    private static byte[] ToRgbaBytes(RasterFrame frame)
    {
        int width = frame.Surface.Width;
        int height = frame.Surface.Height;
        byte[] bytes = new byte[width * height * 4];
        int index = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
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
}

public sealed record PresenterExportResult(
    string OutputPath,
    int Width,
    int Height,
    PresenterProofOptions ProofOptions,
    PresenterDirectOutlineRenderBridgeProofPlacement? DirectOutlineRenderBridgeProofPlacement);
