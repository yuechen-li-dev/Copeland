using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aurelian.Assets.Models;

public sealed record ModelBuildEvidence(string Id, string Importer, string ContentIdentity, float Scale,
    int PrimitiveDefinitions, int Occurrences, string[] MaterialSlots, string[] TextureIdentities);
public sealed record ModelBuildResult(IReadOnlyList<AssetDiagnostic> Diagnostics, IReadOnlyList<ModelBuildEvidence> Built);

public static class ModelAssetBuilder
{
    /// <summary>Validate the whole model set before publishing delivery GLBs and provenance. Originals remain editable.</summary>
    public static ModelBuildResult Build(AssetManifest manifest, string manifestPath, string output)
    {
        var diagnostics = ModelAssetCatalog.Validate(manifest, manifestPath).ToList();
        var built = new List<ModelBuildEvidence>();
        if (diagnostics.Any(item => item.Fatal)) return new(diagnostics, built);
        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var candidates = new List<(ModelAssetRecord Record, byte[] Bytes)>();
        foreach (ModelAssetRecord record in manifest.Models)
        {
            string path = Path.Combine(directory, record.Path);
            var result = GlbModelImporter.Load(record.Id, path, record.Settings);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Success) continue;
            var model = result.Model!;
            var materials = model.Occurrences.Select(item => item.Primitive.Material).Distinct().ToArray();
            built.Add(new(record.Id, GlbModelImporter.Version, model.ContentIdentity, record.Settings.Scale,
                model.Occurrences.Select(item => item.Primitive).Distinct().Count(), model.Occurrences.Length,
                materials.Select(item => item.Slot).ToArray(), materials.SelectMany(item => item.Textures())
                    .Where(item => item is not null).Select(item => item!.Texture.Identity).Distinct().ToArray()));
            byte[] bytes = File.ReadAllBytes(path);
            if (GlbModelImporter.ContentIdentity(bytes, record.Settings) != model.ContentIdentity)
            {
                diagnostics.Add(new("AA3206", "error", "Model source changed during the build; retry with a stable export.", path));
                continue;
            }
            candidates.Add((record, bytes));
        }
        if (diagnostics.Any(item => item.Fatal)) return new(diagnostics, []);
        Directory.CreateDirectory(Path.Combine(output, "models"));
        var delivery = new StringBuilder();
        foreach (var candidate in candidates)
        {
            string filename = candidate.Record.Id + ".glb";
            File.WriteAllBytes(Path.Combine(output, "models", filename), candidate.Bytes);
            ModelBuildEvidence evidence = built.Single(item => item.Id == candidate.Record.Id);
            File.WriteAllText(Path.Combine(output, "models", candidate.Record.Id + ".import.json"),
                JsonSerializer.Serialize(evidence, ModelBuildJson.Default.ModelBuildEvidence));
            delivery.AppendLine("[[models]]");
            delivery.AppendLine($"id = \"{candidate.Record.Id}\"");
            delivery.AppendLine($"path = \"models/{filename}\"");
            delivery.AppendLine("scale = " + candidate.Record.Settings.Scale.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            delivery.AppendLine();
        }
        if (candidates.Count > 0) File.WriteAllText(Path.Combine(output, "models.toml"), delivery.ToString());
        return new(diagnostics, built);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ModelBuildEvidence))]
internal partial class ModelBuildJson : JsonSerializerContext;
