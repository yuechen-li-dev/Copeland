# Environment lighting, ambient occlusion and local lights

The shared `NativeGameGraphics` renderer now owns these passes. Game starters,
static models, scene primitives and compute-skinned characters use the same surface
lighting path. All new GPU algorithms live in real Visual TypeScript source files
and compile through Copeland's binder, VD-MIR, HLSL, DXC and SPIR-V validation.

## Authoring and loading

Bake an equirectangular Radiance RGBE image once:

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --compile-environment --hdr Assets/studio.hdr --output Assets/studio.aenv
```

`GameAssets.CompileEnvironment(plant, hdrPath, artifactPath)` exposes the same
authoring operation in C#. It integrates on the supplied Vulkan plant, reads back
one compiled artifact and saves it. Runtime drawing performs no integration or
environment readback. `.aenv` stores the decoder ID, source hash, content checksum,
six octahedral GGX roughness bands, cosine-weighted diffuse and a split-sum BRDF
table. Each band is 64-square; the GPU compiler uses 1,024 deterministic samples.
The zero-roughness band samples the source directly. Compilation and decoding use
the same endpoint grid to keep directions aligned.

The importer supports modern RGBE RLE scanlines with `-Y height +X width`, up to
4096x2048. Legacy scanlines, other orientations, truncated input, unsupported
decoder IDs and corrupt payloads fail explicitly (`AUR-ENV-001`/`AUR-ENV-002`).
EXR and automatic cubemap capture are not implemented. RGBE data is treated as
linear radiance; no display transfer function is applied to it.

```csharp
using System.Numerics;
using Aurelian.Games;
using Aurelian.Rendering.Contracts.Models;

var options = new StarterOptions(Title: "My Game")
{
    EnvironmentAsset = "Assets/studio.aenv",
    Graphics = Graphics3DSettings.Default with
    {
        EnvironmentIntensity = 1,
        AmbientOcclusionStrength = 1,
        AmbientOcclusionRadius = .5f,
        LocalShadowBudget = 2,
    },
    LocalLights =
    [
        new(new(-2, 2, 0), new(1, .5f, .2f), 15, 5),
        new(new(2, 3, 1), new(.2f, .5f, 1), 35, 7)
        {
            Kind = LocalLightKind.Spot,
            Direction = Vector3.Normalize(new(-1, -2, -1)),
            InnerAngle = .25f,
            OuterAngle = .65f,
            CastShadows = true,
        },
    ],
};

GameStarter.Run("my-game", GamePresets.FirstPersonShooter, args, options);
```

Custom hosts set `NativeGameGraphics.Environment`, `ReflectionProbe` and
`LocalLights` between completed frames. Lights use scene metres, linear colors,
an inverse-square response and a smooth finite-range fade. Spot directions point
away from the light; inner/outer angles are cone half-angles in radians. An empty
environment retains the procedural sky/ground fallback. These settings stay outside
simulation identity and Deliverance save compatibility.

An optional `ReflectionProbe3D(Position, HalfSize)` performs box projection and
feathers the outer 20% of the volume toward the global direction. Its environment
must represent radiance captured at that origin; assigning an arbitrary HDR image
does not manufacture room-specific reflections. One volume is supported per
environment. Overlapping probe grids, dynamic captures and screen-space reflections
remain outside this pack.

## GPU execution and limits

The scene writes five shared attachments: base/roughness, previous UV/depth and
current depth, world normal/metallic, emission/material occlusion, and an explicit
subsurface profile. See [the presentation pack](presentation-graphics-pack.md)
for cascades, fog, weighted transparency and diffuse-only scattering. A retained
surface pass evaluates directional and local GGX lighting, environment response
and the existing validated compiled diffuse asset. TAA, HDR bloom, tone mapping
and the existing menu/HUD compositor follow. The compositor keeps UI out of history.

Half-resolution horizon AO uses eight directions and four samples per direction,
with a world-space radius, normal-aware bilateral filtering and depth-aware
upsampling. Reconstruction uses the exact full-resolution pixel that supplied the
depth; treating a nearest sample as an unsnapped half-resolution position caused
false self-occlusion on flat receivers and has been fixed. AO affects indirect
radiance, including compiled diffuse; direct light and emission are unchanged.
AO defaults to strength zero and skips its expensive sampling when disabled.
It is a bounded screen-space approximation, not a full XeGTAO/CACAO port; offscreen
occluders and thickness reconstruction are not available.

AO now averages its eight direction samples by eight and weights the horizon
with squared elevation, a tangent-horizon cosine-weight approximation. The earlier
divide-by-four and linear weighting produced an overly dark, broad wall-floor band.
This is still the bounded horizon implementation described above, not the full
view-slice integration from [the GTAO paper](https://www.activision.com/cdn/research/PracticalRealtimeStrategiesTRfinal.pdf).
For the studio witness, strength 1 and a 0.5-metre radius keep AO near contacts.
The engine's configurable default radius remains 0.8 metres and AO remains opt-in.

Set `Graphics3DSettings.SurfaceDebugView = SurfaceDebugView3D.AmbientOcclusion`
to inspect filtered, upsampled screen-space visibility. White means unoccluded;
the view automatically bypasses exposure, tone mapping, bloom and TAA. It excludes
material AO, light shadows and reflections. The surface proof also captures
`combined-no-ao.png`, `ao-only.png` and `ao-only-original-settings.png`.
The October 9 tuning witness increased wall-floor visibility at 5 cm from 0.416
to 0.789 with the same original strength/radius, isolating the shader correction.
With the scene's tuned settings, visibility is 0.852 at 5 cm, 0.976 at 30 cm and
1.0 at 50 cm. Native gates retain contact darkening, reject darkening outside the
radius, require disabled AO to return white, and verify that high exposure/bloom
cannot alter the AO inspection output.

GPU tiles conservatively project light influence spheres into 16-pixel cells.
Each cell stores four exact eight-bit mask words in RGBA32F. The supported contract
admits 32 local lights; `LocalLightCulling = false` supplies an uncullled reference.
Two retained 512-square spot shadow maps use existing geometry and GPU-skinned
casters. Both slots are tested. Requests beyond `LocalShadowBudget` fail with
`AUR-LIGHT-LOCAL-003`; point shadow requests fail with `AUR-LIGHT-LOCAL-002`.
Point cube shadows and alpha-masked shadow casters are not qualified here.

These are explicit Vulkan plant operations. Pipeline/resource creation stays in
the backend; game/asset contracts own authoring data. The canonical binder owns
the new scalar `Sin`, `Cos`, `Acos` and `Atan2` intrinsic typing. HLSL emission
hygienically renames local identifiers that coincide with its primitive modifiers.
Payload enums and exhaustive `match` express environment bake jobs, point/spot
attenuation, mask-word selection and shadow-source selection.

## Native qualification and the jagged-edge fix

```powershell
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --surface-graphics
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --temporal-graphics
```

The surface proof writes `artifacts/local/surface-graphics/evidence.json`, a saved
environment and separate environment/AO/light/probe/shadow images. On October 9,
2026, the native RTX 3070 run passed the constant-radiance invariant (maximum error
0.0000071), finite-range rejection, direct/emissive AO byte equality, isolated-flat
receiver AO, byte equality between culled/unculled 32-light images, visible box
projection, both spot shadow slots, and explicit shadow-budget rejection.

The screenshot's jagged spheres and desk revealed a temporal edge-classification
bug. Quad depth derivatives included the silhouette jump itself, so subtracting
them erased the discontinuity and suppressed stationary coverage accumulation.
The resolve now estimates surface slope from the gentler neighbor on each axis,
uses that for bounded depth tolerance, and separates discontinuities from slope.
No blur pass or higher geometry tessellation was needed.

The perspective proof renders unlit spheres and a desk in front of a nearby
backdrop, isolating coverage from BRDF differences. At 384x256, 371 partial-coverage
pixels were compared in linear RGB against a 4x supersampled reference. Edge MAE
fell from 0.098616 without AA to 0.030558 with warmed TAA: a 69.0% reduction.
The old detector reached only 19.2%. Perspective object and camera motion both
produced zero excess-history pixels at the stated 0.08 linear RGB threshold.
The previous native object/camera/compute-skinning regression also retained zero
excess-history trail pixels and exact camera-cut/mip-filtering acceptance.

The JSON records warmed GPU pass timings, excluding CPU work and capture. They
vary with GPU clocks and concurrent activity; these witnesses do not establish
cross-device performance or universal ghost freedom. Lighting maps remain finite
resolution, and mesh faceting is distinct from raster aliasing.

## Research and provenance

The independent IBL implementation follows the GGX/split-sum concepts described
in [Filament's PBR documentation](https://google.github.io/filament/Filament.html).
[XeGTAO](https://github.com/GameTechDev/XeGTAO) and
[AMD CACAO](https://gpuopen.com/fidelityfx-cacao/) informed the AO design review;
their implementations were not copied. The existing adapted Aetheris temporal
policy and its AGPL notices remain intact; see [temporal provenance](temporal-graphics-pack.md).
