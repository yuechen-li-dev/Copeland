using Aurelian.Rendering.Contracts.Models;

namespace Aurelian.Assets.Models;

public sealed record ModelAssetRecord(string Id, string Path, ModelImportSettings Settings);

/// <summary>Manifest-owned stable IDs. Loading and replacement are explicit; no file watchers or discovery.</summary>
public sealed class ModelAssetCatalog
{
    private readonly Dictionary<string, ModelAssetRecord> records;
    private readonly Dictionary<string, ModelSlot> slots = new(StringComparer.Ordinal);
    private readonly string directory;

    private ModelAssetCatalog(AssetManifest manifest, string manifestPath)
    {
        directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        records = manifest.Models.ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    public static ModelAssetCatalog Load(string manifestPath)
    {
        var parsed = AssetManifestParser.Parse(File.ReadAllText(manifestPath), manifestPath);
        IReadOnlyList<AssetDiagnostic> diagnostics = parsed.Manifest is null
            ? parsed.Diagnostics : [.. parsed.Diagnostics, .. AssetManifestValidator.Validate(parsed.Manifest, manifestPath)];
        if (diagnostics.Any(item => item.Fatal))
            throw new InvalidDataException(string.Join(Environment.NewLine, diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        return new(parsed.Manifest!, manifestPath);
    }

    public ModelSlot Model(string id)
    {
        if (slots.TryGetValue(id, out ModelSlot? slot)) return slot;
        ModelAssetRecord record = records[id];
        ModelImportResult result = GlbModelImporter.Load(id, Path.Combine(directory, record.Path), record.Settings);
        if (!result.Success) throw new InvalidDataException(Describe(result.Diagnostics));
        slot = new(result.Model!);
        slots.Add(id, slot);
        return slot;
    }

    public ModelImportResult Reimport(string id, Action<StaticModel>? prepare = null)
    {
        ModelSlot slot = Model(id);
        ModelAssetRecord record = records[id];
        ModelImportResult candidate = GlbModelImporter.Load(id, Path.Combine(directory, record.Path), record.Settings);
        if (!candidate.Success) return candidate;
        try
        {
            slot.Replace(candidate.Model!, prepare);
            return candidate;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(null, [.. candidate.Diagnostics, new AssetDiagnostic("AA3205", "error",
                "Replacement retained the previous model: " + error.Message, record.Path)]);
        }
    }

    public static IReadOnlyList<AssetDiagnostic> Validate(AssetManifest manifest, string path)
    {
        var diagnostics = new List<AssetDiagnostic>();
        var ids = manifest.Shaders.Select(item => item.Id).Concat(manifest.ShaderReferences.Select(item => item.Id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (ModelAssetRecord model in manifest.Models)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.Id, "^[a-z0-9._-]+$") || !ids.Add(model.Id))
                diagnostics.Add(new("AA3203", "error", $"Invalid or duplicate asset ID '{model.Id}'.", path));
            if (string.IsNullOrWhiteSpace(model.Path) || Path.IsPathRooted(model.Path)
                || model.Path.Contains('\\') || AssetManifestValidator.ContainsParentTraversalSegment(model.Path)
                || !model.Path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                || !float.IsFinite(model.Settings.Scale) || model.Settings.Scale <= 0)
                diagnostics.Add(new("AA3204", "error", $"Model '{model.Id}' needs a relative .glb path and a positive finite scale.", path));
        }
        return diagnostics;
    }

    public static string Describe(IEnumerable<AssetDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(item => $"{item.Code}: {item.SourcePath}: {item.Message}"));
}
