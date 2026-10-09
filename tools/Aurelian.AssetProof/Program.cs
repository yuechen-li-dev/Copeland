using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurelian.Assets.Models;
using Aurelian.Games;
using Aurelian.Graphics.Plants;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.Graphics.Vulkan.Native3D;
using Aurelian.Graphics.Vulkan.Resources.Textures;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.Shaders.Graphics;
using Aurelian.World.Scenes;
using Copeland.TS.Gpu;

if (args.Length != 2) throw new ArgumentException("Usage: Aurelian.AssetProof starter-assets-directory output-directory");
string source = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
string working = Path.Combine(output, "working");
Directory.CreateDirectory(Path.Combine(working, "Models"));
File.Copy(Path.Combine(source, "assets.toml"), Path.Combine(working, "assets.toml"), true);
string livePath = Path.Combine(working, "Models", "crate.glb");
File.Copy(Path.Combine(source, "Models", "crate.glb"), livePath, true);
var catalog = ModelAssetCatalog.Load(Path.Combine(working, "assets.toml"));
ModelSlot slot = catalog.Model("starter.crate");
StaticModel original = slot.Current;
ModelMaterial panel = original.Occurrences.First(item => item.Primitive.Material.Slot == "panel").Primitive.Material;
SceneGroup document = StarterScenes.TrainingRange([new("target", new(0, 1, -6), new(0.6f, 1, 0.6f))]);
document = document with
{
    Children = document.Children.AddRange(new SceneNode[]
    {
        Scene.Model("left", slot, new(-1.6f, 0, -4)),
        Scene.Model("right", slot, new(1.6f, 0, -4)) with
        {
            Materials = ImmutableDictionary<string, ModelMaterial>.Empty.Add("panel", panel with
            {
                BaseColor = new(0.35f, 1, 0.45f, 1),
                Metallic = 0,
                Roughness = 0.9f,
            }),
        },
    }),
};
using var game = GameStarter.Create("asset-proof", GamePresets.FirstPersonShooter,
    saveDirectory: Path.Combine(output, "saves"), sceneDocument: document);
game.Activate("start");
SceneFrame frame = game.Scene.Project(agent => agent.Id != "player");
SceneAgent<Vector3> player = game.Scene.Agent<Vector3>("player");
string[] instanceIds = frame.Models.Select(item => item.Id).ToArray();
Matrix4x4[] placements = frame.Models.Select(item => item.WorldTransform).ToArray();

string shaderSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StaticModel3D.v.ts"));
var module = GpuGraphicsBinder.Compile(new([new("StaticModel3D.v.ts", shaderSource),
    new("Lighting3D.v.ts", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Lighting3D.v.ts")))]));
Require(module.Success, string.Join("; ", module.Diagnostics.Select(item => item.Message)));
var backend = VdMirGraphicsBackend.Compile(module, "vulkan1.2");
var shader = CompiledGraphicsProgramExporter.Export(module, backend);
File.WriteAllText(Path.Combine(output, "model.hlsl"), backend.Hlsl);
File.WriteAllBytes(Path.Combine(output, "model.vert.spv"), backend.Vertex.Spirv);
File.WriteAllBytes(Path.Combine(output, "model.frag.spv"), backend.Pixel.Spirv);
File.WriteAllText(Path.Combine(output, "model.frag.spvasm"), backend.Pixel.SpirvDisassembly);
var initialized = VulkanPlantInitializer.CreatePlant(PlantId.Zero, new(EnableValidation: true, ApplicationName: "Static GLB proof"));
Require(initialized.Success, string.Join("; ", initialized.Diagnostics.Select(item => item.Message)));
using var plant = initialized.Plant!;
using var target = new VulkanNativeFrameTarget(plant, 960, 600, VulkanTextureFormat.Rgba8Srgb);
using var renderer = new VulkanSolid3DRenderer(plant, new GameAssets().Shader("Solid3D.v.ts"), target, modelProgram: shader);
Matrix4x4 camera = game.Camera(1.6f);
Native3DFrameResult Capture(string name, SceneFrame scene)
{
    var result = renderer.Render(SceneGeometry3D.BuildScene(scene), camera, game.CameraEye, new(0.055f, 0.095f, 0.15f, 1), capture: true);
    NativeGameGraphics.WritePng(Path.Combine(output, name + ".png"), 960, 600, result.Pixels!);
    return result;
}
var plainFrame = frame with { Models = frame.Models.Select(item => item with { Materials = ImmutableDictionary<string, ModelMaterial>.Empty }).ToImmutableArray() };
Native3DFrameResult plain = Capture("original", plainFrame);
Native3DFrameResult customized = Capture("override", frame);
int leftChanges = Changed(plain.Pixels!, customized.Pixels!, left: true);
int rightChanges = Changed(plain.Pixels!, customized.Pixels!, left: false);
Require(leftChanges == 0 && rightChanges > 100, "Instance override did not independently affect the right model.");
var culledFrame = OverridePanel(plainFrame, panel with { DoubleSided = false });
Require(Capture("backface-culling", culledFrame).PixelSha256 == plain.PixelSha256,
    "glTF front-face winding or backface culling changed the visible faces of a closed cube.");
// A solid box in front must occlude both textured models in the same depth buffer.
SceneFrame occluded = plainFrame with
{
    Boxes = plainFrame.Boxes.Add(new("occluder", Matrix4x4.CreateTranslation(0, 1.4f, -2),
    new(4, 2, 0.1f), new(0.3f, 0.2f, 0.15f, 1), SceneCollision.None))
};
var cover = Capture("shared-depth", occluded);
Require(Capture("shared-depth-without-models", occluded with { Models = [] }).PixelSha256 == cover.PixelSha256,
    "Solid geometry and textured models did not share depth.");

// Change one factor/channel at a time through the same typed material and GPU draw path.
var checks = new List<ChannelProof>();
foreach (var change in new (string Name, ModelMaterial Material)[]
{
    ("metallic", panel with { Metallic = 1 }),
    ("roughness", panel with { Roughness = 0.05f }),
    ("emissive", panel with { Emissive = new(0.4f, 0.05f, 0.1f) }),
    ("occlusion", panel with { OcclusionTexture = Binding(0, 255, 255), OcclusionStrength = 1 }),
    ("normal", panel with { NormalTexture = Binding(220, 128, 220) }),
    ("unlit", panel with { Unlit = true }),
    ("mask", panel with { AlphaMask = true, BaseColor = new(1, 1, 1, 0.1f) }),
    ("metallic-roughness-texture", panel with { MetallicRoughnessTexture = Binding(255, 16, 255) }),
    ("emissive-texture", panel with { Emissive = new(0.4f, 0.05f, 0.1f), EmissiveTexture = Binding(0, 255, 0) }),
})
{
    SceneFrame variant = OverridePanel(plainFrame, change.Material);
    var rendered = Capture(change.Name, variant);
    int changed = Changed(plain.Pixels!, rendered.Pixels!, true) + Changed(plain.Pixels!, rendered.Pixels!, false);
    Require(changed > 100, "Material channel had no visible effect: " + change.Name);
    checks.Add(new(change.Name, changed, rendered.PixelSha256!));
}
var mapped = GlbModelImporter.Load("starter.crate", Path.Combine(source, "Models", "crate-channels.glb"));
Require(mapped.Success, ModelAssetCatalog.Describe(mapped.Diagnostics));
slot.Replace(mapped.Model!, renderer.Prepare);
var importedNormal = Capture("imported-normal-map", plainFrame);
Require(importedNormal.PixelSha256 != plain.PixelSha256, "Imported normal-map GLB did not change shading.");
checks.Add(new("imported-normal-map", Changed(plain.Pixels!, importedNormal.Pixels!, true)
    + Changed(plain.Pixels!, importedNormal.Pixels!, false), importedNormal.PixelSha256!));
slot.Replace(original, renderer.Prepare);

File.Copy(Path.Combine(source, "Models", "crate-replacement.glb"), livePath, true);
ModelImportResult replaced = catalog.Reimport("starter.crate", renderer.Prepare);
Require(replaced.Success && !ReferenceEquals(slot.Current, original), ModelAssetCatalog.Describe(replaced.Diagnostics));
Native3DFrameResult replacement = Capture("replacement", game.Scene.Project(agent => agent.Id != "player"));
Require(replacement.PixelSha256 != customized.PixelSha256, "Replacement did not alter rendered pixels.");
StaticModel accepted = slot.Current;
File.Copy(Path.Combine(source, "Models", "crate-slot-loss.glb"), livePath, true);
ModelImportResult lost = catalog.Reimport("starter.crate", renderer.Prepare);
Require(!lost.Success && ReferenceEquals(accepted, slot.Current), "Slot loss replaced the live model.");
File.WriteAllText(livePath, "broken candidate");
ModelImportResult broken = catalog.Reimport("starter.crate", renderer.Prepare);
Require(!broken.Success && ReferenceEquals(accepted, slot.Current), "Broken candidate replaced the live model.");
var retained = Capture("failed-replacement-retained", game.Scene.Project(agent => agent.Id != "player"));
Require(retained.PixelSha256 == replacement.PixelSha256, "Failed replacement changed the rendered image.");
SceneFrame after = game.Scene.Project();
Require(instanceIds.SequenceEqual(after.Models.Select(item => item.Id)) && placements.SequenceEqual(after.Models.Select(item => item.WorldTransform))
    && ReferenceEquals(player, game.Scene.Agent<Vector3>("player")), "Replacement altered placement or agent identity.");
Require(after.Models.All(item => ReferenceEquals(item.Asset, slot)), "Instances stopped sharing the stable asset slot.");
var report = new AssetProof("Success", plant.Facts.PhysicalDeviceName, GlbModelImporter.Version,
    original.ContentIdentity, accepted.ContentIdentity, original.Occurrences.Select(item => item.Primitive).Distinct().Count(),
    original.Occurrences.Select(item => item.Primitive.Material.Slot).Distinct().ToArray(),
    instanceIds, leftChanges, rightChanges, customized.PixelSha256!, replacement.PixelSha256!, retained.PixelSha256!,
    checks.ToArray(), lost.Diagnostics.ToArray(), broken.Diagnostics.ToArray(), true);
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(report, ProofJson.Default.AssetProof));
Console.WriteLine($"AURELIAN_STATIC_ASSET_PROOF_PASSED gpu={plant.Facts.PhysicalDeviceName} overridePixels={rightChanges} retained={retained.PixelSha256}");

static ModelTextureBinding Binding(byte red, byte green, byte blue) => new(new($"proof-{red}-{green}-{blue}", 1, 1,
    ImmutableArray.Create<byte>(red, green, blue, 255)), new());
static SceneFrame OverridePanel(SceneFrame frame, ModelMaterial material)
{
    return frame with
    {
        Models = frame.Models.Select(item => item with
        {
            Materials = ImmutableDictionary<string, ModelMaterial>.Empty.Add("panel", material),
        }).ToImmutableArray(),
    };
}
static int Changed(byte[] a, byte[] b, bool left)
{
    int changed = 0;
    for (int y = 0; y < 600; y++)
    {
        for (int x = left ? 0 : 480; x < (left ? 480 : 960); x++)
        {
            int offset = (y * 960 + x) * 4;
            if (!a.AsSpan(offset, 4).SequenceEqual(b.AsSpan(offset, 4))) changed++;
        }
    }
    return changed;
}
static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
public sealed record ChannelProof(string Name, int ChangedPixels, string PixelHash);
public sealed record AssetProof(string Outcome, string Gpu, string Importer, string OriginalIdentity, string ReplacementIdentity,
    int PrimitiveDefinitions, string[] MaterialSlots, string[] Instances, int UnmodifiedInstanceChangedPixels, int OverriddenInstanceChangedPixels,
    string OverrideHash, string ReplacementHash, string RetainedHash, ChannelProof[] Channels,
    Aurelian.Assets.AssetDiagnostic[] SlotLossDiagnostics, Aurelian.Assets.AssetDiagnostic[] BrokenDiagnostics, bool PlacementAndAgentsRetained);
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AssetProof))]
internal partial class ProofJson : JsonSerializerContext;
