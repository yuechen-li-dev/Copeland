using System.Text.Json;
using Copeland.TS.Manifest;

namespace Copeland.TS.Assets;

public sealed record ObjectAssetManifestEntry(
    ManifestObjectAsset Registration,
    ObjectAssetDocument Document);

/// <summary>
/// Emits the deterministic interoperability projection of compiler-owned
/// manifest and object-asset semantic state.
/// </summary>
public static class ObjectAssetManifestProjection
{
    public static string EmitJson(
        CopelandManifest manifest,
        IReadOnlyList<ObjectAssetManifestEntry> objects,
        IEnumerable<string> files)
    {
        ManifestAssetGraph graph = manifest.Assets
            ?? throw new ArgumentException("Manifest does not contain an asset graph.", nameof(manifest));
        ManifestAssetOutputs outputs = manifest.AssetOutputs
            ?? new ManifestAssetOutputs(Toml: true, Json: true, Runtime: true, Audit: true);
        var projection = new
        {
            generated = "Do not edit; regenerate from manifest.tsx and *.obj.ts.",
            schemaVersion = 1,
            generatedFrom = "manifest.tsx",
            name = manifest.Workspace.Name,
            sourceRoot = graph.SourceRoot,
            textures = graph.Textures
                .OrderBy(texture => texture.Id, StringComparer.Ordinal)
                .Select(texture => new { texture.Id, texture.Source }),
            objects = objects
                .OrderBy(item => item.Registration.Id, StringComparer.Ordinal)
                .Select(item => new
                {
                    item.Registration.Id,
                    item.Registration.Source,
                    dependencies = item.Registration.Dependencies.OrderBy(id => id, StringComparer.Ordinal),
                    semanticId = item.Document.Id,
                    textureId = item.Document.Texture.Id,
                    panelIds = item.Document.Panels
                        .Select(panel => panel.Id)
                        .OrderBy(id => id, StringComparer.Ordinal),
                }),
            outputs,
            files = files.OrderBy(path => path, StringComparer.Ordinal),
        };
        return JsonSerializer.Serialize(projection, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }) + Environment.NewLine;
    }
}
