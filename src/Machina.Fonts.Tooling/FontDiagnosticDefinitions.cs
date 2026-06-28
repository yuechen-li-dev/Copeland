using Machina.Fonts;
using Machina.Fonts.Generation;
using Machina.Fonts.ReferenceRendering;

namespace Machina.Fonts.Tooling;

public sealed record FontDiagnosticTextDefinition(string Id, string Text)
{
    public string GetPresetArtifactFileName(string presetName)
    {
        return $"m9b-{presetName}-{Id}.png";
    }

    public string DirectOutlinePngFileName => $"direct-outline-{Id}.png";

    public string BrowserPngFileName => $"browser-{Id}.png";

    public string MsdfPpmFileName => $"msdf-{Id}.ppm";

    public string MsdfPngFileName => $"msdf-{Id}.png";

    public string WireframePngFileName => $"wireframe-{Id}.png";

    public FontDiagnosticTextDefinition Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentNullException.ThrowIfNull(Text);
        return this;
    }
}

public sealed record FontDiagnosticCanvasDefinition(
    int SizePx,
    int Width,
    int Height,
    double OriginX,
    double BaselineY)
{
    public string SizeDirectoryName => SizePx.ToString();

    public FontDiagnosticCanvasDefinition Validate()
    {
        if (SizePx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SizePx));
        }

        if (Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width));
        }

        if (Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Height));
        }

        return this;
    }
}

public sealed record FontDiagnosticExportOptions
{
    public required string OutputDirectory { get; init; }

    public string? RepositoryRootDirectory { get; init; }

    public required string AtlasName { get; init; }

    public required string FontPath { get; init; }

    public required string FontFamilyName { get; init; }

    public required string FontStyleName { get; init; }

    public required string LicenseIdentifier { get; init; }

    public required FontFaceId Face { get; init; }

    public IReadOnlyList<FontDiagnosticTextDefinition> TextDefinitions { get; init; } = Array.Empty<FontDiagnosticTextDefinition>();

    public IReadOnlyList<FontDiagnosticCanvasDefinition> CanvasDefinitions { get; init; } = Array.Empty<FontDiagnosticCanvasDefinition>();

    public MachinaFontWeight Weight { get; init; } = MachinaFontWeight.Regular;

    public MachinaFontSlant Slant { get; init; } = MachinaFontSlant.Upright;

    public DistanceFieldKind Kind { get; init; } = DistanceFieldKind.Msdf;

    public int FieldWidth { get; init; } = 32;

    public int FieldHeight { get; init; } = 32;

    public double PixelRange { get; init; } = 4d;

    public int PageWidth { get; init; } = 256;

    public int PageHeight { get; init; } = 256;

    public int PagePadding { get; init; } = 2;

    public bool FlipY { get; init; } = true;

    public string EdgeColoring { get; init; } = "simple";

    public double MiterLimit { get; init; } = 2d;

    public Rgba32 Foreground { get; init; } = new(240, 240, 240, 255);

    public Rgba32 Background { get; init; } = new(16, 16, 24, 255);

    public FontDiagnosticGridOptions GridOptions { get; init; } = new();

    public FontDiagnosticBoundsOverlayOptions BoundsOptions { get; init; } = new();

    public IReadOnlyList<string> PresetNames { get; init; } =
    [
        "direct-vs-msdf",
        "cad-debug",
    ];

    public bool CleanOutputDirectory { get; init; }

    public bool AllowPartial { get; init; }

    public bool IncludeTimestamp { get; init; }

    public FontDiagnosticExportOptions Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(AtlasName);
        ArgumentException.ThrowIfNullOrWhiteSpace(FontPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(FontFamilyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(FontStyleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(LicenseIdentifier);

        if (RepositoryRootDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRootDirectory);
        }

        if (!File.Exists(FontPath))
        {
            throw new FileNotFoundException("Font fixture was not found.", FontPath);
        }

        if (TextDefinitions.Count == 0)
        {
            throw new ArgumentException("At least one text definition is required.", nameof(TextDefinitions));
        }

        if (CanvasDefinitions.Count == 0)
        {
            throw new ArgumentException("At least one canvas definition is required.", nameof(CanvasDefinitions));
        }

        if (PresetNames.Count == 0)
        {
            throw new ArgumentException("At least one preset name is required.", nameof(PresetNames));
        }

        _ = GridOptions.Validate();
        foreach (FontDiagnosticTextDefinition definition in TextDefinitions)
        {
            definition.Validate();
        }

        foreach (FontDiagnosticCanvasDefinition canvas in CanvasDefinitions)
        {
            canvas.Validate();
        }

        foreach (string presetName in PresetNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(presetName);
            _ = LayerPresets.GetPreset(presetName);
        }

        return this;
    }
}
