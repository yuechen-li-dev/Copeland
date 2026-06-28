using Machina.Fonts.Generation;
using Machina.Fonts.Tests.Artifacts.DistanceField;
using Xunit;

namespace Machina.Fonts.Tests.Generation.Packing;

public sealed class GeneratedFieldAtlasPackerTests
{
    [Fact]
    public void GeneratedFieldAtlasPacker_PacksSingleGlyph()
    {
        GeneratedGlyphDistanceField field = DistanceFieldArtifactTestHelpers.CreateField('A', 4, 5);

        GeneratedFieldAtlasPackResult result = DistanceFieldArtifactTestHelpers.Pack([field], 16, 16, 1);

        Assert.True(result.Success);
        Assert.Single(result.Pages);
        Assert.Single(result.Snapshot.Glyphs);
        GlyphAtlasEntry entry = result.Snapshot.Glyphs[field.Key];
        Assert.Equal(0, entry.PageIndex);
        Assert.Equal(0, entry.X);
        Assert.Equal(0, entry.Y);
        Assert.Equal(4, entry.Width);
        Assert.Equal(5, entry.Height);
    }

    [Fact]
    public void GeneratedFieldAtlasPacker_PacksDeterministically()
    {
        GeneratedGlyphDistanceField[] fields =
        [
            DistanceFieldArtifactTestHelpers.CreateField('C', 5, 6, seed: 3),
            DistanceFieldArtifactTestHelpers.CreateField('A', 4, 6, seed: 1),
            DistanceFieldArtifactTestHelpers.CreateField('B', 4, 4, seed: 2),
        ];

        GeneratedFieldAtlasPackResult first = DistanceFieldArtifactTestHelpers.Pack(fields, 32, 32, 1);
        GeneratedFieldAtlasPackResult second = DistanceFieldArtifactTestHelpers.Pack(fields.Reverse().ToArray(), 32, 32, 1);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Snapshot.Glyphs, second.Snapshot.Glyphs);
        Assert.Equal(first.Pages[0].Data, second.Pages[0].Data);
    }

    [Fact]
    public void GeneratedFieldAtlasPacker_GrowsPagesWhenNeeded()
    {
        GeneratedGlyphDistanceField[] fields =
        [
            DistanceFieldArtifactTestHelpers.CreateField('A', 8, 8),
            DistanceFieldArtifactTestHelpers.CreateField('B', 8, 8),
            DistanceFieldArtifactTestHelpers.CreateField('C', 8, 8),
        ];

        GeneratedFieldAtlasPackResult result = DistanceFieldArtifactTestHelpers.Pack(fields, 16, 16, 1);

        Assert.True(result.Success);
        Assert.Equal(3, result.Pages.Count);
        Assert.All(result.Snapshot.Glyphs.Values, entry => Assert.Equal(entry.Key.Codepoint - 'A', entry.PageIndex));
    }

    [Fact]
    public void GeneratedFieldAtlasPacker_RejectsMixedChannelCounts()
    {
        GeneratedGlyphDistanceField msdf = DistanceFieldArtifactTestHelpers.CreateField('A', 4, 4, DistanceFieldKind.Msdf);
        GeneratedGlyphDistanceField mtsdf = DistanceFieldArtifactTestHelpers.CreateField('B', 4, 4, DistanceFieldKind.Mtsdf);

        GeneratedFieldAtlasPackResult result = DistanceFieldArtifactTestHelpers.Pack([msdf, mtsdf], 16, 16, 1);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.AtlasPackingFailed);
    }

    [Fact]
    public void GeneratedFieldAtlasPacker_RejectsGlyphTooLargeForPage()
    {
        GeneratedGlyphDistanceField field = DistanceFieldArtifactTestHelpers.CreateField('A', 20, 4);

        GeneratedFieldAtlasPackResult result = DistanceFieldArtifactTestHelpers.Pack([field], 16, 16, 1);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == FontGenerationDiagnosticCode.AtlasPackingFailed);
    }

    [Fact]
    public void GeneratedFieldAtlasPacker_ComputesUvsFromPageDimensions()
    {
        GeneratedGlyphDistanceField first = DistanceFieldArtifactTestHelpers.CreateField('A', 4, 4, seed: 1);
        GeneratedGlyphDistanceField second = DistanceFieldArtifactTestHelpers.CreateField('B', 4, 4, seed: 2);

        GeneratedFieldAtlasPackResult result = DistanceFieldArtifactTestHelpers.Pack([first, second], 16, 16, 2);

        GlyphAtlasEntry entry = result.Snapshot.Glyphs[second.Key];
        Assert.Equal(6d / 16d, entry.U0);
        Assert.Equal(0d, entry.V0);
        Assert.Equal(10d / 16d, entry.U1);
        Assert.Equal(4d / 16d, entry.V1);
    }

    [Fact]
    public void GeneratedFieldAtlasPacker_CopiesFieldDataIntoPage()
    {
        GeneratedGlyphDistanceField field = DistanceFieldArtifactTestHelpers.CreateField('A', 2, 2, seed: 10);

        GeneratedFieldAtlasPackResult result = DistanceFieldArtifactTestHelpers.Pack([field], 8, 8, 1);

        GeneratedFieldAtlasPage page = result.Pages[0];
        Assert.Equal(10f, page.Data[0]);
        Assert.Equal(10.125f, page.Data[1]);
        Assert.Equal(10.25f, page.Data[2]);

        int secondRowOffset = 8 * 3;
        Assert.Equal(10.75f, page.Data[secondRowOffset]);
        Assert.Equal(10.875f, page.Data[secondRowOffset + 1]);
        Assert.Equal(11f, page.Data[secondRowOffset + 2]);
    }

    [Fact]
    public void GeneratedFieldAtlasPacker_SortsByHeightWidthThenGlyphKey()
    {
        GeneratedGlyphDistanceField[] fields =
        [
            DistanceFieldArtifactTestHelpers.CreateField('B', 4, 6, face: "b-face", emSize: 18),
            DistanceFieldArtifactTestHelpers.CreateField('A', 5, 6, face: "a-face", emSize: 16),
            DistanceFieldArtifactTestHelpers.CreateField('C', 4, 6, face: "a-face", emSize: 14),
            DistanceFieldArtifactTestHelpers.CreateField('D', 4, 6, face: "a-face", emSize: 16, weight: MachinaFontWeight.Bold),
        ];

        GeneratedFieldAtlasPackResult result = DistanceFieldArtifactTestHelpers.Pack(fields, 32, 16, 1);

        GlyphAtlasEntry[] orderedEntries = result.Snapshot.Glyphs.Values.OrderBy(entry => entry.X).ToArray();
        Assert.Equal(['A', 'C', 'D', 'B'], orderedEntries.Select(entry => (char)entry.Key.Codepoint).ToArray());
    }
}
