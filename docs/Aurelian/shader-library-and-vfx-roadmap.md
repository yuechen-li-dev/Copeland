# Shader library and next VFX pack

## Ownership

The production library contains 65 Visual TypeScript sources in 13 families:
36 graphics entries, 17 compute entries and 12 imported helper modules.
Source files live under `src/Aurelian/Aurelian.Shaders/Assets/<Family>/`.
This organization preserves their bytes, flat compiler module IDs and existing
game shader names.

| Owner | Responsibility |
| --- | --- |
| `Aurelian.Shaders` | Compiler and backend; intrinsic lowering helpers, no built-in Visual TypeScript sources |
| `Aurelian.Shaders.Builtins` | Explicit source catalog, embedding, source copies and notices |
| `Aurelian.Games.GameAssets` | Compiled program cache and game-facing convenience API |
| Rendering contracts / Vulkan renderer | Typed pass inputs, resource lifetime, dispatch and scheduling |
| Optional `Aurelian.Lighting.Aetheris` integration | Aetheris field adaptation; accepts a caller-owned source reader |
| `tools/*/Assets` | Proof and experimental sources; not automatically production capabilities |

Games no longer maintain an independent embedded shader list. The built-in
library has no Aetheris project dependency. It is a separate AGPL source bundle
because it includes the adapted Aetheris temporal policy; the compiler remains
independent. The bundle's NuGet artifact supplies shader/catalog sources and
license notices. Project-reference application outputs receive flat source
copies and notices; the NuGet archive retains the grouped authoring sources.
The compiler's generated SPIR-V and browser WGSL paths require their own backend
qualification; this consolidation qualifies the Vulkan path.

The [complete real-image DINO encoder experiment](trellis2-image-encoder-experiment.md)
qualifies FP32 attention with 64-wide heads/query tails, prefix-aware 2D RoPE,
scaled residuals and erf GELU through the built-in compute sources. The same
plant also executes one full-lattice TRELLIS block with actual image features.
The full flow/sampler and dense occupancy decoder remain unimplemented; this
does not qualify image-to-mesh generation.

## Authoring and consumption

```csharp
using Aurelian.Shaders.Library;

var entry = BuiltinShaderLibrary.Get("SurfaceResolve3D.v.ts");
Console.WriteLine(entry.SourcePath); // Assets/Surfaces/SurfaceResolve3D.v.ts
var sources = BuiltinShaderLibrary.LoadSources(entry.Name);
// Pass this immutable import closure to the existing graphics binder/backend.

var assets = new Aurelian.Games.GameAssets();
var surfaceProgram = assets.Shader(entry.Name);
var rayQueries = assets.ComputeShader("RayQuery.v.ts");
```

The catalog maps repository paths to the existing flat module namespace. An
import such as `./Lighting3D` uses that namespace, not the source file's physical
folder. Runtime loading uses the owning assembly, with no repository probing.
Copied `Assets/<name>.v.ts` files keep existing file-based proofs and overrides
working. Unknown entries, missing resources and wrong pipeline kinds have
`AUR-SHADER-LIBRARY-001`, `002` and `003` diagnostics respectively.

To add a built-in, write an ordinary `.v.ts` file in its family and register its
name, family and kind in `BuiltinShaderLibrary`. Keep reusable helpers as Module
entries. Imports are resolved by the existing `GpuSourceLoader`; there is no new
resolver or discovery convention. Add behavioral/native evidence for new
effects, beyond the catalog's automatic compilation checks. Changes to source
or compile-time specialization still require rebuilding any dependent baked
artifact; this reorganization itself leaves shader source hashes unchanged.

## Current graphics baseline

The engine already has PBR surface resolve, emissive and normal maps, environment
prefiltering, light tiles, directional/field shadows, AO and denoising, compiled
diffuse lighting and probe experiments, TAA, bloom and tone mapping. Height fog,
volumetric shafts, transparency, refraction and subsurface diffusion also have
renderer paths. Those are foundations with bounded tests, not a claim of parity
with production engines across arbitrary scenes or hardware.

The initial audit found reusable emitters and particles in **Effects2D**. The
[GPU VFX foundation](gpu-vfx-foundation.md) now supplies 3D billboard particles
for fire, smoke and sparks. Ribbons, mesh particles and decals remain future
work. `Graphics3DSettings` exposes manual
exposure and Shaded/AmbientOcclusion debug views; automatic exposure is another
gap. `ModelMaterial` has no authored clearcoat, anisotropy or fabric sheen.
Existing Vulkan timestamps already measure named passes. Extend them to new
passes instead of creating another profiler.

## Recommended implementation order

This table describes the full proposed packs. Pack 1 now has a bounded billboard
foundation; the remaining items are not all shipped capabilities.

| Pack | Concrete capability | Qualification |
| --- | --- | --- |
| 1. GPU 3D VFX | Bounded particle pools; billboard/mesh emitters and ribbons; spawn/update compute; depth-softened intersections; emissive lighting integration | Agent command replay and seed stability, overflow diagnostics, correct fog/depth/temporal integration, native captures and GPU timings at declared budgets |
| 2. Decals | Projected albedo/normal/roughness marks with explicit receiving surfaces and lifetime | Bullet/scorch/wetness specimens; receiver filtering; overlap and grazing-angle checks; moving-camera TAA checks; screen-coverage cost |
| 3. Exposure and color | GPU luminance histogram, bounded EV adaptation, metering mask, manual override and authored grading | Dark-room-to-sun transition with no single-flash pumping; reset on camera cuts; histogram/debug view and measured GPU cost |
| 4. Material richness | Clearcoat, anisotropy and fabric sheen with explicit material inputs | Neutral light-rig comparisons, energy checks, tangent conventions and imports that preserve authored values |

Start pack 1 with sparks, dust and muzzle flashes in one existing scene. Add
smoke and trails once transparency, temporal response and depth intersections
are qualified. Use the existing transmission/fog renderer seams; avoid a
parallel effect renderer. A render-pass audit should decide exactly where each
effect enters HDR lighting, fog, transparency and TAA.

Dominatus agents should own emitter identity, seed, lifetime, activation and
semantic events. The GPU should own the bounded visual particle population and
its update/draw work. Rewind can reset and replay visual commands; exact
cross-device floating-point replay should not be promised. Visual collisions
can use shared query infrastructure where appropriate; gameplay collision
truth remains with its existing owner. Inspectability should report budgets,
overflow, seed and pass cost without reading every particle back each frame.

This split is consistent with the practical distinction between CPU system
control and GPU particle simulation described in [Epic's Niagara scalability
guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/scalability-and-best-practices-for-niagara).
For decals, explicit receiver/channel response and screen coverage are useful
design constraints from [Epic's decal documentation](https://dev.epicgames.com/documentation/unreal-engine/decal-materials-in-unreal-engine).
Exposure metering and adaptation should be explicit authored settings, drawing
on [Epic's exposure documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/auto-exposure-eye-adaptation?application_version=4.27).

Motion blur and depth of field can follow as opt-in presentation effects. A
reflection expansion should have a motivating scene and measured quality/cost
comparison against the existing environment/probe solution. Larger content
budgets will also require separate visibility/LOD/streaming qualification; more
shader effects alone cannot establish a production engine's scale.

## Reproduction

Run from the repository root with the existing Naga validator available:

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet build Aurelian.slnx -c Release -m:1
dotnet test Aurelian.slnx -c Release --no-build -m:1
dotnet pack src/Aurelian/Aurelian.Shaders.Builtins -c Release --no-build -o artifacts/local/shader-library/package
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --surface-graphics --output artifacts/local/shader-library/surface
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --transmission-graphics --output artifacts/local/shader-library/transmission
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --temporal-graphics --output artifacts/local/shader-library/temporal
```

The catalog tests compile all 51 entry points through the real binder, DXC and
SPIR-V validation, inspect the packaged resource inventory and exercise the game
cache. Native proofs separately exercise rendering and image-quality checks.
Local qualification logs and captures are under `artifacts/local/shader-library`.

Initial consolidation qualification on 2026-10-11: the Release Aurelian solution build passed, and all
1,262 tests across 30 test assemblies passed with no skips. All 48 moved shader
files retained their SHA-256 hashes; the NuGet archive's 48 source files matched
those hashes and included the catalog source and license notices. Surface,
transmission and temporal native proofs accepted on an NVIDIA GeForce RTX 3070.
The compiler NuGet archive was also checked: it contains no built-in shader
sources, temporal notices or dependency on the built-in bundle.
The surface proof also passed from a fresh publish directory, whose 48 shader
source copies and temporal notices were verified. The separate AssetProof tool
build passed with existing third-party font warnings. Other hardware, browser
execution and standalone NuGet-consumer publishing were not qualified here.
