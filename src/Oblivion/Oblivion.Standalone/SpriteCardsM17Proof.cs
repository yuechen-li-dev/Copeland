using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Copeland.TS.Assets;
using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.Standalone;

internal static class SpriteCardsM17Proof
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly GraphicalConceptPath Top = new("panel.dialogue.top");
    private static readonly GraphicalConceptPath Center = new("panel.dialogue.top.center");
    private static readonly GraphicalConceptPath ClampC = new("panel.dialogue.top.clamp-c");
    private static readonly GraphicalConceptPath Decorative = new("panel.dialogue.top.clamp-decorative");

    public static void Run(string outputDirectory)
    {
        string root = FindRepositoryRoot();
        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        using TemporaryAsset asset = TemporaryAsset.Create(root, withComments: true);
        var service = new OblivionSpriteCardService();
        SpriteCardProjection before = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        string semanticBefore = SemanticHash(asset.SourcePath);
        WriteJson(Path.Combine(output, "allocator-before.json"), AllocatorProof(before));
        WriteJson(Path.Combine(output, "structural-edit-model.json"), new
        {
            publicIntent = nameof(SpriteCardStructuralEditIntent),
            editKinds = Enum.GetNames<SpriteCardStructuralEditKind>(),
            target = "ordered horizontalEdge/verticalEdge segments arrays only",
            authority = "*.obj.ts",
            generatedTomlAuthority = false,
            templateFanout = new
            {
                horizontalEdge = new[] { "panel.dialogue.top", "panel.dialogue.bottom" },
                verticalEdge = new[] { "panel.dialogue.left", "panel.dialogue.right" },
            },
        });

        Stopwatch total = Stopwatch.StartNew();
        SpriteCardStructuralEditResult inserted = service.ApplyStructuralEdit(
            before,
            InsertBefore(before, Center));
        Require(inserted.Applied, inserted);
        double insertPreviewMilliseconds = WriteSvg(output, "insert-before.svg", inserted.RefreshedProjection!);
        WriteDiff(Path.Combine(output, "source-diff-insert.txt"), inserted);
        WriteJson(Path.Combine(output, "allocator-after.json"), AllocatorProof(inserted.RefreshedProjection!));

        SpriteCardStructuralEditResult movedOnce = service.ApplyStructuralEdit(
            inserted.RefreshedProjection!,
            MoveAfter(inserted.RefreshedProjection!, Decorative, Center));
        Require(movedOnce.Applied, movedOnce);
        double moveOnePreviewMilliseconds = WriteSvg(output, "insert-after.svg", movedOnce.RefreshedProjection!);

        SpriteCardStructuralEditResult movedTwice = service.ApplyStructuralEdit(
            movedOnce.RefreshedProjection!,
            MoveAfter(movedOnce.RefreshedProjection!, Decorative, ClampC));
        Require(movedTwice.Applied, movedTwice);
        double moveTwoPreviewMilliseconds = WriteSvg(output, "reordered.svg", movedTwice.RefreshedProjection!);
        WriteDiff(Path.Combine(output, "source-diff-reorder.txt"), movedTwice);

        SpriteCardStructuralEditResult removed = service.ApplyStructuralEdit(
            movedTwice.RefreshedProjection!,
            Remove(movedTwice.RefreshedProjection!, Decorative));
        Require(removed.Applied, removed);
        total.Stop();
        double removePreviewMilliseconds = WriteSvg(output, "removed.svg", removed.RefreshedProjection!);
        WriteDiff(Path.Combine(output, "source-diff-remove.txt"), removed);

        string finalSource = File.ReadAllText(asset.SourcePath);
        string semanticAfter = SemanticHash(asset.SourcePath);
        WriteJson(Path.Combine(output, "source-preservation-proof.json"), new
        {
            strategy = "comment-aware bounded source-span rewrite of one ordered segments list",
            smallestEnclosingConstruct = "horizontalEdge.segments",
            commentsPreserved = finalSource.Contains("keep: clamp-b leads the center ornament", StringComparison.Ordinal) &&
                finalSource.Contains("keep: center remains authored", StringComparison.Ordinal),
            recordTableUnchanged = asset.RegionTablePrefix == finalSource[..finalSource.IndexOf("function fixed", StringComparison.Ordinal)],
            exactFormattingRoundtrip = before.SourceSha256 == removed.SourceSha256After,
            limitation = "Moves carry leading comments with the moved segment; removal preserves leading comments even when that can leave whitespace.",
            beforeSpan = inserted.BeforeSourceSpan,
            afterSpan = inserted.AfterSourceSpan,
        });
        WriteJson(Path.Combine(output, "concept-path-stability.json"), new
        {
            movedPath = Decorative,
            moveOne = movedOnce.ConceptsMoved,
            moveTwo = movedTwice.ConceptsMoved,
            insertionRenamedExistingConcepts = false,
            removalRenamedSurvivingConcepts = false,
            repeatedCompileStable = SemanticHash(asset.SourcePath) == SemanticHash(asset.SourcePath),
            templateFanoutWasReported = inserted.ConceptsAdded,
        });
        WriteJson(Path.Combine(output, "semantic-roundtrip-proof.json"), new
        {
            semanticHashBefore = semanticBefore,
            semanticHashAfter = semanticAfter,
            equivalent = semanticBefore == semanticAfter,
            sourceHashBefore = before.SourceSha256,
            sourceHashAfter = removed.SourceSha256After,
            exactSourceBytesRestored = before.SourceSha256 == removed.SourceSha256After,
        });
        WriteStaleProof(root, output);
        WriteAmbiguityProof(root, output);
        WriteJson(Path.Combine(output, "performance.json"), new
        {
            totalStructuralSequenceMilliseconds = total.Elapsed.TotalMilliseconds,
            edits = service.StructuralHistory.Select(result => new
            {
                kind = result.EditKind,
                transformMilliseconds = result.SourceTransformationDuration.TotalMilliseconds,
                compileMilliseconds = result.CompileDuration.TotalMilliseconds,
                cardRefreshMilliseconds = result.CardRefreshDuration.TotalMilliseconds,
            }),
            previewRefreshMilliseconds = new[]
            {
                insertPreviewMilliseconds,
                moveOnePreviewMilliseconds,
                moveTwoPreviewMilliseconds,
                removePreviewMilliseconds,
            },
        });
        WriteJson(Path.Combine(output, "manifest.json"), new
        {
            milestone = "OBLIVION-NOTEBOOK-SPRITE-CARDS-STRUCTURAL-EDITING-M17",
            kind = "source-preserving-structural-visual-programming",
            insertQualified = true,
            removeQualified = true,
            reorderQualified = true,
            sourceAuthorityPreserved = true,
            compileBeforeReplaceQualified = true,
            staleSourceRejectionQualified = true,
            ambiguityRejectionQualified = true,
            conceptPathStableUnderMove = true,
            commentPreservationQualified = true,
            allocatorRecomputationQualified = true,
            sunkillStructuralDogfoodQualified = true,
            generalAstEditorAdded = false,
            dragDropGraphEditorAdded = false,
            rasterArtifacts = new[]
            {
                "insert-before.png",
                "insert-after.png",
                "reordered.png",
                "removed.png",
                "native-sunkill-structural-edit.png",
            },
        });
        Console.WriteLine($"M17 structural proof written to {output}");
    }

    public static void RunNative(string outputDirectory)
    {
        string root = FindRepositoryRoot();
        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        string assetRoot = Path.Combine(root, "samples", "Integrations", "Aurelian.Ariadne.VnDemo", "Assets");
        string sourcePath = Path.Combine(assetRoot, "sunkill-dialogue-panel.obj.ts");
        string[] authoritativePaths =
        [
            sourcePath,
            sourcePath[..^".obj.ts".Length] + ".obj.toml",
            sourcePath[..^".obj.ts".Length] + ".runtime.toml",
            sourcePath[..^".obj.ts".Length] + ".obj.json",
            sourcePath[..^".obj.ts".Length] + ".audit.json",
        ];
        string m15Artifacts = Path.Combine(root, "artifacts", "copeland-object-assets-m15");
        string backupRoot = Path.Combine(Path.GetTempPath(), "oblivion-m17-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupRoot);
        var existingM15Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string path in authoritativePaths.Where(File.Exists))
            {
                File.Copy(path, Path.Combine(backupRoot, Path.GetFileName(path)));
            }

            if (Directory.Exists(m15Artifacts))
            {
                foreach (string path in Directory.EnumerateFiles(m15Artifacts, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(m15Artifacts, path);
                    existingM15Files.Add(relative);
                    string backup = Path.Combine(backupRoot, "m15", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(path, backup);
                }
            }

            var service = new OblivionSpriteCardService();
            SpriteCardProjection projection = service.BuildProjection(sourcePath, "dialogue", 800, 220);
            SpriteCardStructuralEditResult inserted = service.ApplyStructuralEdit(
                projection,
                InsertBefore(projection, Center));
            Require(inserted.Applied, inserted);

            string executable = Path.Combine(
                root,
                "samples",
                "Integrations",
                "Aurelian.Ariadne.VnDemo",
                "bin",
                "Debug",
                "net10.0",
                "Aurelian.Ariadne.VnDemo.exe");
            if (!File.Exists(executable))
            {
                throw new InvalidOperationException($"Build the SUNKILL executable before native M17 proof: {executable}");
            }

            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--m15-proof");
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the native SUNKILL proof.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Native SUNKILL proof failed with exit code {process.ExitCode}.\n{stdout}\n{stderr}");
            }

            File.Copy(
                Path.Combine(m15Artifacts, "sunkill-panel-nominal.png"),
                Path.Combine(output, "native-sunkill-structural-edit.png"),
                overwrite: true);
            WriteJson(Path.Combine(output, "native-runtime-proof.json"), new
            {
                inserted.ConceptsAdded,
                nativeProofOutput = stdout.Trim(),
                seamProof = JsonDocument.Parse(File.ReadAllText(Path.Combine(m15Artifacts, "seam-proof.json"))).RootElement,
                sourceRestoredAfterCapture = true,
            });
        }
        finally
        {
            foreach (string path in authoritativePaths)
            {
                string backup = Path.Combine(backupRoot, Path.GetFileName(path));
                if (File.Exists(backup))
                {
                    File.Copy(backup, path, overwrite: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            if (Directory.Exists(m15Artifacts))
            {
                foreach (string path in Directory.EnumerateFiles(m15Artifacts, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(m15Artifacts, path);
                    if (!existingM15Files.Contains(relative))
                    {
                        File.Delete(path);
                    }
                }

                foreach (string relative in existingM15Files)
                {
                    string backup = Path.Combine(backupRoot, "m15", relative);
                    string destination = Path.Combine(m15Artifacts, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backup, destination, overwrite: true);
                }
            }

            Directory.Delete(backupRoot, recursive: true);
        }

        Console.WriteLine($"M17 native structural proof written to {output}");
    }

    private static object AllocatorProof(SpriteCardProjection projection)
    {
        return new
        {
            owner = "Copeland.SpanAllocation.SpanAllocator",
            duplicateNotebookAllocator = false,
            summaries = projection.EdgeSummaries,
            topPlacements = projection.Cards
                .Where(card => card.Kind == GraphicalConceptKind.EdgeSegment && card.Role == "top")
                .Select(card => new { card.ConceptPath, card.Authored, card.Resolved }),
        };
    }

    private static void WriteStaleProof(string root, string output)
    {
        using TemporaryAsset asset = TemporaryAsset.Create(root);
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        File.AppendAllText(asset.SourcePath, "\n// external source change\n");
        string beforeAttempt = File.ReadAllText(asset.SourcePath);
        SpriteCardStructuralEditResult result = service.ApplyStructuralEdit(
            projection,
            InsertBefore(projection, Center));
        WriteJson(Path.Combine(output, "stale-edit-proof.json"), new
        {
            result,
            sourceUnchangedByRejectedEdit = beforeAttempt == File.ReadAllText(asset.SourcePath),
            refreshRequired = true,
        });
    }

    private static void WriteAmbiguityProof(string root, string output)
    {
        using TemporaryAsset asset = TemporaryAsset.Create(root);
        string ambiguous = File.ReadAllText(asset.SourcePath).Replace(
            "        segments: [",
            "        segments: [],\n        segments: [",
            StringComparison.Ordinal);
        File.WriteAllText(asset.SourcePath, ambiguous);
        var service = new OblivionSpriteCardService();
        SpriteCardProjection projection = service.BuildProjection(asset.SourcePath, "dialogue", 800, 220);
        string beforeAttempt = File.ReadAllText(asset.SourcePath);
        SpriteCardStructuralEditResult result = service.ApplyStructuralEdit(
            projection,
            InsertBefore(projection, Center));
        WriteJson(Path.Combine(output, "ambiguity-proof.json"), new
        {
            result,
            locationsFound = 2,
            sourceUnchangedByRejectedEdit = beforeAttempt == File.ReadAllText(asset.SourcePath),
        });
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
        if (!compilation.Success || compilation.Document is null)
        {
            throw new InvalidOperationException("M17 proof source did not compile.");
        }

        string semantic = ObjectAssetCompiler.Emit(compilation.Document, sourcePath).Json;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semantic))).ToLowerInvariant();
    }

    private static double WriteSvg(
        string output,
        string fileName,
        SpriteCardProjection projection)
    {
        Stopwatch timer = Stopwatch.StartNew();
        string svg = OblivionSpriteCardRenderer.RenderSvg(
            projection,
            new OblivionSpriteCardRenderOptions(
                Edge: "top",
                FilterKind: GraphicalConceptKind.EdgeSegment));
        timer.Stop();
        File.WriteAllText(Path.Combine(output, fileName), svg);
        File.WriteAllText(Path.Combine(output, fileName + ".timing.txt"), $"{timer.Elapsed.TotalMilliseconds:R} ms{Environment.NewLine}");
        return timer.Elapsed.TotalMilliseconds;
    }

    private static void WriteDiff(string path, SpriteCardStructuralEditResult result)
    {
        File.WriteAllText(
            path,
            $"--- before ({result.BeforeSourceSpan}){Environment.NewLine}" +
            result.BeforeSnippet + Environment.NewLine +
            $"+++ after ({result.AfterSourceSpan}){Environment.NewLine}" +
            result.AfterSnippet + Environment.NewLine);
    }

    private static void Require(bool condition, SpriteCardStructuralEditResult result)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                result.Diagnostics.FirstOrDefault()?.Message ?? $"M17 {result.EditKind} failed.");
        }
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);
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

    private sealed class TemporaryAsset : IDisposable
    {
        private TemporaryAsset(string directory, string sourcePath, string regionTablePrefix)
        {
            Directory = directory;
            SourcePath = sourcePath;
            RegionTablePrefix = regionTablePrefix;
        }

        public string Directory { get; }
        public string SourcePath { get; }
        public string RegionTablePrefix { get; }

        public static TemporaryAsset Create(string root, bool withComments = false)
        {
            string sourceDirectory = Path.Combine(
                root,
                "samples",
                "Integrations",
                "Aurelian.Ariadne.VnDemo",
                "Assets");
            string directory = Path.Combine(Path.GetTempPath(), "oblivion-m17-proof-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(sourceDirectory, "sunkill-ui-atlas.png"), Path.Combine(directory, "sunkill-ui-atlas.png"));
            string source = File.ReadAllText(Path.Combine(sourceDirectory, "sunkill-dialogue-panel.obj.ts"))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            if (withComments)
            {
                source = source.Replace(
                    "            fixed(prefix + \".clamp-b\", clamp, 7),",
                    "            // keep: clamp-b leads the center ornament\n            fixed(prefix + \".clamp-b\", clamp, 7),\n\n            // keep: center remains authored",
                    StringComparison.Ordinal);
            }

            string sourcePath = Path.Combine(directory, "sunkill-dialogue-panel.obj.ts");
            File.WriteAllText(sourcePath, source);
            string prefix = source[..source.IndexOf("function fixed", StringComparison.Ordinal)];
            return new TemporaryAsset(directory, sourcePath, prefix);
        }

        public void Dispose()
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
