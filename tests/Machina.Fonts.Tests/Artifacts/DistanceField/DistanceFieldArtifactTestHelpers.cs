using Machina.Fonts.Artifacts;
using Machina.Fonts.Artifacts.DistanceField;
using Machina.Fonts.Generation;
using Machina.Fonts.Generation.MsdfSharp;
using Machina.Fonts.Tests.Generation.MsdfSharp;
using Machina.Fonts.Tests.Generation.Typography;
using Machina.Fonts.Toml;
using Xunit;

namespace Machina.Fonts.Tests.Artifacts.DistanceField;

internal static class DistanceFieldArtifactTestHelpers
{
    public static readonly FontFaceId Face = new("machina-distance-field");
    public static readonly GlyphOutlineLoadOptions OutlineOptions = new(32, 0, GlyphHintingMode.None, normalizeToEm: true);

    public static FontAtlasTomlExportMetadata Metadata(string name = "machina-df", string distanceField = "msdf")
    {
        return new FontAtlasTomlExportMetadata(
            name,
            distanceField,
            "Space Mono",
            "Regular",
            TypographyFixtureFont.FontPath,
            "sha256-space-mono",
            "OFL-1.1",
            new FontAtlasMetricsToml
            {
                EmSize = 32,
                UnitsPerEm = 1000,
                Ascent = 25.6,
                Descent = -6.4,
                LineGap = 0,
                LineHeight = 32,
            },
            new FontAtlasMsdfToml
            {
                Range = 4,
                Scale = 1,
                EdgeColoring = "simple",
                MiterLimit = 2,
            });
    }

    public static GeneratedGlyphDistanceField CreateField(
        char value,
        int width,
        int height,
        DistanceFieldKind kind = DistanceFieldKind.Msdf,
        string face = "machina-distance-field",
        double emSize = 16,
        MachinaFontWeight weight = MachinaFontWeight.Regular,
        MachinaFontSlant slant = MachinaFontSlant.Upright,
        IReadOnlyList<FontGenerationDiagnostic>? diagnostics = null,
        float seed = 1f)
    {
        int channelCount = FakeDistanceFieldValidation.GetChannelCount(kind);
        float[] data = new float[checked(width * height * channelCount)];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = seed + (i * 0.125f);
        }

        GlyphKey key = GlyphKey.FromChar(new FontFaceId(face), value, emSize, weight, slant);
        GlyphMetrics metrics = new(width + 1, 0.5, height, width, height);
        return new GeneratedGlyphDistanceField(
            key,
            metrics,
            width,
            height,
            kind,
            channelCount,
            data,
            diagnostics ?? Array.Empty<FontGenerationDiagnostic>());
    }

    public static GeneratedFieldAtlasPackResult Pack(
        IReadOnlyList<GeneratedGlyphDistanceField> fields,
        int pageWidth = 32,
        int pageHeight = 32,
        int padding = 1,
        string pageNamePrefix = "atlas")
    {
        GeneratedFieldAtlasPacker packer = new();
        return packer.Pack(fields, new GeneratedFieldAtlasPackOptions(pageWidth, pageHeight, padding, pageNamePrefix));
    }

    public static FontAtlasArtifactExportResult Export(
        GeneratedFieldAtlasPackResult packResult,
        string directory,
        string atlasName = "machina-df",
        string distanceField = "msdf")
    {
        return DistanceFieldAtlasArtifactExporter.Export(packResult, Metadata(atlasName, distanceField), directory, atlasName);
    }

    public static void RewriteTomlHash(FontAtlasArtifactExportResult export, string pagePath)
    {
        string hash = DistanceFieldPageArtifactWriter.ComputeFileSha256(pagePath);
        FontAtlasTomlLoadResult loaded = FontAtlasTomlLoader.LoadFile(export.TomlPath);
        FontAtlasTomlDocument document = loaded.Document! with
        {
            Pages = loaded.Document.Pages
                .Select(page => page.Image == Path.GetFileName(pagePath) ? page with { ContentHash = hash } : page)
                .ToArray(),
        };

        File.WriteAllText(export.TomlPath, FontAtlasTomlWriter.Write(document));
    }

    public static async Task<GeneratedFieldAtlasPackResult> RunTypographyMsdfPackAsync(params int[] codepoints)
    {
        GlyphGenerationPipeline pipeline = new(
            TypographyFixtureFont.CreateSource(),
            new MsdfSharpDistanceFieldGenerator());

        List<GeneratedGlyphDistanceField> fields = [];
        foreach (int codepoint in codepoints)
        {
            GlyphGenerationResult result = await pipeline.GenerateAsync(
                GlyphKey.FromCodepoint(TypographyFixtureFont.Face, codepoint, 32),
                OutlineOptions,
                MsdfSharpTestHelpers.CreateSettings(DistanceFieldKind.Msdf));

            Assert.NotNull(result.DistanceField);
            fields.Add(result.DistanceField!);
        }

        return Pack(fields, 96, 96, 2, "space-mono");
    }

    public static FontAtlasSnapshot NormalizeSnapshot(FontAtlasSnapshot original, FontAtlasSnapshot imported)
    {
        FontAtlasPage[] normalizedPages = original.Pages
            .Select(page =>
            {
                FontAtlasPage importedPage = imported.Pages.Single(candidate => candidate.Index == page.Index);
                return new FontAtlasPage(page.Index, importedPage.ImagePath, page.Width, page.Height, importedPage.ContentHash);
            })
            .ToArray();

        return new FontAtlasSnapshot(original.Version, normalizedPages, original.Glyphs);
    }
}
