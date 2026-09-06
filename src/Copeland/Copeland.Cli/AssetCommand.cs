using Copeland.TS.Assets;
using Copeland.TS.Diagnostics;
using Copeland.TS.Manifest;

namespace Copeland.Cli;

internal static class AssetCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3 || args[1] != "build")
        {
            return Usage("Usage: tscl asset build <manifest.tsx> [--output <directory>].");
        }

        string manifestPath = Path.GetFullPath(args[2]);
        string? output = null;
        for (int index = 3; index < args.Length; index++)
        {
            if (args[index] == "--output" && index + 1 < args.Length)
            {
                output = args[++index];
                continue;
            }

            return Usage($"Unknown asset build argument '{args[index]}'.");
        }

        if (!string.Equals(Path.GetFileName(manifestPath), "manifest.tsx", StringComparison.OrdinalIgnoreCase))
        {
            return Usage("Asset build requires a manifest.tsx path.");
        }

        string projectRoot = Path.GetDirectoryName(manifestPath)!;
        ManifestProjectLoadResult load = CopelandProject.LoadRootManifest(projectRoot);
        if (!load.Success || load.Manifest?.Assets is null)
        {
            WriteDiagnostics(load.Diagnostics);
            if (load.Manifest?.Assets is null && load.Diagnostics.Count == 0)
            {
                Console.Error.WriteLine("COPE-ASSET-CLI-0001 error: manifest.tsx does not declare <Assets>.");
            }

            return 1;
        }

        CopelandManifest manifest = load.Manifest;
        ManifestAssetGraph graph = manifest.Assets;
        ManifestAssetOutputs outputs = manifest.AssetOutputs
            ?? new ManifestAssetOutputs(Toml: true, Json: true, Runtime: true, Audit: true);
        string assetRoot = Path.GetFullPath(Path.Combine(projectRoot, graph.SourceRoot));
        string outputRoot = output is null
            ? assetRoot
            : Path.GetFullPath(output, projectRoot);
        Directory.CreateDirectory(outputRoot);

        var compiled = new List<(ManifestObjectAsset Registration, ObjectAssetDocument Document, ObjectAssetBuildOutputs Outputs)>();
        foreach (ManifestObjectAsset registration in TopologicalOrder(graph.Objects))
        {
            string sourcePath = Path.GetFullPath(Path.Combine(assetRoot, registration.Source));
            ObjectAssetCompilationResult result = ObjectAssetCompiler.CompileFile(sourcePath);
            if (!result.Success)
            {
                WriteDiagnostics(result.Diagnostics);
                return 1;
            }

            ObjectAssetDocument document = result.Document
                ?? throw new InvalidOperationException("Successful object compilation did not return a document.");
            if (!graph.Textures.Any(texture => texture.Id == document.Texture.Id))
            {
                Console.Error.WriteLine($"COPE-ASSET-CLI-0002 error: Object '{registration.Id}' references texture '{document.Texture.Id}', which is not registered in manifest.tsx.");
                return 1;
            }

            compiled.Add((registration, document, ObjectAssetCompiler.Emit(document, sourcePath)));
        }

        string[] duplicateDocumentIds = compiled
            .GroupBy(item => item.Document.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateDocumentIds.Length > 0)
        {
            Console.Error.WriteLine("COPE-ASSET-CLI-0003 error: Duplicate object document IDs: " + string.Join(", ", duplicateDocumentIds));
            return 1;
        }

        var emittedFiles = new List<string>();
        foreach ((ManifestObjectAsset registration, _, ObjectAssetBuildOutputs projection) in compiled)
        {
            string baseName = registration.Source[..^".obj.ts".Length];
            if (outputs.Toml)
            {
                Write(Path.Combine(outputRoot, baseName + ".obj.toml"), projection.Toml, emittedFiles, outputRoot);
            }
            if (outputs.Json)
            {
                Write(Path.Combine(outputRoot, baseName + ".obj.json"), projection.Json, emittedFiles, outputRoot);
            }
            if (outputs.Runtime)
            {
                Write(Path.Combine(outputRoot, baseName + ".runtime.toml"), projection.RuntimeToml, emittedFiles, outputRoot);
            }
            if (outputs.Audit)
            {
                Write(Path.Combine(outputRoot, baseName + ".audit.json"), projection.AuditJson, emittedFiles, outputRoot);
            }
        }

        emittedFiles.Add("manifest.generated.json");
        string manifestJson = ObjectAssetManifestProjection.EmitJson(
            manifest,
            compiled.Select(item => new ObjectAssetManifestEntry(item.Registration, item.Document)).ToArray(),
            emittedFiles);
        Write(Path.Combine(outputRoot, "manifest.generated.json"), manifestJson, emittedFiles, outputRoot);

        Console.Out.WriteLine($"compiled {compiled.Count} object asset(s) from {manifestPath}");
        foreach (string file in emittedFiles.OrderBy(path => path, StringComparer.Ordinal))
        {
            Console.Out.WriteLine("wrote " + file);
        }
        return 0;
    }

    private static IReadOnlyList<ManifestObjectAsset> TopologicalOrder(IReadOnlyList<ManifestObjectAsset> objects)
    {
        IReadOnlyDictionary<string, ManifestObjectAsset> byId = objects.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
        var result = new List<ManifestObjectAsset>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (ManifestObjectAsset asset in objects.OrderBy(asset => asset.Id, StringComparer.Ordinal))
        {
            Visit(asset);
        }
        return result;

        void Visit(ManifestObjectAsset asset)
        {
            if (!visited.Add(asset.Id))
            {
                return;
            }

            foreach (string dependency in asset.Dependencies.OrderBy(id => id, StringComparer.Ordinal))
            {
                if (byId.TryGetValue(dependency, out ManifestObjectAsset? target))
                {
                    Visit(target);
                }
            }
            result.Add(asset);
        }
    }

    private static void Write(
        string path,
        string contents,
        ICollection<string> emittedFiles,
        string outputRoot)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, contents);
        string relativePath = Path.GetRelativePath(outputRoot, path).Replace('\\', '/');
        if (!emittedFiles.Contains(relativePath, StringComparer.Ordinal))
        {
            emittedFiles.Add(relativePath);
        }
    }

    private static void WriteDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            Console.Error.WriteLine($"{diagnostic.SourcePath}({diagnostic.Position},{diagnostic.Length}): {diagnostic.Id} error: {diagnostic.Message}");
        }
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine("COPE-ASSET-CLI-0004 error: " + message);
        return 2;
    }
}
