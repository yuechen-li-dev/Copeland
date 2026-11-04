# R4: textured and smooth-normal secondary hits

The retained dynamic diffuse path now shades secondary hits with the resident
surface material and geometry tables. Triangle hits reconstruct UVs, vertex
colors, smooth normals and tangent handedness from their primitive identity and
barycentrics. Analytic hits reuse the raster owner's analytic UV and tangent
construction. Both paths sample the same GPU images and samplers as primary
visibility, then call the shared `EvaluateSurfaceSamples` function.

This extends the [first R4 slice](rendering-r4-dynamic-diffuse.md). Dynamic diffuse
still defaults off. This is an opaque, one-indirect-bounce qualification with
local and emissive sources, a prepared probe grid, and diffuse receivers.
Sun/environment transport, recursive bounces, alpha continuation, sparse grids
and region policy remain open.

## Enable the material path

```csharp
renderer.DynamicDiffuse = new DynamicDiffuseLighting3D
{
    Mode = DynamicDiffuseMode3D.Runtime,
    Grid = grid,
    RaysPerProbePerFrame = 16,
    SecondaryMaterials = true,
    SecondaryTextureLod = 0,
};
```

Use the existing bindless visibility-buffer renderer and shared R3 ray scene.
`AUR-GI-005` diagnoses missing resident geometry/material tables. Constant-factor
flat surfaces retain their existing shading branch. Textures, vertex colors or
smooth vertex normals select the attribute path. No texture copy, software
texture atlas, alternate material evaluator, extra TLAS, or new native runtime
dependency is introduced. GLB import remains the existing `GlbModelImporter`
path; LOD0 geometry is the ray-query surface.

The five material channels are base color, emission, normal, metallic/roughness,
and occlusion. Base color and emission use the owner's sRGB images; the other
channels use linear images. UV wrapping, filtering and channel factors come
from the original material sampler and record. This follows the existing
[glTF material conventions](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html).
Occlusion is preserved in the surface payload; it does not attenuate the direct
light BRDF. There is no ambient secondary term in this bounded transport model.

Compute sampling uses explicit `SampleLevel`. LOD 0 is the qualified default;
`SecondaryTextureLod` exposes a finite 0..13 research control, clamped by the
actual image/sampler mip range. There are no pixel derivatives, ray-cone LOD
selection or automatic distance-based mip policy in this change. Imported mip
chains are borrowed from the ordinary GPU texture owner.

Shadow origins are displaced along the hit's geometric normal. The BRDF uses
the interpolated or normal-mapped shading normal, guarded against pointing
through the geometric surface. Incoming light below the geometric hemisphere
is rejected. Existing probe backface handling and receiver connectivity remain
in force. Reflected triangle transforms remain rejected by the resident
renderer; reflected analytic transforms and negative vertex tangent signs are
qualified separately.

`SecondaryMaterials = false` is the factor-only negative control.
`CaptureSecondaryWitness = true` enables a GPU readback payload for qualification;
normal rendering binds a tiny placeholder and performs no witness stores.

## Compiler and resource ownership

Typed compute admits readonly `Texture2D<float4>`, `Sampler`, and bounded
`DescriptorArray` resources. Dynamic indexing requires `NonUniform(u32)`;
literal indices are checked against the declared capacity. Sampling requires
`SampleLevel(texture, sampler, float2, f32)`. Selected textures/samplers can be
passed to helpers, while resource returns, resource locals and descriptor-array
helper parameters are rejected. Implicit-derivative `Sample` is rejected.
Sampled and cooperative-matrix commands remain separate profiles.

These capabilities are certified as `compute.sampled.m1`, or
`compute.rayquery.m5` when combined with the shared ray-query path. The compute
emitter produces validated DXC/SPIR-V with explicit-LOD image sampling and
nonuniform descriptor decorations. Raster and compute continue to share the
Copeland parser, canonical VD-MIR and material math.

The GI descriptor sets borrow the current frame slot's resident vertex, index
and instance buffers, immutable material records and the owner's GPU images
and samplers. Immutable sampled descriptor arrays are written once per owner
per slot. Transform snapshots update storage bindings without republishing
4,096 image descriptors in each of the three GI phases.

## Reproduce and assess the evidence

```powershell
./tools/aurelian-rendering-secondary/Qualify.ps1 `
    -Output artifacts/local/rendering-secondary/final-qualified
```

The wrapper builds `Aurelian.slnx`, runs the solution and Copeland compiler tests,
then reruns reference, affine, temporal, hybrid, diffuse, R3, sampled lighting,
ReSTIR, TLAS, raster motion, area/tail and R4 GI qualification before the new
secondary-material proof. Native lanes enable Vulkan synchronization validation.
Pinned source hashes are checked after execution. The top-level result remains
unaccepted unless every native and FLIP lane accepts.

The material oracle independently reconstructs interpolation from the GPU hit's
world position and source geometry, performs CPU texture addressing/filtering,
sRGB conversion, tangent-space normal mapping and point-light BRDF evaluation,
then convolves the finite directional field on the CPU. Its compiled field is
consumed by the real GI receiver resolve and surface output. This isolates
material evaluation; it does not independently prove visibility. An emissive
checker is additionally replaced with exact constant-material tiles and traced
by the independent 2,048-sample, two-bounce integrator.

Nine specimens cover albedo, emission, smooth normals, normal mapping, all five
channels plus vertex colors and nonuniform transforms, negative tangent signs,
analytic spheres, reflected analytic boxes and a real imported
`crate-channels.glb`. A six-frame moving smooth/textured instance exercises TLAS
updates and both frame slots, then compares the current result with its CPU
material oracle. Timings measure 24 warmup and 64 ordinary rendering frames at
720p, with witness capture disabled.

Per-hit UV error must remain below 0.001. Nearest-filtered normal error is below
0.00001; nearest material error is below 0.003 after dividing each absolute
channel error by `max(1, abs(expected))`. Linear-filtered witnesses allow 0.006
for normal-vector distance and normalized channel error. These are explicit
qualification tolerances for finite-precision hardware sampling against the CPU
formula, not a bitwise sampling claim. Maximum observed errors and the largest
channel discrepancy are retained in JSON. Final HDR output must be below 0.004
RMSE and below 15% of the factor-only error; each tone-mapped material specimen
must also improve FLIP.

The initial runs are preserved: the first emissive comparison exposed an
absolute tolerance that scaled incorrectly with HDR emission; subsequent runs
isolated the existing reflected-triangle admission boundary and corrected proof
snapshot publication. They remain unaccepted. The interrupted first full run in
`final/` also remains unaccepted; profiling prompted the immutable-descriptor
cache before the final qualification. The interrupted `qualified/` run is also
unaccepted; its replacement validates defined hit payloads without interpreting
the unused words of miss packets.

## Measured material gains

On the RTX 3070, the independent material-oracle comparisons produce these
captured HDR errors. Zero means equality in the renderer's captured HDR output,
not bitwise identity of CPU and shader arithmetic.

| Specimen | Factor-only RMSE | Full material RMSE | Mean FLIP factor-only → full |
| --- | ---: | ---: | ---: |
| Albedo texture | 0.0065919 | 0.0000181 | 0.019972 → 0.000115 |
| Emissive texture | 0.0697552 | 0.0000634 | 0.143055 → 0.001102 |
| Smooth normals | 0.0011925 | 0 | 0.004333 → 0 |
| Normal map | 0.0005104 | 0 | 0.002161 → 0 |
| All channels / vertex colors / affine transform | 0.0083471 | 0.0000262 | 0.016801 → 0.000190 |
| Negative tangent sign | 0.0027331 | 0 | 0.007218 → 0 |
| Analytic sphere | 0.0054066 | 0.0000196 | 0.017449 → 0.000039 |
| Reflected analytic box | 0.0109115 | 0.0000278 | 0.029444 → 0.000154 |
| Imported GLB | 0.0142296 | 0.0000417 | 0.014521 → 0.000191 |

Each static material case reduces the captured HDR error by at least 99.6%.
The independent emissive-tile two-bounce comparison reduces error from
0.0689794 to 0.0179877, a 73.9% improvement. The remaining error includes the
probe representation and finite directional basis; matching material samples
does not remove transport approximation.

Across the static cases the maximum UV error is below 0.0000006. The largest
normal-vector difference is 0.003102 and normalized material error is 0.004231,
both in the linear-filtered imported specimen. The six-frame moving specimen
performs five TLAS updates and has HDR RMSE 0.0000377 against its current-pose
CPU material field.

The descriptor-cache experiment reduced 720p full-material CPU prepare/submit
from about 4.2 ms to 1.0 ms. All 49 capture and FLIP PNGs are byte-identical
before and after caching; the comparison is preserved under
`cached-descriptors/descriptor-cache-comparison.json`. GPU time remained around
3.1 ms for this scene (75 probes, 16 updated directions per probe per frame).
The factor-only control was about 3.6 ms GPU and GI off about 0.32 ms. Those
single-scene timings do not establish a universal speed advantage for texture
sampling; the demonstrated gain is material fidelity. Full-frame measurements
include receiver connectivity and the normal raster/surface pipeline.

The final qualification passed all 3,389 tests (1,650 solution and 1,739
compiler), with zero failures or skips. All required native/FLIP lanes passed
with clean Vulkan synchronization validation. All 681 legacy PNG captures are
byte-identical to the preserved shader-refactor qualification. Its 266
source hashes and proof/graphics binary hashes were rechecked against the final
checkout. The original R4 constant-material scene retains its previous GPU
cost: approximately 3.36 ms with GI versus 2.67 ms off at 720p.

The new textured scene's final full-frame timing is approximately 1.16 ms CPU
prepare/submit and 3.12 ms GPU at 720p. These are bounded scene measurements.
The accepted source-pinned run, per-case evidence, legacy capture comparison,
and detailed timings are under
`artifacts/local/rendering-secondary/final-qualified/`.
