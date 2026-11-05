# R4: retained dynamic diffuse lighting

The ordinary retained surface renderer now supports a world-space diffuse cache
on the same instanced TLAS as R3 local-light visibility. Prepared probes capture
one indirect bounce from local lights and emissive surfaces. A compiled field
can supply the initial directional radiance; current signed updates subtract
occluded light and add relighting without another bake.

This documents the first qualified R4 slice. Its original profile used opaque
constant materials, flat triangle normals and analytic surfaces. The subsequent
[textured and smooth-normal secondary-hit pass](rendering-r4-secondary-materials.md)
extends that profile through shared resident geometry/material tables, including
an imported GLB witness. Local and emissive sources, a bounded prepared grid and
diffuse receivers describe that original scope. The subsequent
[sun/environment pass](rendering-r4-sun-environment.md) adds source-radiance
sampling, sky visibility and coverage fallback, with HDR/FLIP qualification.
The [regional policy](rendering-r4-regional-policy.md) and
[whole-Beacon report](rendering-r4-beacon.md) document subsequent selection and
arena-scale work. Recursive bounces and sparse allocation remain open. Dynamic
diffuse lighting defaults off globally; Beacon's explicit GI option selects
full-runtime updates from its measured controls.
The GI-specific transport specimens cover point lights and emissive geometry;
spot/area GI qualification remains outstanding.

## Authoring and compiled fields

Configure `SurfaceLightingPrograms.DynamicDiffuse` with the compiled
`DynamicDiffuse3D.v.ts` compute program, alongside the existing R3 programs. The
native proof's `RenderingBindlessProof.CreateRenderer` shows the complete setup.
The surface resolve retains its 16-texture ABI: 624 uniform bytes and 33 resources.
The mutually exclusive legacy floor field and dynamic field share one input.

```csharp
renderer.EnableRayShadows = true;
renderer.Settings = renderer.Settings with
{
    SunIntensity = 0,
    SkyAmbient = Vector3.Zero,
    GroundAmbient = Vector3.Zero,
    AmbientOcclusionStrength = 0,
};
renderer.DynamicDiffuse = new DynamicDiffuseLighting3D
{
    Mode = DynamicDiffuseMode3D.Runtime,
    Grid = new DiffuseProbeGrid(
        new(-1.8f, .2f, -1.8f), new(1.8f, 2.6f, 1.8f),
        X: 5, Y: 4, Z: 5, RaysPerProbe: 128),
    RaysPerProbePerFrame = 16,
    ConnectivityVisibility = true,
};
```

Use a resident `NativeInstanceSet3D` scene and the existing opaque shared-ray
profile. The original qualification disabled sun, environment, hemisphere fill
and the legacy floor field. The subsequent
[sun/environment pass](rendering-r4-sun-environment.md) admits sun and source
radiance; unshadowed hemisphere fill and the legacy floor field remain excluded.
The tested receiver model is Lambert. Other direct lobes still use their shared secondary BRDF, while the
receiver's low-frequency response remains diffuse E/pi with the conventional
dielectric factor. Indirect specular belongs to later work.

To acquire a compiled field, render a stationary scene in runtime mode until
every direction has been updated. With 128 directions and 16 updates per frame,
that takes eight frames. `CaptureCompiledDiffuseProbes()` performs an explicit
synchronous authoring readback. Incomplete coverage, or a scene/light change
without subsequent complete coverage, raises `AUR-GI-004`. An author can instead
set the per-frame budget to 128 and capture after one stationary frame.

```csharp
var field = renderer.CaptureCompiledDiffuseProbes();
var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
File.WriteAllText(path, JsonSerializer.Serialize(field, jsonOptions));

var loaded = JsonSerializer.Deserialize<CompiledDiffuseProbeField3D>(
    File.ReadAllText(path), jsonOptions)!;
renderer.SetCompiledDiffuseProbes(loaded);
renderer.DynamicDiffuse = renderer.DynamicDiffuse with
{
    Mode = DynamicDiffuseMode3D.CompiledResidual,
};
```

`IncludeFields` preserves `Vector3` bounds in this JSON example. The versioned
payload contains fixed-direction RGB radiance/backface flags, followed by 8x8
octahedral E/pi coefficients/backface ratios for each probe. Loading validates
dimensions and finite values and copies the caller's array. `CompiledOnly` is a
stale-lighting control. Runtime mode needs no compiled field.

The caller owns the meaning of a baseline: it must have the intended stationary
geometry, materials and lighting. The field is not automatically matched to a
scene content hash. A changed scene can legitimately use the old field as its
initial state while updates replace the old samples.

## What executes each frame

1. The existing R3 scene owner prepares/refits the current TLAS. A frame-slot
   shading table supplies constant BRDF factors, normal transforms and a shared
   mesh-local triangle atlas. `RayQueryTraceRetained` returns packed instance and
   subprimitive identity; procedural hits use the shared affine evaluator.
2. Each probe updates a rotating subset of fixed spherical directions. A
   coprime permutation spreads each subset over the sphere. Closest hits evaluate
   emission and a uniformly selected local light through the shared BRDF/emitter
   sampler, with current shadow visibility. Unvisited directions retain their
   cached values. Cold runtime estimates normalize over visited directions.
3. A compute phase convolves the directional cache into 8x8 octahedral E/pi.
   Backface-heavy probes are excluded. A final phase reconstructs current visible
   receivers from the G-buffer and interpolates eight grid neighbors.
4. Current receiver-to-probe occlusion queries reject connections through opaque
   geometry. The result is added by `SurfaceResolve3D`, including its diffuse
   output. All three phases record on the ordinary frame command buffer, with
   queue-ordered barriers and retained GPU buffers. There is no per-frame CPU ray
   generation, GI readback, separate tracing submission or second runtime TLAS.

In compiled residual mode, a visited direction stores
`currentRadiance - compiledRadiance`. The convolution combines baseline plus
signed corrections; clamping happens only at the final receiver output. This is
a directional replacement cache with a compiled warm start. Unvisited directions
can remain stale for one update cycle; this does not implement an unbiased
Monte Carlo residual estimator or recursive transport. The finite directional
basis also retains quadrature and one-sample local-light errors after warm-up.

The original admitted grid had 2..8 probes per axis. The whole-Beacon profile
now admits 2..16 per axis with an explicit 512-total-probe cap, and 16..128 directions per probe, with
an 8x8 convolution tile. The proof uses 100 probes, 128 directions and 16 updates
per probe per frame: 1,600 primary queries, at most 1,600 secondary-light shadow
queries, and at most eight connectivity queries per covered screen pixel.

## Qualification

Run the full chained qualification from the repository root:

```powershell
./tools/aurelian-rendering-gi/Qualify.ps1 `
    -Output artifacts/local/rendering-gi/final
```

It builds `Aurelian.slnx`, runs its tests and the Copeland TS compiler tests,
then runs reference/affine/temporal/hybrid/diffuse, exact R3, sampling, ReSTIR,
TLAS, raster-motion, area/tail and R4 native qualifications with synchronization
validation. Source and binary hashes are pinned. FLIP uses the existing pinned
NVIDIA evaluator over native, tone-mapped sRGB output. `qualification.json`
remains unaccepted until every required lane passes.

The accepted run passed 3,371 tests (1,641 solution tests and 1,730 compiler
tests), with zero failures or skips, and all required native/FLIP lanes passed
with clean Vulkan synchronization validation. Its 106 source hashes and the
proof/graphics binary hashes match the checked-out implementation. All 648
matching PNG artifacts from the previous area/tail qualification are byte
identical; `legacy-capture-comparison.json` records that comparison. The aborted
pre-cleanup qualification remains in `aborted-extra-texture/` and is unaccepted.

The R4 proof uses an independent two-bounce progressive reference with 2,048
samples per pixel for a red-wall room, moving opaque blocker and emissive-only
room. A separate 1,024-sample reference checks the thin-divider receiver region.
The measurements below are from the RTX 3070 qualification:

| Case | Control HDR RMSE | Dynamic GI HDR RMSE | Mean FLIP control → GI |
| --- | ---: | ---: | ---: |
| Static local-lit room | 0.190386, direct only | 0.034361 | 0.149607 → 0.043204 |
| Moving occluder | 0.215723, stale compiled field | 0.078204 | 0.313011 → 0.043285 |
| Emissive-only room | 0.139055, direct only | 0.050494 | 0.284352 → 0.077512 |

The warmed runtime and compiled residual outputs match exactly in the captured
HDR surface output. The compiled baseline removes cold-start error; it does not
improve the final warmed finite basis. A fresh runtime cache is a strong
negative result at the initial 16-direction budget: its first-frame RMSE is
0.308766, improving to 0.034361 after eight updates. This low-budget startup can
temporarily be worse than direct-only lighting. The compiled variant starts at
the warmed result. Positive and negative cache corrections are read back and
asserted, and changing global emission intensity by two scales secondary
irradiance exactly by two.

Across a 2.5 cm divider, current connectivity reduces mean leaked irradiance in
the tested dark-side floor region from 0.026086 to zero. All 136 selected floor
receivers retain valid probe connections. The independent reference error also
improves there. This is a bounded leak specimen, not a guarantee for arbitrary
probe placement. Disabling connectivity preserves the leaking negative control.

At 720p the measured full frame costs approximately 2.67 ms GPU with GI off and
3.38 ms with runtime or compiled residual GI, an additional 0.71–0.72 ms.
Compiled-only lookup has
similar cost: screen-space connectivity dominates this small scene. Compiled
lighting therefore earns its current role through initialization and stability,
not a demonstrated steady-state speedup. Full-frame CPU prepare/submit stays
near a millisecond. Detailed per-mode timing, startup curves, signed correction
energy, TLAS statistics, JSON round-trip evidence and 360p/720p captures are in
`artifacts/local/rendering-gi/final/gi/`.

## Research conclusions and next seam

The [DDGI visibility-aware probe work](https://jcgt.org/published/0008/02/01/)
and [production probe extensions](https://jcgt.org/published/0010/02/01/)
motivate a world-space irradiance representation. This experiment replaces
moment-based connection visibility with current opaque ray queries. That choice
has a measured cost and a falsifiable leak control; it does not inherit a DDGI
production qualification claim.

The signed compiled/current decomposition follows the variance-reduction intent
of [control variates](https://www.pbr-book.org/4ed/Monte_Carlo_Integration/Improving_Efficiency),
but this implementation's directional cache converges to the same finite basis
as the warmed runtime control. Its useful result is subtraction of stale light,
immediate compiled startup, and shared-scene integration. A per-region
freshness/error controller remains a next seam before choosing GI defaults or
claiming whole-scene R4 completion. The subsequent
[secondary-material pass](rendering-r4-secondary-materials.md) now supplies the
textured and smooth-normal payload through the existing material owner.

The subsequent [regional-policy qualification](rendering-r4-regional-policy.md)
adds Dominatus hysteresis/minCommit, per-region directional cursors and delayed
GPU change feedback on this same retained cache. Whole-Beacon remains open.

[Whole-Beacon qualification](rendering-r4-beacon.md) now exercises the complete
rigid game scene and records the runtime-versus-regional selection.
