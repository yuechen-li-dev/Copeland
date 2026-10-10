# Scene lighting experts: bounded experiment

This combines compiled light transport with a saved deterministic expert. OpenUSD
carries the source scene, decoder identity and learned coefficients. Aurelian
validates it, specializes a Visual TypeScript decoder and executes it on Vulkan.
Dominatus selects a representation using measured error and cost after checking
eligibility. The experiment succeeds for one static diffuse floor receiver;
production GI quality and a general scene model remain unqualified.

## Reproduce

Run from the repository root:

```powershell
dotnet build Aurelian.slnx -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --lighting-experts
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --lighting-experts --reuse-reference
```

Default Blender: `C:/Program Files/Blender Foundation/Blender 5.2/blender.exe`;
override with `--blender <executable>`. This witness used installed Blender 5.2.2
LTS, bundled NumPy/OpenUSD and Cycles OptiX on an RTX 3070. Nothing was downloaded.
The script requires an OptiX GPU and fails explicitly without one.

Output: `artifacts/local/lighting-experts`, overridable with `--output`.
The full reference bake took about 137 seconds locally. `--reuse-reference`
reuses samples when scene key, sample count and Blender version match, then
repeats fitting, USD reload, shader compilation and native qualification. This is
a local cache, without complete dependency tracking or reference-file integrity
guarantees.

Important outputs:

- `room.usda`: Aetheris-derived meshes, bound materials and sun.
- `lighting.usda`: sublayers the room and carries all four predictors under
  `/AurelianLighting` as typed attributes.
- `loaded-expert.json`: extracted exclusively from a reopened native USD stage;
  C# consumes this projection rather than parsing USDA itself.
- `expert-evidence.json`, `expert-inspection.json`: native results and the real
  Dominatus decision/blackboard/agent trace.
- `reference-manifest.json`, `reference-bake.log`: reference settings and bake log.
- Generated `ExpertWeights.v.ts`, retained shader sources/HLSL, `comparison.png`.
- `inline-shapes-stop.log`: the rejected indexed-array lowering specimen.
- Integration NOTICE and GPL/AGPL texts, retaining the existing optional
  [Aetheris boundary](aetheris-field-lighting.md).

## Compiled domain and reference

The existing lighting experiment owns the room. Both experiments share Aetheris
BRep tessellation and coordinate conversion. Native OpenUSD writes and reopens
the mesh/material/light stage; Blender constructs the reference from those
attributes. No sibling repository or geometry kernel changed.

The receiver is visible floor at Y=0, X/Z in [-2.7,2.7] metres. Fitting masks the
blue block's footprint in this fixture; independent C# qualification uses the
Aetheris field to identify visible receivers. The decoder validates rectangular
coordinates; the host owns surface visibility and eligibility. Hidden floor
queries are not qualified.

The six output channels are two fixed light bases:

1. Unit sun diffuse indirect RGB, with lamp emission disabled.
2. Unit lamp diffuse direct-plus-indirect RGB, with sun disabled.

Including lamp direct illumination captures its visibility boundary and on/off
coefficient. Values already include the floor's fixed diffuse albedo and use
scene-linear Rec.709. Geometry, source placement and material changes require
recompilation. Nonnegative source coefficients scale/combine these bases linearly.

Cycles uses 8,192 samples, an eight-bounce cap, independent seeds, no denoising,
adaptive sampling or clamping. This is a finite Monte Carlo estimate of the
tessellated scene, not an exact or unbiased infinite-bounce solution.
Training uses 48x48 points; evaluation uses disjoint 64x64 pixel centres with
3,975 visible held-out receivers. An independent-seed repeat measures sampling
variation. Holding out points in one room does not prove generalization to other
scenes or visibility configurations.

## Results

Polynomial: six quadratic features. Neural: those six plus 26 fixed, seeded ReLU
features, with NumPy ridge-trained output weights. Hidden features are not
trained. Grid: 6x6 cells averaging twelve nearby visible training samples, with
bilinear reconstruction. Hybrid: a learned 32-feature residual over that grid.
Fitting runs on CPU; reference integration and decoder execution run on GPU.
There is no general ML runtime, fully trained MLP or tensor-core qualification.

Final local RTX 3070 measurements:

| Representation | Basis RMSE | Relative RMSE | Max channel error | Coefficient bytes | GPU ms / 65,536 probes |
|---|---:|---:|---:|---:|---:|
| Polynomial | 0.023676 | 34.39% | 0.222414 | 144 | 0.014368 |
| Coarse grid | 0.008369 | 12.16% | 0.109214 | 864 | 0.030624 |
| Neural | 0.009058 | 13.16% | 0.140342 | 1,152 | 0.021472 |
| Grid plus learned residual | 0.005706 | 8.29% | 0.117881 | 2,016 | 0.039712 |

RMSE aggregates six basis channels at visible held-out receivers. Relative RMSE
is sqrt(error energy/reference energy), not per-pixel percentage or a perceptual
score. The independent reference repeat has RMSE 0.001267, relative RMSE 1.84%
and maximum channel difference 0.018358. Model error remains above reference
noise. Hybrid wins aggregate error; the grid has lower maximum channel error.

Neural is faster than grid in this lowering: arithmetic weights become constants
while grid access becomes cell selection branches. This does not prove neural
inference universally beats texture sampling. Times are median GPU timestamp
intervals for five draws after two warm-ups; compilation, pipeline creation,
submission, readback and presentation are excluded. Whole-game performance is
unqualified.

Coefficient bytes exclude scene, decoder instructions and shader binaries. The
text USDA layer containing all models is 10,631 bytes, with the scene in a
separate sublayer; training samples occupy 55,296 bytes. These represent different
information and do not establish a general compression ratio or total GPU memory
footprint.

## Artifact and execution checks

USD float arrays survive an exact SHA-256 round trip. Immutable
`SceneLightingExpert` rejects malformed manifests, stale scene/decoder keys,
wrong shape/profile/domain/colour-space/basis, nonfinite values and bad checksums.
Coefficient access returns copies; queries reject invalid coordinates/scales.

Validated weights specialize into ordinary `ExpertWeights.v.ts` functions,
compiled through the existing binder, VD-MIR, DXC and SPIR-V validation. Actual
Vulkan probes agree with C# within 0.000245 maximum channel difference, including
RGBA16F quantization. No geometry traversal, field tracing or training occurs in
the inference shader closure.

The first indexed inline-array form exposed the 256-element aggregate cap and
selection-chain expansion that exceeded DXC's existing 15-second timeout. The
failed specimen is retained. Specializing immutable coefficients is reasonable
for this small compiled scene. Larger/frequently replaced weights need a reusable
resource-buffer path; this experiment does not implement that language milestone
or patch renderer-owned HLSL around it.

The 3D shader evaluates the selected decoder directly on visible floor fragments,
without a runtime lighting texture. Two cameras and lamp-off demonstrate reuse.
Repeated captures are byte-identical; lamp-off changes 15,104 pixels. Other
surfaces retain direct sun and raster shadow maps. These captures are not
full-scene Cycles beauty matches; the panel also lacks an HDR emission display
pass here. Reflections, animation and volumetric transport are outside scope.

## Deterministic selection and next directions

The decoder regresses continuous radiance; it does not choose a pixel colour with
argmax or temperature sampling. Dominatus chooses which eligible representation
to execute. Scene validity, finite positive measured cost and measured quality
are gated before kernel options exist. A deliberately stale, misleadingly cheap
candidate is rejected, including the invalid-only case.

| Maximum measured RMSE | Selection on this device |
|---:|---|
| 0.040 | Polynomial |
| 0.010 | Neural |
| 0.006 | Hybrid |
| 0.001 | No eligible candidate |

A real HFSM `Decide` step selects Neural at 0.010 and records scene key, choice,
RMSE and cost in the inspector. The threshold is an example aggregate policy,
not a pointwise or perceptual error bound.

The combined idea is promising for bounded static transport: small deterministic
functions support moving diffuse views and source scaling without repeating
offline integration. The contact sheet exposes a global predictor's weakness:
polynomial loses occluder detail and ReLU introduces angular artifacts. Grid plus
residual follows the reference better, but still blurs boundaries.

Geometry/visibility partitions with local experts, explicit discontinuity
metadata and adaptive sampling are the strongest next experiment suggested by
this witness. Compare them against an equally sized texture/grid baseline and
measure errors near boundaries as well as aggregate RMSE. General view-dependent
specular transport needs a richer input domain and separate evidence.

## Validation

Full Release Aurelian build: zero warnings/errors. All 999 tests across 28
projects passed, zero failures/skips. New tests cover manifest validity,
immutability, linear coefficients, bounded queries and quality-before-cost.
Native offscreen `--lighting-experts --reuse-reference` and the existing
`--lighting-compilation` regression passed. Desktop game integration and
multi-GPU execution are unqualified.
