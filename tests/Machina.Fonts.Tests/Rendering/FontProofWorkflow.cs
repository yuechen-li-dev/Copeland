using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Generation.Typography;
using Machina.Fonts.ReferenceRendering;
using Machina.Fonts.Tests.Artifacts.DistanceField;
using Machina.Fonts.Tests.Generation.Typography;

namespace Machina.Fonts.Tests.Rendering;

internal static class FontProofWorkflow
{
    public const string OutputDirectoryEnvironmentVariable = "MACHINA_FONT_PROOF_OUTPUT_DIR";

    private static readonly Rgba32 Background = new(16, 16, 24, 255);

    public static IReadOnlyList<FontProofArtifactDefinition> Definitions { get; } =
    [
        new("msdf-machina.ppm", "Machina"),
        new("msdf-aa0.ppm", "Aa0"),
        new("msdf-a-space-a.ppm", "A A"),
        new("msdf-machina-0.ppm", "Machina 0"),
        new("msdf-hello-machina.ppm", "Hello Machina"),
    ];

    public static FontProofExportOptions CreateOptions(string outputDirectory)
    {
        return new FontProofExportOptions(
            outputDirectory,
            "space-mono-msdf-proofs",
            TypographyFixtureFont.Face,
            32,
            MachinaFontWeight.Regular,
            MachinaFontSlant.Upright,
            Machina.Fonts.Generation.DistanceFieldKind.Msdf,
            320,
            64,
            32,
            32,
            4d,
            new Rgba32(240, 240, 240, 255),
            Background,
            8d,
            40d,
            FlipY: true,
            PageWidth: 128,
            PageHeight: 128,
            PagePadding: 2);
    }

    public static async Task<FontProofExportResult> ExportAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        FontProofExporter exporter = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator(),
            DistanceFieldArtifactTestHelpers.Metadata("space-mono-msdf-proofs", "msdf"));

        return await exporter.ExportAsync(Definitions, CreateOptions(outputDirectory), cancellationToken);
    }

    public static string GetRequestedOutputDirectoryOrCreateTemp()
    {
        string? requested = Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Path.GetFullPath(requested);
        }

        return Path.Combine(Path.GetTempPath(), "machina-fonts-m8l", Guid.NewGuid().ToString("N"));
    }

    public static Rgba32 BackgroundColor => Background;
}
