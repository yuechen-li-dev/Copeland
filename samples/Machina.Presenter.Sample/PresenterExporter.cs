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
        return Export(DemoState.Default, options.OutputPath, options.ProofOptions, options.NavigationOptions, null);
    }

    public static PresenterExportResult Export(
        DemoState state,
        string outputPath,
        PresenterProofOptions proofOptions,
        StandardTheme? theme = null)
    {
        return Export(state, outputPath, proofOptions, PresenterNavigationExportOptions.DefaultShell, theme);
    }

    public static PresenterExportResult Export(
        DemoState state,
        string outputPath,
        PresenterProofOptions proofOptions,
        PresenterNavigationExportOptions navigationOptions,
        StandardTheme? theme = null)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        StandardTheme effectiveTheme = theme ?? Program.AppTheme;
        PresenterDirectOutlineRenderBridgeProofPlacement? placement = null;
        string? selectedSectionId = null;
        string? selectedTabId = null;
        string? selectedPageId = null;
        ScrollbarGeometry? scrollbarGeometry = null;
        PresenterNavigationRenderDiagnostics? navigationDiagnostics = null;
        string? manifestJsonPath = null;
        string? manifestTextPath = null;
        string? oblivionManifestJsonPath = null;
        string? oblivionManifestTextPath = null;
        string? oblivionInspectorManifestJsonPath = null;
        string? oblivionInspectorManifestTextPath = null;
        string? oblivionPhaseCloseoutManifestJsonPath = null;
        string? oblivionPhaseCloseoutManifestTextPath = null;
        RasterFrame rasterFrame;
        int width;
        int height;

        if (navigationOptions.IncludeNavigationShell)
        {
            PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
            PresenterNavigationState navigationState = PresenterNavigationCatalog.CreateState(model, proofOptions, navigationOptions);

            PresenterNavigationShellRenderResult shellRender = PresenterNavigationShellRenderer.Render(
                state,
                navigationState,
                effectiveTheme,
                proofOptions);

            rasterFrame = shellRender.ComposedFrame;
            width = shellRender.ComposedFrame.Width;
            height = shellRender.ComposedFrame.Height;
            selectedSectionId = shellRender.SelectedSection.Id;
            selectedTabId = shellRender.SelectedTab.Id;
            selectedPageId = shellRender.SelectedTab.PageId;
            scrollbarGeometry = shellRender.ScrollbarGeometry;
            navigationDiagnostics = shellRender.Diagnostics;

            string outputDirectory = Path.GetDirectoryName(fullOutputPath) ?? Path.GetFullPath(".");
            (manifestJsonPath, manifestTextPath) = PresenterNavigationManifestWriter.Write(
                outputDirectory,
                shellRender,
                proofOptions,
                navigationOptions.InteractionBackendName);
            (oblivionManifestJsonPath, oblivionManifestTextPath) = OblivionWorkbenchCatalog.WriteManifest(outputDirectory, proofOptions);
            (oblivionInspectorManifestJsonPath, oblivionInspectorManifestTextPath) = OblivionWorkbenchCatalog.WriteInspectorManifest(
                outputDirectory,
                shellRender.NavigationState,
                proofOptions);
            (oblivionPhaseCloseoutManifestJsonPath, oblivionPhaseCloseoutManifestTextPath) =
                OblivionWorkbenchCatalog.WritePhaseCloseoutManifest(outputDirectory);
        }
        else
        {
            var pipeline = new MachinaRasterPipeline();
            var document = SettingsScreen.Build(state, effectiveTheme, proofOptions);
            var frame = pipeline.Render(
                document,
                SettingsScreen.GetWidth(proofOptions),
                SettingsScreen.GetHeight(proofOptions));

            if (proofOptions.IncludeDirectOutlineRenderBridgeProof)
            {
                placement = PresenterDirectOutlineRenderBridgeProofRenderer.BlitProof(frame.RasterFrame, frame.Resolved);
            }

            rasterFrame = frame.RasterFrame;
            width = frame.RasterFrame.Width;
            height = frame.RasterFrame.Height;
        }

        PresenterPngWriter.Write(fullOutputPath, rasterFrame);

        return new PresenterExportResult(
            fullOutputPath,
            width,
            height,
            proofOptions,
            placement)
        {
            IncludesNavigationShell = navigationOptions.IncludeNavigationShell,
            NavigationSectionId = selectedSectionId,
            NavigationTabId = selectedTabId,
            NavigationPageId = selectedPageId,
            ScrollbarGeometry = scrollbarGeometry,
            NavigationDiagnostics = navigationDiagnostics,
            NavigationManifestJsonPath = manifestJsonPath,
            NavigationManifestTextPath = manifestTextPath,
            OblivionManifestJsonPath = oblivionManifestJsonPath,
            OblivionManifestTextPath = oblivionManifestTextPath,
            OblivionInspectorManifestJsonPath = oblivionInspectorManifestJsonPath,
            OblivionInspectorManifestTextPath = oblivionInspectorManifestTextPath,
            OblivionPhaseCloseoutManifestJsonPath = oblivionPhaseCloseoutManifestJsonPath,
            OblivionPhaseCloseoutManifestTextPath = oblivionPhaseCloseoutManifestTextPath,
        };
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
    PresenterDirectOutlineRenderBridgeProofPlacement? DirectOutlineRenderBridgeProofPlacement)
{
    public bool IncludesNavigationShell { get; init; }

    public string? NavigationSectionId { get; init; }

    public string? NavigationTabId { get; init; }

    public string? NavigationPageId { get; init; }

    public ScrollbarGeometry? ScrollbarGeometry { get; init; }

    public PresenterNavigationRenderDiagnostics? NavigationDiagnostics { get; init; }

    public string? NavigationManifestJsonPath { get; init; }

    public string? NavigationManifestTextPath { get; init; }

    public string? OblivionManifestJsonPath { get; init; }

    public string? OblivionManifestTextPath { get; init; }

    public string? OblivionInspectorManifestJsonPath { get; init; }

    public string? OblivionInspectorManifestTextPath { get; init; }

    public string? OblivionPhaseCloseoutManifestJsonPath { get; init; }

    public string? OblivionPhaseCloseoutManifestTextPath { get; init; }
}
