# Static 3D assets

The default workflow is **Blender source â†’ self-contained glTF 2.0 GLB â†’ `assets.toml` â†’ typed scene instance**. Keep `.blend` files and source textures editable. Export GLBs for the game; no Blender installation is required to load or play them. Aetheris STEP/BRep remains CAD authority, and the existing Aetheris scene adapter remains available. Neither GLB nor its triangles replace CAD topology or automatically become collision.

The exchange conventions come from the [glTF 2.0 specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html). [SharpGLTF](https://github.com/vpenades/SharpGLTF) owns container/accessor decoding behind Aurelian's adapter; its object model never becomes the game's scene or agent API.

## Author and export

Use Blender's Principled BSDF metallic/roughness materials, unique material names, UV maps and image textures. Bake procedural textures before exporting. Work in metres; Blender's GLB exporter converts its Z-up scene into glTF's Y-up coordinates. Export **GLB**, normals, UVs and tangents, with animations disabled. Images must be embedded PNG/JPEG. The runtime preserves the selected default scene's node transforms and mesh reuse.

Name material slots deliberately (`panel`, `frame`, `wood`, etc.). Names are the override/replacement contract. Unnamed slots receive `material-N`; explicit names are preferable when re-exporting or rearranging materials. Duplicate names fail. Export positive, invertible transforms: reflected and singular nodes are diagnosed. Apply mirrored transforms in the authoring tool before export.

```toml
[[models]]
id = "props.crate"
path = "Models/crate.glb"
scale = 1.0
```

`id` is stable and independent of filenames. Paths are relative to the manifest, cannot escape through `..`, and use `/`. Scale is the only import adjustment in static.v1; units/axis guesses, implicit collision and unknown settings are rejected. Content identity includes the source bytes, scale and importer version.

## Compose with C#

Reference `Aurelian.Games` in the game's csproj and copy the manifest and GLBs into its output directory. Materials and presentation geometry live in renderer-neutral contracts; SharpGLTF and image decoding stay in `Aurelian.Assets`.

```csharp
using System.Collections.Immutable;
using System.Numerics;
using Aurelian.Assets.Models;
using Aurelian.Games;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

var assets = ModelAssetCatalog.Load(
    Path.Combine(AppContext.BaseDirectory, "Assets", "assets.toml"));
var crate = assets.Model("props.crate");
var panel = crate.Current.Occurrences
    .First(item => item.Primitive.Material.Slot == "panel").Primitive.Material;

var scene = Scene.World("range",
[
    Scene.Box("floor", new(24, 0.2f, 24), new(0.2f, 0.3f, 0.35f, 1),
        at: new(0, -0.1f, 0), collision: SceneCollision.Solid),
    Scene.Agent("player", new StarterPlayerDefinition(), at: new(0, 0, 7)),
    Scene.Model("original", crate, at: new(-1.6f, 0, -4)),
    Scene.Model("customized", crate, at: new(1.6f, 0, -4)) with
    {
        Materials = ImmutableDictionary<string, ModelMaterial>.Empty.Add(
            "panel", panel with
            {
                BaseColor = new(0.35f, 1, 0.45f, 1),
                Metallic = 0,
                Roughness = 0.9f,
            }),
    },
]);

GameStarter.Run("my-game", GamePresets.FirstPersonShooter, args,
    sceneDocument: scene);
```

`Scene.Model` composes inside groups, fragments and agent presentation bodies. Instances share a `ModelSlot` and retained indexed model definitions. Transforms and overrides belong to the instance. Override keys must exist, and textured/normal-mapped overrides require the corresponding UV/tangent attributes. Collision still uses separately authored scene geometry and `SceneSpatial3D`.

The current native adapter expands indices and transforms draw vertices per frame; GPU hardware instancing and retained object-space vertex/index draws are not implemented. Textures, samplers, material buffers and pipeline resources persist between frames. Obsolete resources are reclaimed after completed draws when a material is replaced or disappears.

## Replace explicitly

Re-export to the same manifest path, then reimport on the application's frame boundary:

```csharp
var result = assets.Reimport("props.crate", graphics.Renderer.Prepare);
if (!result.Success)
{
    Console.Error.WriteLine(ModelAssetCatalog.Describe(result.Diagnostics));
}
```

The full candidate is imported and validated before publication. The optional preparation callback uploads GPU material/texture resources before the slot changes. Malformed candidates, lost material names, lost UV/tangent capabilities and preparation failures retain the previous model. All instances observe the replacement through their stable slot; placements, overrides and agents remain intact. There are no implicit file watchers. Headless tools may omit GPU preparation.

Overrides replace a **whole material record**: `panel with { ... }` also keeps its existing texture bindings. A replacement updates unoverridden materials; an explicitly overridden material keeps its chosen textures until the application changes the override. Rename/remap operations require an explicit new scene declaration.

Scene identity records the initial imported content, placement and material overrides. Live visual replacement does not mutate the initial plan or game state. Rebuilding the scene from different assets produces a different identity, and Deliverance retains the existing save compatibility check. Scenes without model nodes retain the previous v2 identity format.

## Qualified rendering profile

| Feature | static.v1 behavior |
| --- | --- |
| Geometry | Static indexed triangles; default-scene hierarchy; NORMAL required; UV0; optional vertex color/tangent |
| Base color | Linear factor and vertex color; embedded texture sampled as sRGB |
| Metallic/roughness | Linear factors; texture G = roughness, B = metallic |
| Normal map | Linear RGB; exported tangent frame and normal scale |
| Emissive | Linear RGB factor; texture sampled as sRGB |
| Occlusion | Linear texture R and strength, applied to ambient lighting |
| Surface flags | OPAQUE, MASK/cutoff, BLEND/coverage, double-sided, `KHR_materials_unlit` |
| Lighting | One directional light, GGX/Schlick direct shading and simple ambient; no IBL, shadows or Blender render equivalence |
| Samplers | Independent nearest/linear min/mag and repeat/clamp/mirror addressing per channel |
| Mips | Base level only; requested mip filtering reports warning `AA3103` |

Textures use UV0 with no texture transform. Animation, skins, morphs, non-triangle primitives, other glTF extensions, external/data URIs and alternative texture encodings are rejected with actionable diagnostics. This is a bounded glTF profile, not a full glTF renderer. Import budgets are 128 MiB source, 256 MiB decoded textures, 8192-pixel texture dimensions and one million triangle-list vertices per model; native model draws also have a one-million-vertex frame budget.

Rendering goes through `StaticModel3D.v.ts` â†’ Copeland GPU binder â†’ VD-MIR â†’ Aurelian HLSL/DXC/SPIR-V. The qualified ABI is a 144-byte material, five vertex attributes and eleven descriptor bindings. Native game graphics uses the Vulkan 1.2 shader target so MASK uses core `OpKill` without requiring Vulkan 1.3's optional demote feature. GPU ray queries retain their existing separate path.

## Delivery and proof

```powershell
dotnet run --project src/Aurelian/Aurelian.AssetTool -c Release -- build-assets --manifest Games/Starter/Aurelian.Starter/Assets/assets.toml --output artifacts/aurelian-static-assets/delivery
starter.cmd --asset-demo
dotnet run --project tools/Aurelian.AssetProof -c Release -- Games/Starter/Aurelian.Starter/Assets artifacts/aurelian-static-assets
```

The asset tool validates models alongside existing shaders, writes delivery GLBs and source-generated `.import.json` evidence, and emits `models.toml` for loading that delivery. This packages an exchange artifact; it does not export arbitrary runtime scenes or reconstruct `.blend` sources.

The [Blender authoring script](../../tools/Aurelian.AssetProof/create-fixture.py) produces the editable crate source, base/replacement GLBs, a slot-loss specimen and an imported normal-map specimen. The [native proof manifest](../../artifacts/aurelian-static-assets/manifest.json) records real RTX 3070 rendering, independent instance overrides, material-channel pixel effects, replacement diagnostics and exact retention after failure. The executable also checks backface winding and solid/model depth occlusion. Starter playtesting exercises the actual window/presentation path with menus, controls, save/load and rewind.

BLEND uses the shared renderer's [weighted transparency path](presentation-graphics-pack.md). Transmission/refraction extensions remain unsupported; translucency does not imply physical glass.
