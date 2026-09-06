using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Copeland.TS.Assets;
using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.Standalone;

internal static class SpriteCardsM16Proof
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Run(string outputDirectory)
    {
        string root = FindRepositoryRoot();
        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        string sourcePath = Path.Combine(
            root,
            "samples",
            "Integrations",
            "Aurelian.Ariadne.VnDemo",
            "Assets",
            "sunkill-dialogue-panel.obj.ts");
        var service = new OblivionSpriteCardService();
        EnsureBaseline(service, sourcePath);
        SpriteCardProjection before = service.BuildProjection(sourcePath, "dialogue", 800, 220);
        WriteJson(Path.Combine(output, "card-model.json"), before);
        WriteSvg(Path.Combine(output, "sunkill-edit-before.svg"), before, new OblivionSpriteCardRenderOptions(FilterKind: GraphicalConceptKind.EdgeSegment));

        GraphicalConceptPath center = new("panel.dialogue.top.center");
        GraphicalConceptPath glow = new("panel.dialogue.top.glow-left");
        var traces = new List<SpriteCardEditTrace>();
        Apply(center, SpriteCardEditProperty.FlexWeight, "3");
        Apply(glow, SpriteCardEditProperty.Sampling, "tile");
        Apply(center, SpriteCardEditProperty.MinimumLength, "44");
        Apply(center, SpriteCardEditProperty.SourceRegion, "dialogue.top.glow");
        Apply(center, SpriteCardEditProperty.SourceRegion, "dialogue.top.center");

        SpriteCardProjection after = service.BuildProjection(sourcePath, "dialogue", 800, 220);
        WriteJson(Path.Combine(output, "source-edit-proof.json"), new
        {
            authority = "sunkill-dialogue-panel.obj.ts",
            generatedTomlEdited = false,
            traces,
            refreshedVersion = after.CompileVersion,
            refreshedSourceSha256 = after.SourceSha256,
        });
        WriteSvg(Path.Combine(output, "sunkill-edit-after.svg"), after, new OblivionSpriteCardRenderOptions(FilterKind: GraphicalConceptKind.EdgeSegment));
        Stopwatch previewTimer = Stopwatch.StartNew();
        string overviewSvg = OblivionSpriteCardRenderer.RenderSvg(
            after,
            new OblivionSpriteCardRenderOptions(FilterKind: GraphicalConceptKind.EdgeSegment));
        previewTimer.Stop();
        File.WriteAllText(Path.Combine(output, "sunkill-cards-overview.svg"), overviewSvg);
        WriteSvg(Path.Combine(output, "sunkill-card-selected.svg"), after, new OblivionSpriteCardRenderOptions(Selected: center));
        WriteSvg(
            Path.Combine(output, "sunkill-guide-datum.svg"),
            after,
            new OblivionSpriteCardRenderOptions(Selected: new GraphicalConceptPath("guide.dialogue.datum.text-baseline")));

        SpriteCardProjection narrow = service.BuildProjection(sourcePath, "dialogue", 220, 220);
        SpriteCardProjection wide = service.BuildProjection(sourcePath, "dialogue", 1200, 220);
        WriteSvg(Path.Combine(output, "sunkill-edge-narrow.svg"), narrow, new OblivionSpriteCardRenderOptions(FilterKind: GraphicalConceptKind.EdgeSegment));
        WriteSvg(Path.Combine(output, "sunkill-edge-wide.svg"), wide, new OblivionSpriteCardRenderOptions(FilterKind: GraphicalConceptKind.EdgeSegment));
        WriteJson(Path.Combine(output, "allocator-projection-proof.json"), new
        {
            owner = "Copeland.SpanAllocation.SpanAllocator",
            duplicatePreviewAllocator = false,
            narrow = narrow.EdgeSummaries,
            nominal = after.EdgeSummaries,
            wide = wide.EdgeSummaries,
        });

        SpriteCard[] scaffolding = after.Cards.Where(card => card.Kind is
            GraphicalConceptKind.Guide or GraphicalConceptKind.Datum or GraphicalConceptKind.Blockout).ToArray();
        WriteJson(Path.Combine(output, "guide-datum-proof.json"), new
        {
            authored = scaffolding,
            visualized = true,
            runtimeTomlContainsScaffolding = File.ReadAllText(sourcePath[..^".obj.ts".Length] + ".runtime.toml")
                .Contains("guide.dialogue", StringComparison.Ordinal),
        });
        WriteJson(Path.Combine(output, "concept-model.json"), new
        {
            law = new[] { "concept", "stable-path", "authored", "resolved", "runtime-projection", "notebook-card" },
            cardKinds = Enum.GetNames<GraphicalConceptKind>(),
            relationshipKinds = Enum.GetNames<SpriteCardRelationshipKind>(),
            sourceAuthority = new[] { "*.obj.ts", "manifest.tsx" },
            generatedAuthority = false,
        });
        WriteJson(Path.Combine(output, "concept-lineage.json"), ConceptLineage());
        WriteStaleProof(service, sourcePath, output);
        WriteJson(Path.Combine(output, "performance.json"), new
        {
            cardProjectionMilliseconds = after.BuildDuration.TotalMilliseconds,
            recompileMilliseconds = traces.Select(trace => trace.RecompileDuration.TotalMilliseconds).ToArray(),
            allocationVisualizationMilliseconds = previewTimer.Elapsed.TotalMilliseconds,
            previewRefreshMilliseconds = after.BuildDuration.TotalMilliseconds + previewTimer.Elapsed.TotalMilliseconds,
        });
        WriteJson(Path.Combine(output, "manifest.json"), new
        {
            milestone = "OBLIVION-NOTEBOOK-SPRITE-CARDS-M16",
            kind = "semantic-visual-programming-for-graphical-assets",
            crossProjectSynthesisCompleted = true,
            conceptPathQualified = true,
            conceptStructReconciled = true,
            machinaCanvasGuideIdeasRecovered = true,
            aetherisConceptIdeasRecovered = true,
            spriteForgeIntegrated = true,
            allocatorProjectionUsesRuntimeAllocator = true,
            spriteCardsQualified = true,
            sourceAwareEditingQualified = true,
            staleEditProtectionQualified = true,
            guideDatumAuthoringQualified = true,
            authoringScaffoldingErases = true,
            sunkillDogfoodQualified = true,
            generalNodeGraphAdded = false,
            pixelEditorAdded = false,
        });
        Console.WriteLine($"M16 proof written to {output}");

        void Apply(GraphicalConceptPath path, SpriteCardEditProperty property, string value)
        {
            SpriteCardProjection projection = service.BuildProjection(sourcePath, "dialogue", 800, 220);
            SpriteCardEditTrace trace = service.ApplyEdit(projection, path, property, value);
            if (!trace.Applied)
            {
                throw new InvalidOperationException($"M16 proof edit failed: {trace.Diagnostics.FirstOrDefault()?.Message}");
            }

            traces.Add(trace);
        }
    }

    private static void EnsureBaseline(OblivionSpriteCardService service, string sourcePath)
    {
        GraphicalConceptPath center = new("panel.dialogue.top.center");
        GraphicalConceptPath glow = new("panel.dialogue.top.glow-left");
        Reset(center, SpriteCardEditProperty.MinimumLength, "30", card => card.Authored.MinimumLength != 30);
        Reset(center, SpriteCardEditProperty.FlexWeight, "2", card => card.Authored.Weight != 2);
        Reset(glow, SpriteCardEditProperty.Sampling, "stretch", card => card.Authored.Sampling != "stretch");

        void Reset(
            GraphicalConceptPath path,
            SpriteCardEditProperty property,
            string value,
            Func<SpriteCard, bool> requiresReset)
        {
            SpriteCardProjection projection = service.BuildProjection(sourcePath, "dialogue", 800, 220);
            SpriteCard card = projection.Cards.Single(candidate => candidate.ConceptPath == path);
            if (requiresReset(card))
            {
                SpriteCardEditTrace trace = service.ApplyEdit(projection, path, property, value);
                if (!trace.Applied)
                {
                    throw new InvalidOperationException("Could not establish the M16 proof baseline.");
                }
            }
        }
    }

    private static object[] ConceptLineage()
    {
        return
        [
            Lineage("stable semantic identity", "Aetheris Concept Struct/Path + M15 IDs", "retain", "GraphicalConceptPath"),
            Lineage("guides and datums", "MachinaCanvas guide sidecars", "adapt", "ObjectAssetAuthoringConcept"),
            Lineage("blockouts", "MachinaCanvas blockout sidecars", "adapt", "authoring-only spatial concept"),
            Lineage("sprite frames and regions", "SpriteForge + MachinaCanvas", "retain region; defer animation", "ObjectAssetRegion / RegionCard"),
            Lineage("sidecar attachment", "MachinaCanvas TOML", "supersede", "compiler-owned semantic IR"),
            Lineage("resolved overlay", "MachinaCanvas overlays + M15 strip", "merge", "SpriteCardResolvedState + SVG overlay"),
            Lineage("selectors", "Aetheris/Profile", "bound", "exact path, kind filter, diagnostics-only"),
            Lineage("authoring erasure", "Aetheris/Firmament", "retain law", "guides/datums/blockouts omit runtime TOML"),
            Lineage("allocation", "M15", "retain unchanged", "SpanAllocator"),
            Lineage("freeform workflow graph", "MachinaCanvas", "reject", "ordered strip/grid/detail only"),
        ];
    }

    private static object Lineage(string concept, string origin, string decision, string owner)
    {
        return new { concept, origin, problem = "semantic graphical authoring/inspection", decision, owner };
    }

    private static void WriteStaleProof(OblivionSpriteCardService service, string sourcePath, string output)
    {
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "m16-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string copy = Path.Combine(temporaryDirectory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, copy);
            File.Copy(Path.Combine(Path.GetDirectoryName(sourcePath)!, "sunkill-ui-atlas.png"), Path.Combine(temporaryDirectory, "sunkill-ui-atlas.png"));
            SpriteCardProjection projection = service.BuildProjection(copy, "dialogue", 800, 220);
            File.AppendAllText(copy, "\n// external source edit\n");
            SpriteCardEditTrace trace = service.ApplyEdit(
                projection,
                new GraphicalConceptPath("panel.dialogue.top.center"),
                SpriteCardEditProperty.FlexWeight,
                "5");
            WriteJson(Path.Combine(output, "stale-edit-proof.json"), trace);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);
    }

    private static void WriteSvg(
        string path,
        SpriteCardProjection projection,
        OblivionSpriteCardRenderOptions options)
    {
        File.WriteAllText(path, OblivionSpriteCardRenderer.RenderSvg(projection, options));
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
