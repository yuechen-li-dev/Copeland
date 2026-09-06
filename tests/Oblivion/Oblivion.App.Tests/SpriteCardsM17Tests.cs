using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Assets;
using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class SpriteCardsM17Tests
{
    private static readonly GraphicalConceptPath Top = new("panel.dialogue.top");
    private static readonly GraphicalConceptPath Center = new("panel.dialogue.top.center");
    private static readonly GraphicalConceptPath ClampC = new("panel.dialogue.top.clamp-c");
    private static readonly GraphicalConceptPath Decorative = new("panel.dialogue.top.clamp-decorative");

    [Fact]
    public void InsertMoveTwiceAndRemoveRoundTripsSemanticProgram()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        string sourceBefore = File.ReadAllText(asset.SourcePath);
        SpriteCardProjection original = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        string semanticBefore = SemanticHash(asset.SourcePath);
        SpriteCardEdgeSummary allocationBefore = original.EdgeSummaries.Single(summary => summary.Edge == "top");

        SpriteCardStructuralEditResult inserted = service.ApplyStructuralEdit(
            original,
            InsertBefore(original, Center));

        Assert.True(inserted.Applied, FirstDiagnostic(inserted));
        Assert.Contains(Decorative, inserted.ConceptsAdded);
        Assert.Contains(new GraphicalConceptPath("panel.dialogue.bottom.clamp-decorative"), inserted.ConceptsAdded);
        Assert.Contains("fixed(prefix + \".clamp-decorative\", clamp, 7)", inserted.AfterSnippet);
        SpriteCardProjection afterInsert = inserted.RefreshedProjection!;
        Assert.Contains(afterInsert.Cards, card => card.ConceptPath == Decorative);
        Assert.Equal(allocationBefore.MinimumDemand + 7, afterInsert.EdgeSummaries.Single(summary => summary.Edge == "top").MinimumDemand);
        Assert.Contains("clamp-decorative", File.ReadAllText(asset.RuntimeTomlPath));

        SpriteCardStructuralEditResult movedOnce = service.ApplyStructuralEdit(
            afterInsert,
            MoveAfter(afterInsert, Decorative, Center));
        Assert.True(movedOnce.Applied, FirstDiagnostic(movedOnce));
        Assert.Equal(Decorative, Assert.Single(movedOnce.ConceptsMoved));
        Assert.Empty(movedOnce.ConceptsAdded);
        Assert.Empty(movedOnce.ConceptsRemoved);

        SpriteCardStructuralEditResult movedTwice = service.ApplyStructuralEdit(
            movedOnce.RefreshedProjection!,
            MoveAfter(movedOnce.RefreshedProjection!, Decorative, ClampC));
        Assert.True(movedTwice.Applied, FirstDiagnostic(movedTwice));

        SpriteCardStructuralEditResult removed = service.ApplyStructuralEdit(
            movedTwice.RefreshedProjection!,
            Remove(movedTwice.RefreshedProjection!, Decorative));
        Assert.True(removed.Applied, FirstDiagnostic(removed));
        Assert.Contains(Decorative, removed.ConceptsRemoved);
        Assert.DoesNotContain(removed.RefreshedProjection!.Cards, card => card.ConceptPath == Decorative);
        Assert.Equal(allocationBefore, removed.RefreshedProjection.EdgeSummaries.Single(summary => summary.Edge == "top"));
        Assert.Equal(semanticBefore, SemanticHash(asset.SourcePath));
        Assert.Equal(sourceBefore, File.ReadAllText(asset.SourcePath));
        Assert.Equal(4, service.StructuralHistory.Count);
    }

    [Fact]
    public void InsertPreservesCommentsBlankLinesAndUnrelatedRecordTableRows()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create(source => source.Replace(
            "            fixed(prefix + \".clamp-b\", clamp, 7),",
            "            // keep: clamp-b leads the center ornament\n            fixed(prefix + \".clamp-b\", clamp, 7),\n\n            // keep: center remains authored",
            StringComparison.Ordinal));
        string before = File.ReadAllText(asset.SourcePath);
        string regionTablePrefix = before[..before.IndexOf("function fixed", StringComparison.Ordinal)];
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);

        SpriteCardStructuralEditResult result = service.ApplyStructuralEdit(
            projection,
            InsertBefore(projection, Center));

        Assert.True(result.Applied, FirstDiagnostic(result));
        string after = File.ReadAllText(asset.SourcePath);
        Assert.Contains("// keep: clamp-b leads the center ornament", after);
        Assert.Contains("// keep: center remains authored", after);
        Assert.StartsWith(regionTablePrefix, after, StringComparison.Ordinal);
    }

    [Fact]
    public void StaleAndAmbiguousStructuralEditsRejectWithoutWriting()
    {
        using TemporarySunkillAsset staleAsset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        SpriteCardProjection staleProjection = service.BuildProjection(staleAsset.SourcePath, "dialogue", 800, 220);
        File.AppendAllText(staleAsset.SourcePath, "\n// external edit\n");
        string staleSource = File.ReadAllText(staleAsset.SourcePath);

        SpriteCardStructuralEditResult stale = service.ApplyStructuralEdit(
            staleProjection,
            InsertBefore(staleProjection, Center));

        Assert.False(stale.Applied);
        Assert.Contains(stale.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-SPRITE-CARD-STALE-SOURCE");
        Assert.Equal(staleSource, File.ReadAllText(staleAsset.SourcePath));

        using TemporarySunkillAsset ambiguousAsset = TemporarySunkillAsset.Create(source => source.Replace(
            "        segments: [",
            "        segments: [],\n        segments: [",
            StringComparison.Ordinal));
        SpriteCardProjection ambiguousProjection = service.BuildProjection(ambiguousAsset.SourcePath, "dialogue", 800, 220);
        string ambiguousSource = File.ReadAllText(ambiguousAsset.SourcePath);

        SpriteCardStructuralEditResult ambiguous = service.ApplyStructuralEdit(
            ambiguousProjection,
            InsertBefore(ambiguousProjection, Center));

        Assert.False(ambiguous.Applied);
        Assert.Contains(ambiguous.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-SPRITE-CARD-AMBIGUOUS-SOURCE");
        Assert.Equal(ambiguousSource, File.ReadAllText(ambiguousAsset.SourcePath));
    }

    [Fact]
    public void StructuralBoundaryRejectsNonAdjacentMoveAndCapRemoval()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        string before = File.ReadAllText(asset.SourcePath);

        SpriteCardStructuralEditResult move = service.ApplyStructuralEdit(
            projection,
            MoveAfter(
                projection,
                new GraphicalConceptPath("panel.dialogue.top.clamp-a"),
                Center));
        SpriteCardStructuralEditResult remove = service.ApplyStructuralEdit(
            projection,
            Remove(projection, new GraphicalConceptPath("panel.dialogue.top.cap-left")));

        Assert.False(move.Applied);
        Assert.Contains("adjacent", FirstDiagnostic(move), StringComparison.OrdinalIgnoreCase);
        Assert.False(remove.Applied);
        Assert.Contains("cap", FirstDiagnostic(remove), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(asset.SourcePath));
    }

    [Fact]
    public void OutputCommitFailureRollsBackAuthoritativeSourceAndDerivedFiles()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        string sourceBefore = File.ReadAllText(asset.SourcePath);
        File.Delete(asset.RuntimeTomlPath);
        System.IO.Directory.CreateDirectory(asset.RuntimeTomlPath);

        SpriteCardStructuralEditResult result = service.ApplyStructuralEdit(
            projection,
            InsertBefore(projection, Center));

        Assert.False(result.Applied);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "OBLIVION-SPRITE-CARD-WRITE-FAILED");
        Assert.Equal(sourceBefore, File.ReadAllText(asset.SourcePath));
        Assert.DoesNotContain("clamp-decorative", sourceBefore, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertAfterAndMoveBeforeUseTheSameStableIdentity()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        GraphicalConceptPath clampB = new("panel.dialogue.top.clamp-b");

        SpriteCardStructuralEditResult inserted = service.ApplyStructuralEdit(
            projection,
            InsertAfter(projection, clampB));
        SpriteCardStructuralEditResult moved = service.ApplyStructuralEdit(
            inserted.RefreshedProjection!,
            MoveBefore(inserted.RefreshedProjection!, Decorative, clampB));

        Assert.True(inserted.Applied, FirstDiagnostic(inserted));
        Assert.True(moved.Applied, FirstDiagnostic(moved));
        Assert.Equal(Decorative, Assert.Single(moved.ConceptsMoved));
        Assert.Empty(moved.ConceptsAdded);
        Assert.Empty(moved.ConceptsRemoved);
    }

    [Fact]
    public void CardViewProjectsExplicitStructuralControls()
    {
        using TemporarySunkillAsset asset = TemporarySunkillAsset.Create();
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);

        string svg = OblivionSpriteCardRenderer.RenderSvg(
            projection,
            new OblivionSpriteCardRenderOptions(
                FilterKind: GraphicalConceptKind.EdgeSegment,
                Selected: Center));

        Assert.Contains("+ Before", svg);
        Assert.Contains("+ After", svg);
        Assert.Contains("←", svg);
        Assert.Contains("→", svg);
        Assert.Contains("Remove", svg);
    }

    private static SpriteCardStructuralEditIntent InsertBefore(
        SpriteCardProjection projection,
        GraphicalConceptPath anchor)
    {
        return new SpriteCardStructuralEditIntent(
            SpriteCardStructuralEditKind.InsertBefore,
            Top,
            null,
            anchor,
            new SpriteCardNewSegment(
                "clamp-decorative",
                "dialogue.top.clamp",
                SpriteCardSegmentAllocation.Fixed,
                7,
                0,
                "crop"),
            projection.SourceSha256,
            "horizontalEdge");
    }

    private static SpriteCardStructuralEditIntent MoveAfter(
        SpriteCardProjection projection,
        GraphicalConceptPath concept,
        GraphicalConceptPath anchor)
    {
        return new SpriteCardStructuralEditIntent(
            SpriteCardStructuralEditKind.MoveAfter,
            Top,
            concept,
            anchor,
            null,
            projection.SourceSha256,
            "horizontalEdge");
    }

    private static SpriteCardStructuralEditIntent InsertAfter(
        SpriteCardProjection projection,
        GraphicalConceptPath anchor)
    {
        SpriteCardStructuralEditIntent before = InsertBefore(projection, anchor);
        return before with { Kind = SpriteCardStructuralEditKind.InsertAfter };
    }

    private static SpriteCardStructuralEditIntent MoveBefore(
        SpriteCardProjection projection,
        GraphicalConceptPath concept,
        GraphicalConceptPath anchor)
    {
        SpriteCardStructuralEditIntent after = MoveAfter(projection, concept, anchor);
        return after with { Kind = SpriteCardStructuralEditKind.MoveBefore };
    }

    private static SpriteCardStructuralEditIntent Remove(
        SpriteCardProjection projection,
        GraphicalConceptPath concept)
    {
        return new SpriteCardStructuralEditIntent(
            SpriteCardStructuralEditKind.Remove,
            Top,
            concept,
            null,
            null,
            projection.SourceSha256,
            "horizontalEdge");
    }

    private static string SemanticHash(string sourcePath)
    {
        ObjectAssetCompilationResult compilation = ObjectAssetCompiler.CompileFile(sourcePath);
        Assert.True(compilation.Success);
        string semantic = ObjectAssetCompiler.Emit(compilation.Document!, sourcePath).Json;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semantic)));
    }

    private static string FirstDiagnostic(SpriteCardStructuralEditResult result)
    {
        return result.Diagnostics.FirstOrDefault()?.Message ?? result.Status;
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

        public static TemporarySunkillAsset Create(Func<string, string>? transform = null)
        {
            string root = FindRepositoryRoot();
            string sourceDirectory = Path.Combine(
                root,
                "samples",
                "Integrations",
                "Aurelian.Ariadne.VnDemo",
                "Assets");
            string directory = Path.Combine(Path.GetTempPath(), "oblivion-m17-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(sourceDirectory, "sunkill-ui-atlas.png"), Path.Combine(directory, "sunkill-ui-atlas.png"));
            string sourcePath = Path.Combine(directory, "sunkill-dialogue-panel.obj.ts");
            string source = File.ReadAllText(Path.Combine(sourceDirectory, "sunkill-dialogue-panel.obj.ts"))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            File.WriteAllText(sourcePath, transform?.Invoke(source) ?? source);
            ObjectAssetCompilationResult compilation = ObjectAssetCompiler.CompileFile(sourcePath);
            if (compilation.Success)
            {
                ObjectAssetBuildOutputs outputs = ObjectAssetCompiler.Emit(compilation.Document!, sourcePath);
                File.WriteAllText(sourcePath[..^".obj.ts".Length] + ".runtime.toml", outputs.RuntimeToml);
            }

            return new TemporarySunkillAsset(directory, sourcePath);
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
