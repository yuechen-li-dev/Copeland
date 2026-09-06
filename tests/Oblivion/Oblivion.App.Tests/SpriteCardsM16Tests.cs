using Copeland.TS.Assets;
using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class SpriteCardsM16Tests
{
    [Fact]
    public void ConceptPathCompositionEqualityAndInvalidInputAreExplicit()
    {
        GraphicalConceptPath panel = new("panel.dialogue");
        GraphicalConceptPath center = panel.Child("top").Child("center");

        Assert.Equal(new GraphicalConceptPath("panel.dialogue.top.center"), center);
        Assert.True(center.IsDescendantOf(panel));
        Assert.Throws<ArgumentException>(() => new GraphicalConceptPath("panel..center"));
        Assert.Throws<ArgumentException>(() => new GraphicalConceptPath("panel.dialogue.$pixel"));
    }

    [Fact]
    public void ProjectionUsesAllocatorResultsAndRetainsSourceAndErasedGuides()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();

        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        SpriteCard center = projection.Cards.Single(card =>
            card.ConceptPath == new GraphicalConceptPath("panel.dialogue.top.center"));
        SpriteCard datum = projection.Cards.Single(card =>
            card.ConceptPath == new GraphicalConceptPath("guide.dialogue.datum.text-baseline"));

        Assert.Equal(291, center.Resolved!.Length);
        Assert.Equal(217, center.Resolved.Offset);
        Assert.True(center.Source.Line > 0);
        Assert.True(center.Runtime.SurvivesLowering);
        Assert.False(datum.Runtime.SurvivesLowering);
        Assert.DoesNotContain("guide.dialogue", File.ReadAllText(asset.RuntimeTomlPath));
        Assert.Equal(9, projection.Filter(GraphicalConceptKind.EdgeSegment)
            .Count(card => card.Role == "top"));
    }

    [Fact]
    public void FourBoundedEditsRecompileRefreshCardsAndRuntimeProjection()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        GraphicalConceptPath center = new("panel.dialogue.top.center");
        GraphicalConceptPath glow = new("panel.dialogue.top.glow-left");

        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        SpriteCardEditTrace weight = service.ApplyEdit(
            projection,
            center,
            SpriteCardEditProperty.FlexWeight,
            "3");
        Assert.True(weight.Applied);
        projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        Assert.Equal(343, Card(projection, center).Resolved!.Length);

        SpriteCardEditTrace sampling = service.ApplyEdit(
            projection,
            glow,
            SpriteCardEditProperty.Sampling,
            "tile");
        Assert.True(sampling.Applied);
        projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        Assert.Equal("tile", Card(projection, glow).Authored.Sampling);

        SpriteCardEditTrace minimum = service.ApplyEdit(
            projection,
            center,
            SpriteCardEditProperty.MinimumLength,
            "44");
        Assert.True(minimum.Applied);
        projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        Assert.Equal(44, Card(projection, center).Authored.MinimumLength);

        SpriteCardEditTrace region = service.ApplyEdit(
            projection,
            center,
            SpriteCardEditProperty.SourceRegion,
            "dialogue.top.glow");
        Assert.True(region.Applied);
        projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        Assert.Equal("dialogue.top.glow", Card(projection, center).Authored.RegionId);
        Assert.Contains("weight = 3", File.ReadAllText(asset.RuntimeTomlPath));
        Assert.Contains("sampling = \"tile\"", File.ReadAllText(asset.RuntimeTomlPath));
        Assert.Contains("length = 44", File.ReadAllText(asset.RuntimeTomlPath));
        Assert.Contains("region = \"dialogue.top.glow\"", File.ReadAllText(asset.RuntimeTomlPath));
    }

    [Fact]
    public void StaleProjectionIsRejectedWithoutChangingSource()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        File.AppendAllText(asset.SourcePath, "\n// external change\n");
        string before = File.ReadAllText(asset.SourcePath);

        SpriteCardEditTrace trace = service.ApplyEdit(
            projection,
            new GraphicalConceptPath("panel.dialogue.top.center"),
            SpriteCardEditProperty.FlexWeight,
            "4");

        Assert.False(trace.Applied);
        Assert.Contains(trace.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-SPRITE-CARD-STALE-SOURCE");
        Assert.Equal(before, File.ReadAllText(asset.SourcePath));
    }

    [Fact]
    public void DuplicateAuthoringConceptPathFailsCompilation()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        string source = File.ReadAllText(asset.SourcePath)
            .Replace(
                "\"blockout.dialogue.content\",",
                "\"guide.dialogue.content-safe-area\",",
                StringComparison.Ordinal);

        ObjectAssetCompilationResult result = ObjectAssetCompiler.Compile(source, asset.SourcePath);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-ASSET-0115");
    }

    [Fact]
    public void SvgViewShowsSourcePolicyResolutionFocusAndDiagnosticsFilter()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 220, 220);
        GraphicalConceptPath selected = new("panel.dialogue.top.center");

        string svg = OblivionSpriteCardRenderer.RenderSvg(
            projection,
            new OblivionSpriteCardRenderOptions(Selected: selected));
        string diagnosticSvg = OblivionSpriteCardRenderer.RenderSvg(
            projection,
            new OblivionSpriteCardRenderOptions(DiagnosticsOnly: true));

        Assert.Contains("panel.dialogue.top.center", svg);
        Assert.Contains("sampling stretch", svg);
        Assert.Contains("resolved", svg);
        Assert.Contains("source L", svg);
        Assert.Contains("guide.dialogue.datum.text-baseline", svg);
        Assert.Contains("<line", svg);
        Assert.Contains("deficit", diagnosticSvg);
    }

    [Fact]
    public void FreshStructureEditAddsSemanticClampAndRebuildsCardsWithoutTomlAuthoring()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        string source = File.ReadAllText(asset.SourcePath)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace(
            "            fixed(prefix + \".clamp-b\", clamp, 7),\n            flex(prefix + \".center\"",
            "            fixed(prefix + \".clamp-b\", clamp, 7),\n            fixed(prefix + \".clamp-decorative\", clamp, 7),\n            flex(prefix + \".center\"",
            StringComparison.Ordinal);
        File.WriteAllText(asset.SourcePath, source);
        var service = new OblivionSpriteCardService();

        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        ObjectAssetCompilationResult compilation = ObjectAssetCompiler.CompileFile(asset.SourcePath);
        ObjectAssetBuildOutputs outputs = ObjectAssetCompiler.Emit(compilation.Document!, asset.SourcePath);

        Assert.Contains(projection.Cards, card =>
            card.ConceptPath == new GraphicalConceptPath("panel.dialogue.top.clamp-decorative"));
        Assert.Contains("clamp-decorative", outputs.RuntimeToml);
        Assert.DoesNotContain("clamp-decorative", File.ReadAllText(asset.RuntimeTomlPath));
    }

    private static SpriteCard Card(SpriteCardProjection projection, GraphicalConceptPath path)
    {
        return projection.Cards.Single(card => card.ConceptPath == path);
    }

    private sealed class TemporarySunkillAsset : IDisposable
    {
        private TemporarySunkillAsset(string directory, string sourcePath)
        {
            Directory = directory;
            SourcePath = sourcePath;
        }

        public string Directory { get; }
        public string SourcePath { get; }
        public string RuntimeTomlPath => SourcePath[..^".obj.ts".Length] + ".runtime.toml";

        public static TemporarySunkillAsset Create()
        {
            string root = FindRepositoryRoot();
            string sourceDirectory = Path.Combine(
                root,
                "samples",
                "Integrations",
                "Aurelian.Ariadne.VnDemo",
                "Assets");
            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "oblivion-sprite-cards-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(temporaryDirectory);
            foreach (string name in new[]
            {
                "sunkill-dialogue-panel.obj.ts",
                "sunkill-ui-atlas.png",
            })
            {
                File.Copy(Path.Combine(sourceDirectory, name), Path.Combine(temporaryDirectory, name));
            }

            string sourcePath = Path.Combine(temporaryDirectory, "sunkill-dialogue-panel.obj.ts");
            string source = File.ReadAllText(sourcePath)
                .Replace(
                    "        44,\r\n        3,\r\n        \"tile\",\r\n        \"stretch\");",
                    "        30,\r\n        2,\r\n        \"stretch\",\r\n        \"stretch\");",
                    StringComparison.Ordinal)
                .Replace(
                    "        44,\n        3,\n        \"tile\",\n        \"stretch\");",
                    "        30,\n        2,\n        \"stretch\",\n        \"stretch\");",
                    StringComparison.Ordinal);
            File.WriteAllText(sourcePath, source);
            ObjectAssetCompilationResult compilation = ObjectAssetCompiler.CompileFile(sourcePath);
            Assert.True(compilation.Success);
            ObjectAssetBuildOutputs outputs = ObjectAssetCompiler.Emit(compilation.Document!, sourcePath);
            File.WriteAllText(sourcePath[..^".obj.ts".Length] + ".runtime.toml", outputs.RuntimeToml);
            return new TemporarySunkillAsset(temporaryDirectory, sourcePath);
        }

        public void Dispose()
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "Copeland.slnx")))
            {
                current = current.Parent;
            }

            return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
