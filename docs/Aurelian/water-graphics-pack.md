# Pond and pool water

The shared Vulkan surface renderer now supports horizontal water patches with
GPU wind waves, impact ripples, depth-dependent absorption, refraction, contact
foam, optional planar reflection and separate temporal water history. This is an
above-water pond/pool profile, with explicit authoring and resource bounds.

## Authoring

```csharp
using System.Numerics;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Models;
using Aurelian.World.Scenes;

var water = new WaterSurface3D
{
    FoamWidth = .12f,
    MaximumDepth = 8,
};
var patch = WaterSurfaceMesh3D.Create("pond", new Vector2(8, 6), water,
    cellsX: 96, cellsZ: 96, at: Vector3.Zero);
using var world = SceneCompiler.Compile(Scene.World("garden", [
    Scene.Box("bed", new Vector3(8, .2f, 6), Vector4.One, new Vector3(0, -1.5f, 0)),
    patch,
])).Mount();

// Supply presentation time and interaction events from the application's agent
// snapshot. Keep absolute event times on the same clock; there is no wall clock
// or hidden event queue in the renderer.
var frame = new WaterFrame3D(1.7f)
{
    Impulses = [new WaterImpulse3D(new Vector2(.2f, .3f), StartedAt: 1.2f)],
};
var scene = SceneGeometry3D.BuildScene(world.Project()) with
{
    WaterFrame = frame,
    WaterReflection = new PlanarWaterReflection3D(Height: 0)
    {
        ResolutionDivisor = 2,
    },
};
// graphics.Render(scene, camera, eye);
```

`WaterSurfaceMesh3D` generates an indexed regular patch once, with upward winding
and normals. The existing scene compiler owns placement, materials and identity;
water profile changes participate in scene content identity. Choose cell spacing
to resolve the shortest geometric wave. Fine normal ripples do not require finer
geometry. The patch defaults to no collision: this pack does not add buoyancy or
fluid physics.

`WaterSurface3D.Material()` supplies dielectric IOR 1.333, full transmission,
zero slab thickness and an absorption profile. Customize optical factors through
ordinary material `with` expressions. Keep the water profile in `ModelMaterial.Water`;
alpha blending, metals, double-sided water and slab thickness are rejected.

Four wind waves and eight impact events are the explicit limits. Wave directions
are normalized; collective steepness is bounded to prevent folding. Time is in
seconds, positions and wavelengths in metres, speed in metres/second. The bounded
presentation clock accepts 0..65,536 seconds. Expired events contribute zero and
can be removed by the author. Cancelling an active event, changing a past event,
rewinding time or toggling reflection invalidates temporal history.

## Rendering and ownership

`WaterGeometry.v.ts` evaluates Gerstner displacement and analytic surface
differentials. Impact rings have a smooth birth and finite lifetime. Small waves
contribute pixel normals. There are no per-frame CPU mesh deformations, simulation
readbacks, new shader-language extensions or inference dependencies.

The existing `RefractiveModel3D.v.ts` has an explicit water optical profile. Water
traces the refracted direction through opaque scene depth, using 32 bounded march
steps and six hit refinements. Path length drives Beer-Lambert absorption and a
simple authored deep-color scattering approximation. Depth-qualified rough
transmission taps account for the projected sample footprint. Foreground geometry
is rejected; missed rays use a valid undistorted submerged background when one is
visible, otherwise the authored deep-color fallback. The trace debug view keeps
these fallback pixels distinct from real screen-space ray hits.

Depth contact produces foam around shallow edges and visible intersections.
It is a surface shading effect, not a foam simulation. Existing direct lighting,
environment/probe reflection and Fresnel response remain shared with glass.

One optional planar reflection renders the opaque scene at half or quarter
resolution on the existing plant and renderer. An oblique near plane clips
submerged geometry on the GPU. Reversed triangle winding preserves facing under
the mirrored camera. The captured HDR surface output is sampled before tone
mapping; alpha identifies geometry and preserves environment fallback for sky.
Glass, alpha surfaces, water, particles and recursively reflected views are
excluded. Reflection GPU measurements aggregate the child renderer's measured
passes and exclude CPU preparation/submission.

Water shares the nearest refractive depth pass with glass, including opaque
occlusion. A third attachment stores previous projected position/depth from the
previous camera and wave time. Water owns separate color/depth history through
the existing temporal resolver. Glass retains reactive reconstruction. The final
compositor marks transport reactive so opaque history cannot be retained through
water. Removing water resets correspondence; no water history is sampled as an
opaque surface. `SurfaceLightingPrograms.WaterTemporal` supplies the shared
`TemporalResolve3D.v.ts` program; the game graphics wrapper includes it by default.

The refraction ABI is now 1,392 uniform bytes, 33 resources and three targets.
The renderer validates this contract; rebuild old externally compiled refractive
programs. The shader registry includes `WaterGeometry.v.ts` as a transparency
module. Shader code remains in source files.

## Inspection and qualification

`WaterSurface3D.DebugView` exposes shaded, depth, normals, contact foam, true
refraction hits and planar-reflection views. The proof scene saves all of them
before the animated/moving-camera sequence and the final pond preview.

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --water-graphics
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --transmission-graphics --output artifacts/local/water-transmission-regression
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -c Release -m:1
```

Ignored `artifacts/local/water-graphics/` contains native images and evidence.
The RTX 3070 witness enables `VK_LAYER_KHRONOS_validation`, renders an 18,432
triangle patch at 960x640 and checks real pixel changes for wind, impact, foam and
reflection. Expired-event output, cancelled-event history, reflection toggling and
water removal match their fresh/reference renders exactly. The camera moves
during the timed animation; this does not certify every disocclusion or view.

Independent flat-water fixtures at .25 m and 1.5 m depth compare the native output
against Snell refraction, Fresnel reflection and Beer-Lambert absorption plus the
authored scattering term. Maximum linear RGB vector error is **0.00292**, below
the **0.015** bound. The existing glass/volumetric native proof also passes.

The focused graphics proof build has zero warnings/errors. The full Aurelian regression passes
**1,398 tests in 31 assemblies**, with no failures or skips. New unit checks cover
water optical contracts, event bounds, nonfolding waves, indexed patch winding
and profile-owned scene identity; shader tests validate the updated SPIR-V ABI.
The full test build emits 14 existing unassigned-field warnings from the
third-party OpenFont project; no build errors occur.

With eight warmup frames and 32 measured frames, the final local run measures
**1.91 ms median / 4.43 ms p95** for water/refraction/temporal resolve, plus
**0.24 ms median / 0.79 ms p95** for the optional half-resolution reflection. These
are measured pass costs for this small scene, not a full-frame performance claim
or a guarantee across scenes/hardware.

The profile requires a perspective camera above a horizontal base patch. Planar
reflection supports coplanar patches at one authored height and scene batches,
not externally generated GPU geometry. Raw geometry authors must update scene
revision when geometry changes, as with other native presentation. Underwater
transport is explicitly rejected. Stacked liquids, hidden/off-screen refracted
geometry, caustics, an ocean spectrum, breaking waves and volumetric fluid physics
remain unsupported. No full water simulation or production certification is
claimed.

The original analytic implementation is informed by
[GPU Gems: Effective Water Simulation from Physical Models](https://developer.nvidia.com/gpugems/gpugems/part-i-natural-effects/chapter-1-effective-water-simulation-physical-models).
No external shader implementation was copied. Temporal policy retains its
existing Aetheris provenance and license boundary.
