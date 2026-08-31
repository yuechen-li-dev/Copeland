namespace Machina.Presenter.Sample;

public sealed record PresenterProgramOptions(
    bool ExportOnly,
    string OutputPath,
    string OutputDirectory,
    PresenterProofOptions ProofOptions,
    PresenterNavigationExportOptions NavigationOptions,
    string? PlaybackScenarioPath,
    string? PlaybackSuitePath)
{
    public static PresenterProgramOptions Parse(IReadOnlyList<string> args)
    {
        bool exportOnly = false;
        string outputPath = PresenterExportContract.DefaultOutputPath;
        string outputDirectory = Path.Combine("artifacts", "m16c", "playback");
        bool includeDirectOutlineRenderBridgeProof = false;
        bool includeNavigationShell = true;
        string? oblivionWorkspacePath = null;
        string? oblivionPresentationId = null;
        string? selectedSectionId = null;
        string? selectedTabId = null;
        string? selectedNavigationPageId = null;
        string? selectedCardId = null;
        string? expandedCardId = null;
        double? expandedCardBodyScroll = null;
        double? inspectorScroll = null;
        double? inspectorRawSourceScroll = null;
        OblivionCompactPane? compactPane = null;
        PresenterShellMode? shellMode = null;
        int width = 1120;
        int height = 760;
        string? invokeActionId = null;
        Dictionary<string, double>? scrollOffsetByPageId = null;
        bool runtimeSizeExplicit = false;
        string? playbackScenarioPath = null;
        string? playbackSuitePath = null;

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];

            if (arg == "--export-only")
            {
                exportOnly = true;
                continue;
            }

            if (arg == "--output-path" && index + 1 < args.Count)
            {
                outputPath = args[++index];
                continue;
            }

            if (arg == "--output-directory" && index + 1 < args.Count)
            {
                outputDirectory = args[++index];
                continue;
            }

            if (arg == "--include-direct-outline-render-bridge-proof")
            {
                includeDirectOutlineRenderBridgeProof = true;
                continue;
            }

            if (arg == "--include-navigation-shell")
            {
                includeNavigationShell = true;
                continue;
            }

            if (arg == "--oblivion-workspace" && index + 1 < args.Count)
            {
                oblivionWorkspacePath = args[++index];
                continue;
            }

            if (arg == "--oblivion-presentation" && index + 1 < args.Count)
            {
                oblivionPresentationId = args[++index];
                continue;
            }

            if (arg == "--legacy-single-card")
            {
                includeNavigationShell = false;
                continue;
            }

            if (arg == "--navigation-page" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                selectedNavigationPageId = args[++index];
                continue;
            }

            if (arg == "--selected-section" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                selectedSectionId = args[++index];
                continue;
            }

            if (arg == "--selected-tab" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                selectedTabId = args[++index];
                continue;
            }

            if (arg == "--selected-card" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                selectedCardId = args[++index];
                continue;
            }

            if (arg == "--expanded-card" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                expandedCardId = args[++index];
                continue;
            }

            if (arg == "--expanded-card-body-scroll" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                expandedCardBodyScroll = double.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            if (arg == "--inspector-scroll" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                inspectorScroll = double.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            if (arg == "--inspector-raw-source-scroll" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                inspectorRawSourceScroll = double.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            if (arg == "--compact-pane" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                compactPane = Enum.Parse<OblivionCompactPane>(args[++index], ignoreCase: true);
                continue;
            }

            if (arg == "--shell-mode" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                shellMode = Enum.Parse<PresenterShellMode>(args[++index], ignoreCase: true);
                continue;
            }

            if (arg == "--width" && index + 1 < args.Count)
            {
                width = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                runtimeSizeExplicit = true;
                continue;
            }

            if (arg == "--height" && index + 1 < args.Count)
            {
                height = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                runtimeSizeExplicit = true;
                continue;
            }

            if (arg == "--invoke-action" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                invokeActionId = args[++index];
                continue;
            }

            if (arg == "--scroll-page" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                string payload = args[++index];
                int separator = payload.LastIndexOf(':');
                if (separator <= 0)
                {
                    throw new ArgumentException($"Expected page scroll value in the form '<pageId>:<offset>', but got '{payload}'.", nameof(args));
                }

                string pageId = payload[..separator];
                string offsetText = payload[(separator + 1)..];
                if (!double.TryParse(offsetText, System.Globalization.CultureInfo.InvariantCulture, out double offset))
                {
                    throw new ArgumentException($"Expected numeric page scroll offset in '{payload}'.", nameof(args));
                }

                scrollOffsetByPageId ??= new Dictionary<string, double>(StringComparer.Ordinal);
                scrollOffsetByPageId[pageId] = offset;
                continue;
            }

            if (arg == "--playback-scenario" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                exportOnly = true;
                playbackScenarioPath = args[++index];
                continue;
            }

            if (arg == "--playback-suite" && index + 1 < args.Count)
            {
                includeNavigationShell = true;
                exportOnly = true;
                playbackSuitePath = args[++index];
            }
        }

        return new PresenterProgramOptions(
            exportOnly,
            outputPath,
            outputDirectory,
            new PresenterProofOptions(
                includeDirectOutlineRenderBridgeProof,
                oblivionWorkspacePath,
                oblivionPresentationId),
            new PresenterNavigationExportOptions(
                includeNavigationShell,
                selectedSectionId,
                selectedTabId,
                selectedNavigationPageId,
                selectedCardId,
                expandedCardId,
                expandedCardBodyScroll,
                inspectorScroll,
                inspectorRawSourceScroll,
                compactPane,
                shellMode,
                width,
                height,
                invokeActionId,
                scrollOffsetByPageId,
                includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null,
                runtimeSizeExplicit),
            playbackScenarioPath,
            playbackSuitePath);
    }
}
