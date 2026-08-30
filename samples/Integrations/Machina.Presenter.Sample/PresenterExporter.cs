using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Machina.Pipeline;
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
        string? oblivionExpandableMarkdownCardsManifestJsonPath = null;
        string? oblivionExpandableMarkdownCardsManifestTextPath = null;
        string? oblivionExpandedMarkdownReadingSurfaceManifestJsonPath = null;
        string? oblivionExpandedMarkdownReadingSurfaceManifestTextPath = null;
        string? oblivionIndependentScrollPanesManifestJsonPath = null;
        string? oblivionIndependentScrollPanesManifestTextPath = null;
        string? oblivionScrollRegressionStabilizationManifestJsonPath = null;
        string? oblivionScrollRegressionStabilizationManifestTextPath = null;
        string? oblivionPhaseCloseoutManifestJsonPath = null;
        string? oblivionPhaseCloseoutManifestTextPath = null;
        string? oblivionMarkdownRenderingManifestJsonPath = null;
        string? oblivionMarkdownRenderingManifestTextPath = null;
        string? oblivionDocsDogfoodManifestJsonPath = null;
        string? oblivionDocsDogfoodManifestTextPath = null;
        string? oblivionAgenticCardContractManifestJsonPath = null;
        string? oblivionAgenticCardContractManifestTextPath = null;
        string? oblivionEffectRoutingManifestJsonPath = null;
        string? oblivionEffectRoutingManifestTextPath = null;
        string? keyboardManifestJsonPath = null;
        string? keyboardManifestTextPath = null;
        string? adaptiveShellManifestJsonPath = null;
        string? adaptiveShellManifestTextPath = null;
        RasterFrame rasterFrame;
        int width;
        int height;

        if (navigationOptions.IncludeNavigationShell)
        {
            PresenterNavigationModel model = PresenterNavigationCatalog.CreateModel();
            PresenterShellMode shellMode = navigationOptions.ShellMode
                ?? PresenterShellModeResolver.Resolve(navigationOptions.Width);
            PresenterNavigationLayout layout = PresenterNavigationLayout.Create(
                navigationOptions.Width,
                navigationOptions.Height,
                shellMode);
            PresenterNavigationState navigationState = PresenterNavigationCatalog.CreateState(model, proofOptions, navigationOptions);
            if (!string.IsNullOrWhiteSpace(navigationOptions.InvokeActionId))
            {
                string pageId = navigationOptions.SelectedPageId
                    ?? model.FindTab(
                        navigationState.SelectedSectionId,
                        navigationState.GetSelectedTabId(navigationState.SelectedSectionId, model))?.PageId
                    ?? throw new InvalidOperationException("Could not resolve presenter page for action invocation.");
                if (string.IsNullOrWhiteSpace(navigationOptions.SelectedCardId))
                {
                    throw new InvalidOperationException("Action invocation requires a selected Oblivion card.");
                }

                string resolvedCardId = OblivionWorkbench.ResolveCardSelectionId(
                    pageId,
                    navigationOptions.SelectedCardId,
                    proofOptions);
                navigationState = PresenterNavigationDispatch.Dispatch(
                    navigationState,
                    OblivionUiActions.InvokeProductAction(
                        pageId,
                        resolvedCardId,
                        navigationOptions.InvokeActionId),
                    model,
                    proofOptions,
                    layout);
            }

            PresenterNavigationShellRenderResult shellRender = PresenterNavigationShellRenderer.Render(
                state,
                navigationState,
                effectiveTheme,
                proofOptions,
                layout);

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
            (oblivionManifestJsonPath, oblivionManifestTextPath) = OblivionWorkbench.WriteManifest(outputDirectory, proofOptions);
            (oblivionInspectorManifestJsonPath, oblivionInspectorManifestTextPath) = OblivionWorkbench.WriteInspectorManifest(
                outputDirectory,
                shellRender.NavigationState,
                proofOptions);
            (oblivionExpandableMarkdownCardsManifestJsonPath, oblivionExpandableMarkdownCardsManifestTextPath) =
                OblivionWorkbench.WriteExpandableMarkdownCardsManifest(
                    outputDirectory,
                    shellRender.NavigationState,
                    proofOptions);
            (oblivionExpandedMarkdownReadingSurfaceManifestJsonPath, oblivionExpandedMarkdownReadingSurfaceManifestTextPath) =
                OblivionWorkbench.WriteExpandedMarkdownReadingSurfaceManifest(
                    outputDirectory,
                    shellRender.NavigationState,
                    proofOptions);
            (oblivionIndependentScrollPanesManifestJsonPath, oblivionIndependentScrollPanesManifestTextPath) =
                OblivionWorkbench.WriteIndependentScrollPanesManifest(
                    outputDirectory,
                    shellRender.NavigationState,
                    proofOptions);
            (oblivionScrollRegressionStabilizationManifestJsonPath, oblivionScrollRegressionStabilizationManifestTextPath) =
                OblivionWorkbench.WriteScrollRegressionStabilizationManifest(
                    outputDirectory,
                    shellRender.NavigationState,
                    inspectorLagFixed: true,
                    inspectorLagRootCauseDocumented: true,
                    inspectorLagBlockerDocumented: false);
            (oblivionPhaseCloseoutManifestJsonPath, oblivionPhaseCloseoutManifestTextPath) =
                OblivionWorkbench.WritePhaseCloseoutManifest(outputDirectory);
            (oblivionMarkdownRenderingManifestJsonPath, oblivionMarkdownRenderingManifestTextPath) =
                OblivionWorkbench.WriteMarkdownRenderingManifest(outputDirectory, proofOptions);
            (oblivionDocsDogfoodManifestJsonPath, oblivionDocsDogfoodManifestTextPath) =
                OblivionWorkbench.WriteDocsDogfoodManifest(outputDirectory, proofOptions);
            (oblivionAgenticCardContractManifestJsonPath, oblivionAgenticCardContractManifestTextPath) =
                OblivionWorkbench.WriteAgenticCardContractManifest(outputDirectory, proofOptions);
            (oblivionEffectRoutingManifestJsonPath, oblivionEffectRoutingManifestTextPath) =
                OblivionWorkbench.WriteEffectRoutingManifest(
                    outputDirectory,
                    shellRender.NavigationState,
                    proofOptions);
            (keyboardManifestJsonPath, keyboardManifestTextPath) =
                PresenterKeyboardManifestWriter.Write(
                    outputDirectory,
                    shellRender,
                    navigationOptions.InteractionBackendName);
            (adaptiveShellManifestJsonPath, adaptiveShellManifestTextPath) =
                PresenterAdaptiveShellManifestWriter.Write(
                    outputDirectory,
                    shellRender);
        }
        else
        {
            var document = SettingsScreen.Build(state, effectiveTheme, proofOptions);
            var frame = MachinaAurelianCpuRasterComposition.Render(
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
            OblivionExpandableMarkdownCardsManifestJsonPath = oblivionExpandableMarkdownCardsManifestJsonPath,
            OblivionExpandableMarkdownCardsManifestTextPath = oblivionExpandableMarkdownCardsManifestTextPath,
            OblivionExpandedMarkdownReadingSurfaceManifestJsonPath = oblivionExpandedMarkdownReadingSurfaceManifestJsonPath,
            OblivionExpandedMarkdownReadingSurfaceManifestTextPath = oblivionExpandedMarkdownReadingSurfaceManifestTextPath,
            OblivionIndependentScrollPanesManifestJsonPath = oblivionIndependentScrollPanesManifestJsonPath,
            OblivionIndependentScrollPanesManifestTextPath = oblivionIndependentScrollPanesManifestTextPath,
            OblivionScrollRegressionStabilizationManifestJsonPath = oblivionScrollRegressionStabilizationManifestJsonPath,
            OblivionScrollRegressionStabilizationManifestTextPath = oblivionScrollRegressionStabilizationManifestTextPath,
            OblivionPhaseCloseoutManifestJsonPath = oblivionPhaseCloseoutManifestJsonPath,
            OblivionPhaseCloseoutManifestTextPath = oblivionPhaseCloseoutManifestTextPath,
            OblivionMarkdownRenderingManifestJsonPath = oblivionMarkdownRenderingManifestJsonPath,
            OblivionMarkdownRenderingManifestTextPath = oblivionMarkdownRenderingManifestTextPath,
            OblivionDocsDogfoodManifestJsonPath = oblivionDocsDogfoodManifestJsonPath,
            OblivionDocsDogfoodManifestTextPath = oblivionDocsDogfoodManifestTextPath,
            OblivionAgenticCardContractManifestJsonPath = oblivionAgenticCardContractManifestJsonPath,
            OblivionAgenticCardContractManifestTextPath = oblivionAgenticCardContractManifestTextPath,
            OblivionEffectRoutingManifestJsonPath = oblivionEffectRoutingManifestJsonPath,
            OblivionEffectRoutingManifestTextPath = oblivionEffectRoutingManifestTextPath,
            KeyboardManifestJsonPath = keyboardManifestJsonPath,
            KeyboardManifestTextPath = keyboardManifestTextPath,
            AdaptiveShellManifestJsonPath = adaptiveShellManifestJsonPath,
            AdaptiveShellManifestTextPath = adaptiveShellManifestTextPath,
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

    public string? OblivionExpandableMarkdownCardsManifestJsonPath { get; init; }

    public string? OblivionExpandableMarkdownCardsManifestTextPath { get; init; }

    public string? OblivionExpandedMarkdownReadingSurfaceManifestJsonPath { get; init; }

    public string? OblivionExpandedMarkdownReadingSurfaceManifestTextPath { get; init; }

    public string? OblivionIndependentScrollPanesManifestJsonPath { get; init; }

    public string? OblivionIndependentScrollPanesManifestTextPath { get; init; }

    public string? OblivionScrollRegressionStabilizationManifestJsonPath { get; init; }

    public string? OblivionScrollRegressionStabilizationManifestTextPath { get; init; }

    public string? OblivionPhaseCloseoutManifestJsonPath { get; init; }

    public string? OblivionPhaseCloseoutManifestTextPath { get; init; }

    public string? OblivionMarkdownRenderingManifestJsonPath { get; init; }

    public string? OblivionMarkdownRenderingManifestTextPath { get; init; }

    public string? OblivionDocsDogfoodManifestJsonPath { get; init; }

    public string? OblivionDocsDogfoodManifestTextPath { get; init; }

    public string? OblivionAgenticCardContractManifestJsonPath { get; init; }

    public string? OblivionAgenticCardContractManifestTextPath { get; init; }

    public string? OblivionEffectRoutingManifestJsonPath { get; init; }

    public string? OblivionEffectRoutingManifestTextPath { get; init; }

    public string? KeyboardManifestJsonPath { get; init; }

    public string? KeyboardManifestTextPath { get; init; }

    public string? AdaptiveShellManifestJsonPath { get; init; }

    public string? AdaptiveShellManifestTextPath { get; init; }
}
