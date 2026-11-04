# R3a: sun, environment, FLIP and temporal qualification

The reference integrator now traces directional sun and source HDR environments.
The qualifier measures the existing compiled floor's sun-indirect basis, evaluates
native output with NVIDIA FLIP, and compares ordinary TAA against fresh reference
captures in static, camera-pan and moving-occluder sequences. This completes this
bounded R3a step; the full textured/coverage and standing scene suite remains open.

## Run

```powershell
./tools/aurelian-rendering-reference/Qualify.ps1 `
  -Output artifacts/local/rendering-reference-lighting/final

# Focused native capture after building:
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --rendering-reference-lighting `
  --output artifacts/local/rendering-reference-lighting/experiment `
  --reference-lighting artifacts/local/beacon-compiled-lighting
```

The full command builds Aurelian, runs its tests and Copeland TS tests, and runs
the original reference/furnace/floor, affine, lighting/temporal, hybrid and diffuse
native proofs with synchronization validation. It then evaluates the saved native
images using pinned `flip-evaluator==1.7`, NumPy and Pillow in a repository-local
tooling venv. These packages are offline qualification dependencies; games neither
load them nor launch Python. FLIP is NVIDIA's
[BSD-3-Clause evaluator](https://github.com/NVlabs/flip), not a replacement metric
with the same name.

`qualification.json` records source and assembly hashes and rejects sources
changed during execution. `lighting-temporal/evidence.json` contains transport
and temporal results; `metrics-manifest.json` freezes inputs/output policy, and
`flip-evidence.json` records evaluator versions, native evaluator binary hashes,
input image hashes, per-pair scores and heatmaps. Previous qualification runs are
preserved under their existing artifact roots.

A final validation ledger at
`artifacts/local/rendering-reference-lighting/final/qualification.json` accepts
**1,615 Aurelian tests plus 1,730 Copeland TS tests**, with zero failures or skips.
All five native proofs and **139 FLIP pairs** pass. The final build has zero
warnings/errors; Vulkan validation and synchronization are clean. All 50 recorded
source hashes and both assembly hashes match the qualified checkout.

## Share the source and lighting coefficients

```csharp
var imported = RadianceEnvironmentImporter.Load("studio.hdr");
var source = new EnvironmentRadiance3D(imported.Width, imported.Height, imported.Rgba);
renderer.Environment = VulkanEnvironmentCompiler.Compile(
    plant, assets.Shader("EnvironmentCompile3D.v.ts"), source);
var lighting = ReferenceLighting3D.FromSettings(renderer.Settings, source);
using var reference = new ReferenceIntegrator3D(
    plant, assets.ComputeShader("ReferenceIntegrator3D.v.ts"),
    captureSettings, camera, triangles, spheres, light, analytic, lighting);
```

`EnvironmentRadiance3D` owns an immutable, finite, scene-linear equirectangular
RGBA source up to 4096×2048. Its content identity matches the real-time compiler's
source identity. The filtered atlas cannot recover the original radiance image;
retain the source for reference comparisons. The shared `EnvironmentCoordinates3D`
module defines longitude `atan2(z,x)`, +Y north and the same clamp-to-edge source
sampling used by the existing compiler. The tracer implements matching bilinear
lookup over immutable GPU storage, including primary misses.

`ReferenceSun3D` uses a direction **toward** the sun and normal-incidence
irradiance `SunColor * SunIntensity`. It is a delta-direction light: visibility
rays and the shared direct BRDF evaluate it at every admitted scattering vertex.
There is no finite solar disk added to primary misses. The sun estimator has no
competing continuous BSDF PDF and is not MIS-weighted. It remains active when
`NextEventEstimation=false`, which disables non-delta area/environment sampling.

Environment sampling uses luminance times exact texel solid angle to build a
CDF with at most 64×32 angular bins. Sampling is uniform in solid angle inside
the selected bin. A 5% uniform-sphere component guarantees support across
bilinear radiance bleeding into dark bins. The evaluated PDF uses the stored
float32 CDF differences, not an independently rounded ideal distribution.
Continuation misses receive the complementary power-heuristic weight. See
[PBRT's infinite-area-light treatment](https://pbr-book.org/4ed/Light_Sources/Infinite_Area_Lights).

The CDF is an importance proposal; the full source image is sampled for radiance.
The small CDF does not downsample the reference's light. Source pixels and path
state use device-only buffers. The CDF and geometry are retained across sample
batches, with no host bounce decisions or ray readback. Captures still declare a
finite scattering depth and 100 km ray range.

The default proposal detects exactly constant RGB sources and uses BSDF sampling
alone. Adding uniform light samples to the Lambertian constant-environment
control introduced noise; the adaptive path removes that unnecessary estimator.
`ImportanceSampleEnvironment=false` explicitly selects the uniform control.
No radiance clamping or post-hoc normalization is used to improve a score.

The larger shader uses Copeland's certified bounded runtime bounce loop. Fully
expanding all bounces exceeded DXC's existing 15-second limit; the runtime loop
compiled in about one second in the focused test. The timeout was not increased.

## Transport measurements

RTX 3070, opaque constant materials, declared seeds and sample counts:

| Control | Measured result |
| --- | ---: |
| Directional sun versus independent Lambert law | maximum RGB vector error 6.71e-8 |
| HDR environment importance, 64 spp | RMSE 0.049030 |
| Same environment with uniform proposal, 64 spp | RMSE 0.150138 |
| 4,096 spp mean versus independent spherical quadrature | RGB vector error 0.00038866 |
| Primary environment lookup versus independent bilinear oracle | maximum vector error 0.00018116 |
| Constant environment, adaptive, 1,024 spp | RMSE 0.000001837 |
| Constant environment, uniform MIS, 1,024 spp | RMSE 0.00188925 |

The hotspot control uses the same high-sample independent seed for both low-sample
estimators. Below-horizon and zero-light sun controls remain black; an analytic
blocker measurably shadows the sun. Constant-source classification ignores alpha,
which is not environment radiance, and is tested for immutable source ownership.

The compiled floor comparison uses the actual `.alight` asset and its GPU decoder,
with emission disabled and primary direct sunlight excluded from the reference.
It checks 3,963 conservatively visible receiver sites at 4,096 spp. Outgoing
radiance RMSE is **0.00098301**; per-unit-receiver-albedo RMSE is **0.00345714**.
The new reference versus retained independent Cycles sun samples has outgoing
radiance RMSE **0.00020246**. Scene identity and retained sample checksums are
verified. The original emission-basis comparison continues to run separately.

## Output and FLIP

`VulkanReferenceOutput3D` uploads the captured HDR mean and runs the ordinary
`ToneMap3D.v.ts` program: exposure 1, existing filmic curve, sRGB encoding, zero
bloom, zero color retention/gamut-compression options. Runtime images use those
same output settings. FLIP consumes these encoded images with `inputsRGB=true`,
LDR mode and **67 pixels per degree**. It does not choose its own exposure or
substitute its HDR tone-mapper for Aurelian's output.

| Comparison | Mean LDR-FLIP |
| --- | ---: |
| Identical native output | 0 |
| Importance-sampled environment, 64 spp | 0.17810 |
| Uniform environment, 64 spp | 0.36149 |
| Constant environment, adaptive | 0 |
| Constant environment, uniform MIS | 0.02843 |
| Actual compiled environment lighting versus reference | 0.05057 |
| Environment-off runtime control versus reference | 0.95362 |

The compiled-environment case uses actual native raster lighting and the atlas
compiled from the same source. Its error includes prefiltering, split-sum and
render-target approximations; the reference directly integrates the source and
shared BRDF. It is not advertised as an exact equality test for approximate IBL.

The evaluator also reports full-frame P95/maximum error and mean error over
reference edge pixels. The edge mask includes both adjacent pixels wherever any
sRGB reference channel changes by more than .05 horizontally or vertically. The
mask is applied to the full-image FLIP error map, preserving filtering context;
matching background pixels cannot dilute this separate edge score.

## Scripted temporal comparisons

Each sequence has 16 warmup frames and 12 measured frames at 96×64. Ground truth
is a fresh 1,024 spp direct-sun capture at each camera/object pose; static poses
reuse the identical reference. The scene contains a floor, thin bars and a moving
box. Instances and previous transforms run through the ordinary retained renderer,
with a stable vertex-correspondence identity. This isolates the direct-light
target implemented by the real-time path instead of adding GI mismatch to a TAA
test. Full indirect sun transport is checked by the compiled receiver separately.

Three native controls run at every pose: no AA, jitter with history weight zero,
and ordinary TAA. Display-linear residual flicker is
`mean(abs((I[t]-I[t-1])-(R[t]-R[t-1])))`, with explicit sRGB decoding and no
reprojection. Subtracting reference change avoids counting real camera/object
motion as flicker.

| Sequence | Residual flicker: no AA / jitter / TAA | Mean FLIP: no AA / TAA | Edge FLIP: no AA / TAA |
| --- | --- | --- | --- |
| Static | 0 / 0.011568 / 0.003859 | 0.03888 / 0.03016 | 0.17940 / 0.14537 |
| Camera pan | 0.002230 / 0.009440 / 0.007236 | 0.03437 / 0.03217 | 0.16076 / 0.15475 |
| Moving occluder | 0.001028 / 0.011403 / 0.005306 | 0.03743 / 0.03165 | 0.17333 / 0.15300 |

Stationary no-AA output has zero frame-to-frame flicker but greater reconstruction
error. No-AA motion residuals are also smaller in these pose-step fixtures. TAA
improves reference reconstruction and suppresses jitter noise here; it does not
dominate no AA on every metric. The gates test spatial reference error and history
improvement over the matched jitter control, while preserving this tradeoff.

A deliberately frozen first native TAA frame gives mean FLIP **0.06249** on
camera pan and **0.04662** on moving occluders, versus approximately **0.032** for
the updated sequences. This stale-frame ablation retains the same renderer's
static approximation error. Separately, resetting each sequence at a camera cut
produces byte-identical output to a fresh TAA renderer.

The automated gates require improved whole-frame and edge FLIP over no AA in
these specimens, lower motion error than the frozen-native control, and the
declared residual-flicker bounds against jitter without history. Scores, gates,
scenes and output settings are retained rather than selected from screenshots.

These results qualify the declared opaque scenes and viewing assumptions. They
do not finish textured/alpha foliage, Wet Night, general scene material admission,
analytic emitter sampling or the entire R3a standing scene suite.
