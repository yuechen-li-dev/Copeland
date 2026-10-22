# Procedural surfaces and small model primitives

`PrimitiveModels3D` authors small meshes with UVs and tangent frames;
`SurfaceMapBaker` generates coherent material channels. Both feed ordinary
`ModelSlot` / `ModelMaterial` records through the existing `Scene.Model` compiler
and renderer. Neither facility depends on Aetheris.

Use them for planes, blocks, rounded edges, pipes, cones, spheres and revolved
profiles. Keep complex CAD in Aetheris and finish authored assets in Blender.
This is mesh authoring: no BRep, Booleans, constraints, automatic collision,
topology naming, LOD system or model editor is introduced.

## Generate maps

```csharp
using System.Numerics;
using Aurelian.Assets.Materials;
using Aurelian.Rendering.Contracts.Models;

SurfaceMapSet asphalt = ProceduralSurfaceMaps.Asphalt("courtyard", wetness: .85f, seed: 733);
SurfaceMapSet brick = ProceduralSurfaceMaps.Masonry("brick", seed: 53);
SurfaceMapSet paving = ProceduralSurfaceMaps.Pavers("paving", seed: 71);
ModelMaterial roadMaterial = asphalt.Material("road");
SurfaceMapFiles.Write("artifacts/local/my-materials", "asphalt", asphalt);

var waves = SurfaceMapBaker.Bake("ripple-test", 256, 256, Vector2.One, uv =>
    new SurfaceSample(
        HeightMetres: .002f * MathF.Sin(MathF.Tau * uv.X * 8),
        BaseColor: new Vector3(.08f, .09f, .10f),
        Roughness: .35f));
```

Bake once during authoring and share the immutable textures. The callback receives
UVs at texel centres and must be periodic. Wrapped central differences use the
physical tile size to derive normal slopes; they do not make arbitrary
nonperiodic input seamless. Inputs are linear, height is in metres, and samples
are validated before maps are returned.

| Output | Encoding and use |
| --- | --- |
| Base color | Linear sample colors encoded to sRGB bytes; decoded as color by the renderer. |
| Normal | Linear tangent-space XYZ in RGB, with +Y green along the mesh bitangent. |
| ORM | Linear R = cavity approximation, G = roughness, B = metallic; shared by the occlusion and metallic/roughness bindings. |
| Height | Normalized linear grayscale plus JSON minimum/range for reconstruction. Preview/export only; no displacement or parallax. |

Cavity is an authored approximation of ambient occlusion, not a ray-traced bake.
Wet asphalt changes albedo and roughness; it does not simulate a layered water
film. The recipes derive relief and material response from coherent seeded fields,
rather than painting unrelated random RGB normals.

The baker accepts 8..1024 texels per axis and .01..1000 metre tiles. Invalid and
nonfinite samples fail before publication. Texture IDs include dimensions and
SHA-256 byte hashes. Export validates all images before unmanaged copying/writing.

Generate the demo recipes without opening a GPU device:

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --surface-maps --output artifacts/local/surface-maps
```

The `materials/` directory contains base-color, normal, ORM and height PNGs plus
physical-scale/channel metadata for asphalt, masonry and pavers. This command
creates no Vulkan plant or window. The reusable API is in `Aurelian.Assets.Materials`.

## Author primitives

```csharp
using Aurelian.World.Scenes;

var metal = new ModelMaterial("iron") { Metallic = .7f, Roughness = .35f };
var stone = paving.Material("stone");
var road = PrimitiveModels3D.Plane("road-mesh", new Vector2(12, 24), roadMaterial,
    uvTileSizeMetres: asphalt.TileSizeMetres);
var column = PrimitiveModels3D.Cylinder("column", .12f, 3, stone);
var roof = PrimitiveModels3D.Cylinder("roof", .4f, .3f, metal, topRadius: .03f);
var finial = PrimitiveModels3D.Sphere("finial", .08f, metal);
var step = PrimitiveModels3D.BeveledBox("step", new Vector3(2, .2f, .6f), .025f, stone);
var post = PrimitiveModels3D.Revolve("post",
[
    new Vector2(.15f, 0),
    new Vector2(.15f, .12f),
    new Vector2(.06f, .25f),
    new Vector2(.035f, 2.8f),
], metal);

var scene = Scene.World("courtyard",
[
    Scene.Model("road", road, new Vector3(0, 0, -6)),
    Scene.Model("post-a", post, new Vector3(-3, 0, 0)),
    Scene.Model("post-b", post, new Vector3(-3, 0, -5)),
]);
```

Factories return shareable `ModelSlot` assets. Dimensions are metres; planes face
+Y and radial shapes revolve around Y. Profile points are `(radius, height)` with
increasing heights and hard normals at corners. Caps are optional. Sphere UVs
have the usual polar distortion. Box and rounded-box normals/tangents are explicit.

UVs default to one tile per local metre. Pass `uvTileSizeMetres` to match the
baker's physical tile extent. Radial shapes have a UV seam; a U tile size that
divides the maximum circumference gives a periodic repeat around that seam.
Tapered/revolved profiles compress U at smaller radii; sphere latitude mapping
also changes texel density toward the poles.
Instance scaling stretches UVs, so use intended dimensions in the factory
when physical texel scale matters. No inferred tangent fallback is added to
`Scene.Box` / `Scene.Mesh`; they retain their normal-map rejection. Content identity
reuses the scene owner's material hashing, including texture bytes.

Dimensions are bounded to .0001..1000 metres. Revolve has 2..64 points and 3..128
radial segments; spheres have 2..64 rings. Rounded boxes accept 1..8 segments per
half-edge strip and a bevel below every half extent. Choose tessellation for the
projected size; the rain gallery uses one strip for tiny trim radii. These are
presentation meshes; collision is separately authored.

## Aetheris and Blender handoff

1. Keep exact geometry and parametric source in Aetheris. Its current CLI exposes
   `aetheris scene export-usd scene.firmament out.usda` and
   `aetheris scene export-glb scene.firmament out.glb`. Assemblies have corresponding
   `aetheris asm export-usd` / `export-glb` commands.
2. Import into Blender for UVs, baking, organic detail and material finishing.
   Keep `.blend`, source textures and Aetheris source editable. USDA is an authoring
   handoff; it is not an Aurelian runtime input.
3. Use Principled BSDF, tangent-space Normal Map nodes, and Non-Color image data for
   normal/ORM/height. Bake procedural nodes into images before GLB export. Use the
   generated JSON tile size when mapping textures.
   [Blender glTF documentation](https://docs.blender.org/manual/en/3.6/addons/import_export/scene_gltf2.html)
4. Export a static self-contained GLB with embedded PNG/JPEG, UV0, normals, tangents
   and stable material names. Load it through `assets.toml` / `ModelAssetCatalog`
   and place it with `Scene.Model`; see [static assets](static-assets.md).

The inspected Aetheris USDA exporter declares Z-up and `metersPerUnit = .001`.
Its GLB exporter applies a root conversion to Y-up metres:
`(x, y, z) -> .001 * (x, z, -y)`. Verify a one-metre control after import and avoid
double conversion. Display exports may lack UVs/tangents needed for normal maps;
Blender supplies that finishing stage. USD import supports a subset of the format,
so inspect the intended geometry and materials after import.
[Blender USD documentation](https://docs.blender.org/manual/en/4.4/files/import_export/usd.html)

CLI names and conversion were checked against current sibling source. This slice
does not certify a new end-to-end Aetheris/Blender asset. It preserves the existing
GLB importer and core dependency boundary, with no new USD parser or CAD engine.

## Native qualification and limits

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --rain-night --output artifacts/local/rain-surfaces
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --rain-night --view
```

The rain gallery uses mapped masonry/pavers/asphalt, rounded trim, cylindrical
downpipes, revolved lantern posts, cone roofs and sphere finials. **N** toggles
normal maps and resets temporal history. R/P/Space/Esc retain their previous roles.

The proof compares identical native renders with normals removed and with
constant roughness, and records cavity removal as another control. Rain, replay,
rewind, drain, reflection, ripple, non-emission and GPU-only-pool checks remain.
Final previews and the explicit window smoke use temporal AA.

Tests check winding, closed welded edges, tangent handedness, UV scale, caps/seams,
bounds, identity and invalid inputs. Surface tests compare normal vectors with
independent periodic derivatives and check color/data encoding, channel packing,
seed replay, wetness response and lossless data PNGs.

The local RTX 3070 witness at 1280x800 passed with 76,116 normal-map-changed pixels,
306,427 roughness-map-changed pixels and 7,636 cavity-map-changed pixels. The
roughness control removes only the texture and preserves its scalar factors.
The scene has 73,210 model triangles, including the water patches, and 325 authored
model instances. The offline export and twelve-frame native window smoke passed.
The full Release solution build had zero warnings/errors, and all **1,510 tests
in 33 assemblies** passed with zero failures or skips.

Geometry is more expensive than the original box gallery. The current renderer
expands model triangles; hardware instancing and automatic LOD remain absent.
Normal maps preserve silhouettes and do not provide geometric self-shadowing.
Base-level texture sampling and temporal-AA limitations also remain unchanged.
