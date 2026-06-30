namespace Machina.Presenter.Sample;

public sealed record PresenterProgramOptions(
    bool ExportOnly,
    string OutputPath,
    PresenterProofOptions ProofOptions,
    PresenterNavigationExportOptions NavigationOptions)
{
    public static PresenterProgramOptions Parse(IReadOnlyList<string> args)
    {
        bool exportOnly = false;
        string outputPath = PresenterExportContract.DefaultOutputPath;
        bool includeDirectOutlineRenderBridgeProof = false;
        bool includeNavigationShell = true;
        string? oblivionWorkspacePath = null;
        string? selectedSectionId = null;
        string? selectedTabId = null;
        string? selectedNavigationPageId = null;
        string? selectedCardId = null;
        string? invokeActionId = null;
        Dictionary<string, double>? scrollOffsetByPageId = null;

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
            }
        }

        return new PresenterProgramOptions(
            exportOnly,
            outputPath,
            new PresenterProofOptions(includeDirectOutlineRenderBridgeProof, oblivionWorkspacePath),
            new PresenterNavigationExportOptions(
                includeNavigationShell,
                selectedSectionId,
                selectedTabId,
                selectedNavigationPageId,
                selectedCardId,
                invokeActionId,
                scrollOffsetByPageId,
                includeNavigationShell ? AvaloniaPresenterInputBackend.BackendName : null));
    }
}
