namespace Machina.ComponentGallery.Sample;

public sealed record GalleryProgramOptions(
    GalleryState InitialState,
    bool ExportOnly,
    string ExportDirectory,
    string ExportName,
    bool IncludeDirectOutlineTextProof,
    bool IncludeMsdfFontProof)
{
    public static GalleryProgramOptions Parse(IReadOnlyList<string> args)
    {
        var state = GalleryState.Default;
        var exportOnly = false;
        var exportDirectory = GalleryExportContract.DefaultOutputDirectory;
        var exportName = GalleryExportContract.DefaultExportName;
        var includeDirectOutlineTextProof = false;
        var includeMsdfFontProof = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];

            if (arg == "--export-only")
            {
                exportOnly = true;
                continue;
            }

            if (arg == "--export-dir" && index + 1 < args.Count)
            {
                exportDirectory = args[++index];
                continue;
            }

            if (arg == "--export-name" && index + 1 < args.Count)
            {
                exportName = args[++index];
                continue;
            }

            if (arg == "--include-msdf-font-proof")
            {
                includeMsdfFontProof = true;
                continue;
            }

            if (arg == "--include-direct-outline-text-proof")
            {
                includeDirectOutlineTextProof = true;
                continue;
            }

            if (arg == "--primary-clicks" && index + 1 < args.Count && int.TryParse(args[++index], out var clickCount))
            {
                state = state with { PrimaryClicks = clickCount };
                continue;
            }

            if (arg == "--checkbox" && index + 1 < args.Count)
            {
                state = state with { LiveCheckboxChecked = ParseOnOff(args[++index]) };
                continue;
            }

            if (arg == "--switch" && index + 1 < args.Count)
            {
                state = state with { LiveSwitchOn = ParseOnOff(args[++index]) };
                continue;
            }
        }

        return new GalleryProgramOptions(
            state,
            exportOnly,
            exportDirectory,
            exportName,
            includeDirectOutlineTextProof,
            includeMsdfFontProof);
    }

    private static bool ParseOnOff(string value)
    {
        return value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
