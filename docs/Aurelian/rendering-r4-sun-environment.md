# R4: sun and environment bounce

The ordinary retained GI path now accepts directional sunlight and immutable HDR
environment sources. Secondary hits evaluate the shared material BRDF, test
visibility through the current R3 TLAS, and add sun or sampled environment
illumination to the existing directional cache. An environment miss returns its
source radiance. The receiver field therefore includes direct visible sky and
one indirect bounce.

This is an opt-in research capability. Dynamic diffuse still defaults off, and
no Tier R default is promoted before regional-policy and whole-arena
qualification. All three source cases improve both HDR error and FLIP against
GI off and visibility-only GI. Pixels without a valid probe estimate retain the
existing compiled diffuse IBL, avoiding a coverage-induced black sky.

## Authoring

```csharp
renderer.EnvironmentSource = sourceRadiance; // EnvironmentRadiance3D, scene-linear HDR
renderer.Environment = compiledEnvironment; // Optional existing specular atlas
renderer.EnableRayShadows = true;
renderer.Settings = renderer.Settings with
{
    SunDirection = Vector3.Normalize(new(1, .65f, .3f)),
    SunColor = new(1, .85f, .65f),
    SunIntensity = 5,
    EnvironmentIntensity = 1,
    SkyAmbient = Vector3.Zero,
    GroundAmbient = Vector3.Zero,
    Shadows = true,
};
renderer.DynamicDiffuse = new DynamicDiffuseLighting3D
{
    Mode = DynamicDiffuseMode3D.Runtime,
    Grid = grid,
    RaysPerProbePerFrame = 16,
    SecondarySkyLighting = true,
    EnvironmentImportanceSampling = true,
};
```

Use the existing bindless visibility-buffer renderer, opaque resident instances,
and shared ray scene. The compiled environment is optional; when present its
`SourceKey` must match `EnvironmentSource.ContentKey`. `AUR-GI-007` rejects a
missing or mismatched source. Raw source data alone supplies GI diffuse sky;
the optional atlas supplies the existing primary split-sum specular response
and diffuse fallback outside valid probe coverage. Without an atlas, diffuse sky
is available only where the prepared GI field has a valid estimate.
The renderer continues to use its authored clear color for camera background;
this change does not add a skybox.

A shadowed sun uses the existing primary shadow-map path and ray queries at
secondary hits. The qualified sun scenes use triangles. Existing analytic
admission still requires `Shadows = false`; this change does not introduce an
analytic primary sun-shadow implementation. The environment-only scene includes
an affine analytic sphere and uses opaque ray visibility. Unshadowed hemisphere
fill and the legacy floor field remain excluded from dynamic GI.

`SecondarySkyLighting = false` is the visibility-only negative control. It keeps
visible environment misses and disables secondary sun/environment illumination;
local/emissive transport remains unchanged. `EnvironmentImportanceSampling =
false` supplies the equal-basis uniform-sphere sampling control. These controls
do not change the probe grid or directional basis.

## Shared transport and ownership

The immutable `EnvironmentRadiance3D` owns its bounded luminance/solid-angle CDF,
coarsened to at most 64 by 32 bins. The reference and runtime paths share that
distribution and `EnvironmentSampling3D.v.ts` source lookup/PDF/sampling helpers.
A 95% importance / 5% uniform mixture retains support for bilinear radiance
outside bright CDF bins. Constant sources use uniform sphere sampling.

The sun is a delta-direction source, evaluated exactly at a secondary hit.
Environment illumination uses one deterministic next-event sample per updated
hit, divided by its solid-angle PDF. The ray origin uses the geometric normal;
the BRDF uses the already-qualified smooth/normal-mapped material normal. Hits
below the geometric hemisphere are rejected. Existing backface/probe validity
and receiver-to-probe connection visibility remain in force.

This follows the light-interface and infinite-source sampling treatment in
[Physically Based Rendering](https://pbr-book.org/4ed/Light_Sources/Infinite_Area_Lights).
There is no secondary BSDF continuation, so the runtime cache does not double
count an additional environment-hit estimator and needs no MIS between those
paths. The independent reference integrator retains its own progressive
multi-bounce transport and light/BSDF MIS.

The source RGBA32 buffer is uploaded once per content identity. It is bounded by
the device storage-buffer range (`AUR-GI-008`), retired with the normal frame
owner, and replaced only after pending frames retire. Parameters/CDF use frame
slots. No CPU ray tracing, per-frame source upload, extra TLAS, new native
runtime dependency, or compiler-language extension is introduced.

When GI owns diffuse sky, `SurfaceResolve3D` sets the existing environment
response's diffuse weight to zero while retaining specular, provided the field
has a valid probe connection. Pixels without an estimate retain compiled diffuse
IBL. A displaced-grid native test requires that fallback to match GI off. The shared material
owner computes that response once. The cache supplies diffuse sky and bounce
through the existing GI texture input and three ordered compute phases. The
surface resolve keeps its existing texture/uniform ABI; GI adds two storage
bindings for sky parameters and source radiance.

Source identity, source intensity, sun direction/color/intensity and shadow mode
invalidate stationary compile coverage. A same-shape source edit also invalidates
coverage even if its CDF is unchanged. Capture remains blocked until all
directions have refreshed. Signed updates subtract stale sky/sun radiance after
occlusion; compiled-only remains a stale-lighting control.

## Experiments and qualification

```powershell
./tools/aurelian-rendering-sky/Qualify.ps1 `
    -Output artifacts/local/rendering-sky/final-coverage
```

The chain builds/tests the solution and compiler, then reruns the reference,
affine, temporal, hybrid, diffuse, R3, sampled-light, ReSTIR, TLAS, raster-motion,
area/tail, R4 GI and secondary-material lanes before the sky qualification.
Source and binary hashes are pinned. Vulkan synchronization validation is enabled
for native lanes. A result remains unaccepted until every required lane passes.

The room tests use the real retained/surface path and the independent 2,048-sample
two-bounce path integrator. HDR error is measured on visible surfaces. Unmodified
path-reference images retain their visible environment backgrounds; the display
comparison presents those background pixels with the renderer's authored black
clear. Both unmasked full-frame HDR error and surface-only HDR error are retained
in JSON. This avoids confusing a skybox/presentation difference with lighting.

Controls include GI off, visibility-only GI, stale compiled lighting after a
moving opaque blocker, and uniform environment sampling. The concentrated HDR
case uses 4,096 reference samples. Open-plane tests check the environment energy
and actual surface composition, reject a doubled diffuse sky, preserve metallic
specular, and reject missing/mismatched source metadata, and require uncovered pixels to retain existing IBL. Separate live-frame
checks reject compile capture after sun/source edits until complete refresh.

Measurements with the coverage fallback:

| Source | GI-off surface HDR RMSE | Full GI RMSE | Visibility-only RMSE |
| --- | ---: | ---: | ---: |
| Sun | 0.104941 | 0.021162 | 0.104941 |
| Environment | 0.092292 | 0.032063 | 0.064883 |
| Combined | 0.119995 | 0.031014 | 0.157707 |

Moving-blocker stale errors fall from 0.056973 to 0.024311 for sun, 0.040477 to
0.034659 for environment, and 0.076950 to 0.039430 for combined light. Negative
cache corrections are explicitly read back and asserted. Uniform/importance HDR
errors in the concentrated source are 0.142482 and 0.039788 respectively.

| Source | GI-off mean FLIP | Visibility-only mean FLIP | Full GI mean FLIP |
| --- | ---: | ---: | ---: |
| Sun | 0.119139 | 0.119139 | 0.027664 |
| Environment | 0.103295 | 0.159123 | 0.070765 |
| Combined | 0.086293 | 0.106963 | 0.034775 |

Sun, environment and combined lighting reduce surface HDR error by about 80%,
65% and 74% respectively. Environment bounce reduces FLIP about 56% against
visibility-only GI, and the complete environment path reduces FLIP about 31%
against GI off. Importance sampling reduces concentrated-source HDR error about
72% and FLIP about 57% against uniform sampling with the same probe/update basis.
All moving-blocker cases improve both metrics over stale compiled lighting.
The qualification requires each of these improvements; its adoption record still
sets `DefaultPromoted = false` until the R4 arena-scale decision.

The initial standalone environment trial improved HDR but regressed FLIP against
unoccluded IBL. A displaced-grid test isolated the concrete problem: GI enabled
globally disabled diffuse IBL even for pixels without a valid cache estimate.
Respecting the field's existing validity channel fixed that case. The fully
uncovered native test now matches GI off exactly, as does the metallic specular
control. The earlier failed FLIP trials are retained as evidence of the issue.

A 245-probe near-surface grid trial slightly improved broad-environment error
but did not resolve the then-present coverage-induced FLIP regression and
weakened the concentrated-source sampling gain. The qualified specimen retains the original 100-probe basis.
A typed pipeline-specialization trial changed one short 720p run from 6.455 to
6.300 ms, too little evidence to justify three pipelines and a compiler primitive.
Its sources/evidence are archived under `specialization-experiment/`; it was
removed from the implementation. Early admission/fixture failures and failed
FLIP trials remain preserved and unaccepted as complete qualifications.

The finite probe basis still approximates direct sky visibility and indirect
transport. Sharp HDR lobes, near-wall placement, contact contrast, temporal
quality, per-region fallback, recursive transport and whole-Beacon default
selection remain research work. Explicit cache validity and safe fallback are
inputs to that next regional-policy decision.

## Final result

The accepted run is `artifacts/local/rendering-sky/final-coverage/`. All 3,390
tests passed (1,651 solution and 1,739 compiler), with zero failures or skips.
Every required native/FLIP lane passed with clean Vulkan synchronization
validation. All 730 previous captures are byte-identical. 273 source
hashes and the proof/graphics binary hashes match the final checkout.

The 720p combined-source specimen measured 1.24 ms GPU with GI off and 6.34 ms
with full sky GI; visibility-only GI measured 6.43 ms. Full sky CPU prepare/submit
was 0.69 ms, with 100 probes and 16 updated directions per probe per frame.
These are full-frame, workload/device-specific measurements, including current
receiver connectivity. This is a quality gain with a substantial GI cost, not a
claim of a universal speedup. The subsequent [regional-policy experiment](rendering-r4-regional-policy.md)
adds Dominatus selection and bounded regional updates. Whole-Beacon qualification
and arena-scale default selection remain open.
