# Refraction and shadowed volumetric lighting

The shared surface renderer supports GPU-resident shadowed fog and a separate
refractive material pass. Both are available through `NativeGameGraphics`; games
author settings and materials using the existing records and scene composition.
Volumetrics default to disabled and transmission defaults to zero. Froxel storage
and the refractive framebuffer are allocated on first use, then retained.

```csharp
graphics.Settings = Graphics3DSettings.Default with
{
    Fog = new()
    {
        Density = .025f, // extinction per world metre
        HeightFalloff = .1f,
        MaximumDistance = 50,
    },
    Volumetrics = new()
    {
        Enabled = true,
        PixelSize = 8,
        DepthSlices = 64,
        MaximumDistance = 40,
        ScatteringAlbedo = new(.9f),
        AmbientRadiance = new(.015f),
        Anisotropy = .35f,
        Region = new(new(-5, 0, -5), new(5, 5, 6)),
    },
};

var glass = new ModelMaterial("glass")
{
    Metallic = 0,
    BaseColor = Vector4.One,
    Transmission = 1,
    IndexOfRefraction = 1.5f,
    Thickness = .2f,
    AttenuationColor = new(.2f, .7f, .95f),
    AttenuationDistance = .5f,
    Roughness = .08f,
};
```

Set these settings on the renderer or the existing game graphics settings seam.
The example uses world metres. `Thickness = 0` means a thin sheet with no lateral
refraction offset. `AttenuationDistance = 0` explicitly disables volume absorption.
Base color also tints transmission; attenuation controls additional thickness
dependent absorption. Transmission is independent of alpha blending. Metallic,
unlit, alpha-mask/blend and subsurface transmission combinations fail validation.

## GPU path and composition

`VolumeInject3D.v.ts` evaluates the existing exponential height density on a
camera-aligned logarithmic-depth grid. An optional world box restricts density.
Four deterministic samples per cell resolve density and shadow boundaries. Sun
lighting uses all three existing cascades, including overlap/fade; local lighting
uses the existing 32-light data texture and two shadowed spotlight slots.
Henyey-Greenstein phase controls scattering direction. Radiance is bounded before
storing half-float values.

`VolumeIntegrate3D.v.ts` integrates source radiance and Beer-Lambert transmission
front-to-back. The grid is packed in a 2D atlas using the existing Vulkan texture
and canonical Visual TypeScript resource contracts. No CPU lighting integration,
per-frame texture readback, ray-query hardware, or additional compiler profile is
required. These are explicit sequential graphics-queue passes; async compute and
multi-GPU overlap are not claimed.

`VolumeLighting.v.ts` reconstructs cumulative in-scattering and transmittance.
Sampling clamps within each atlas slice and interpolates optical depth between
slice endpoints. Opaque and alpha-blended surfaces sample the same volume at
their own depths. Analytic height fog remains the inexpensive selectable mode;
`Fog.Color` belongs to that mode. Volumetric source radiance instead comes from
lights plus `Volumetrics.AmbientRadiance` and `ScatteringAlbedo`.

`RefractiveModel3D.v.ts` has a dedicated depth-tested framebuffer. Each pixel
selects the nearest refractive surface, independently of draw order. Opaque depth
rejects hidden glass. The shader uses an authored parallel-slab exit displacement,
64 bounded depth-aware screen steps, a depth-guarded rough transmission filter,
IOR-dependent dielectric reflection, base-color tint and Beer-Lambert absorption.
`RefractionTraceDistance` bounds the screen search independently of fog distance.
Missing/off-screen opaque hits fall back to the compiled environment.

Glass samples **unfogged** opaque HDR. Background volume segments are reconstructed
from differences/ratios of cumulative scattering/transmittance, then the foreground
segment is composed once. Analytic mode integrates the corresponding segments
using the shared height-fog helper. This avoids double-counting the atmosphere.
Volume segments behind displaced glass approximate the original view rays; they
are not a general integral along a bent multi-medium path.

Pass order is opaque lighting, optional diffuse diffusion, atmosphere, nearest
refraction, alpha OIT, TAA, bloom and output. OIT preserves the glass reactive mask.
The existing temporal resolver reconstructs refractive coverage without retaining
incorrect opaque depth history. Changed lights, fog settings, material/asset keys
and scene lighting revisions invalidate retained shading. There is no separate
stochastic froxel history or probe-GI dependency.

`SceneGeometry3D` supplies `Native3DScene.LightingRevision` from scene geometry
identity and world transforms. This changes when a shadow caster moves while the
vertex-correspondence key remains stable. Authors of raw batches or animated GPU
geometry must update `LightingRevision` when shadow casters move/deform. The
invalidation is currently whole-frame, not regional.

## Asset import and supported boundaries

The default GLB importer admits the constant-factor subset of
[`KHR_materials_transmission`](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_transmission/README.md),
[`KHR_materials_volume`](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_volume/README.md),
and [`KHR_materials_ior`](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_ior/README.md).
SharpGLTF still owns parsing/accessors/container validation; its public material
channels supply optical factors. Base-color behavior was checked against the
[Khronos sample renderer](https://github.com/KhronosGroup/glTF-Sample-Renderer/blob/main/source/Renderer/shaders/ibl.glsl).
No external rendering implementation was copied.

Thickness incorporates uniform occurrence and import scale. Attenuation distance
incorporates import-unit conversion, and is independent of occurrence scale.
Material overrides continue to use explicit world metres. Unsupported transmission
or thickness textures produce `AA3111`; nonuniform scale/shear for a volume
produces `AA3110`. Infinite glTF attenuation distance maps to the explicit disabled
absorption sentinel. Non-default opaque IOR produces `AUR-REFRACT-003` rather than
being silently ignored; this slice admits authored IOR through transmission.
Import identity is `static.v2`; scene material content hashes
use `scene.v5` and include every optical factor.

This slice requires a perspective camera; an orthographic transport request
produces `AUR-TRANSPORT-002`. Analytic height fog retains its existing camera path.
It models one nearest refractive layer against opaque background. Stacked
glass, nested liquids, alpha-blended objects behind glass, actual closed-mesh exit
intersections, camera-inside-volume total internal reflection, caustics and colored
transmissive shadow maps are not modeled. Glass does not cast an opaque silhouette.
The authored slab approximation does not certify mesh manifoldness.

Fog is a geometry-shadowed single-scattering approximation. It omits medium
self-shadowing along light paths and multiple scattering. One density box plus
height fog is supported; arbitrary overlapping media are not yet authored.
Depth resolution limits very thin distant beams. Transparent/refractive edges
use reactive reconstruction and can shimmer; this is not accumulated glass TAA.

## Native qualification

```powershell
dotnet build Aurelian.slnx -m:1
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx --no-build -m:1 -- RunConfiguration.MaxCpuCount=1
dotnet test tests/Copeland/Copeland.TS.Tests --no-build -m:1
dotnet build tools/Aurelian.GraphicsProof -m:1
dotnet run --project tools/Aurelian.GraphicsProof --no-build -- --transmission-graphics --output artifacts/local/transmission-graphics
dotnet run --project tools/Aurelian.GraphicsProof --no-build -- --presentation-graphics --output artifacts/local/transmission-presentation-regression
```

The native proof requests Vulkan validation, records enabled layers, and writes captures plus
`evidence.json`, which starts unaccepted and is accepted only after all assertions.
It checks zero-density identity, a homogeneous Beer-Lambert reference, surface
tint, thickness absorption, IOR=1, roughness, normal maps, environment fallback,
opaque occlusion, nearest-layer order, and glass/fog composition. A black unlit
receiver isolates shadowed medium lighting from opaque surface shading. Moving
glass, moving the camera, moving a shadow caster, and switching a light off are compared against
fresh temporal history. The combined room contains sun through slats, a movable
spotlight, clear/tinted glass and a checkerboard.

GPU timestamp entries `atmosphere` and `refraction` report their complete native
pass groups. They exclude CPU uploads, fences, initialization and capture. At
960x640 with 8-pixel cells and 64 slices, the two half-float froxel atlases occupy
9.375 MiB, in addition to screen targets. Actual timings are in the evidence;
they are measurements of this machine and scene, not a hardware-independent
performance guarantee.

The froxel approach follows the general technique described in
[Wronski's volumetric fog presentation](https://bartwronski.com/wp-content/uploads/2014/08/bwronski_volumetric_fog_siggraph2014.pdf).
This implementation reuses Aurelian's existing plants, shaders, shadow maps,
material batching and temporal system.
