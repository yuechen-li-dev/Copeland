# R3: fixed-budget local-light sampling experiments

The ordinary retained renderer now has explicit local-light sampling controls.
All variants consume the same point/spot table, retained acceleration and shared
surface BRDF. They run in the existing compiled `local-ray-shadows` pass, followed
by ordinary surface lighting and optionally ordinary TAA. Exact lighting remains
the default.

## Authoring

At renderer construction, supply both `LocalShadows3D.v.ts` and
`SampledLocalLights3D.v.ts` through `SurfaceLightingPrograms.LocalRayShadows` and
`SampledLocalLights`. The complete configuration is executable in
`RenderingBindlessProof.CreateRenderer(..., rayShadows: true)`.

```csharp
renderer.EnableRayShadows = true;
renderer.LocalLightSampling = new LocalLightSampling3D
{
    Mode = LocalLightSamplingMode3D.Importance,
    FrameRayBudget = width * height * 4,
    ExactLights = 2,
    Candidates = 16,
    HistoryLimit = 256,
    Seed = 719,
};
```

Use the same opaque `NativeInstanceSet3D` and local lights as the
[exact ray-shadow path](rendering-r3-ray-shadows.md). Analytic surfaces still
require hybrid visibility. Sampling currently activates with ray local shadows;
unshadowed-only light tables retain the existing exact path.

`FrameRayBudget` is an absolute upper bound for this image, not a multiplier of
the number of lights. This initial profile admits between one and four allocated
samples per image pixel. Integer quotas and a rotating remainder distribute that
budget over the complete image, including background. Invalid, out-of-range or
back-facing selections do not issue rays, so actual queries can be lower than the
cap. This is not yet an active-pixel compactor that redistributes unused slots.
Qualification reads GPU counters to verify both quota conservation and actual
ray counts. No normal frame performs that readback.

`ResetLightSamplingHistory()` resets the sample sequence and temporal reservoirs.
Changing sampler options or the light table also resets them. Snapshot replacement
keeps the shared mesh BLAS cache and updates TLAS exactly as before. Reused samples
always retrace current visibility, including after an occluder moves.

## Compared methods

| Mode | Selection and estimator |
|---|---|
| Exact | Existing complete tiled-light evaluation and per-light shadow visibility. |
| Uniform | Independent global-uniform light draws; each contribution is weighted by light count / allocated samples. |
| UniformEligible | Uniform draws among sources with a positive unshadowed geometric contribution. This stronger control avoids treating irrelevant global lights as useful samples. |
| Importance | A per-receiver CDF proportional to positive intensity, color luminance, range/cone attenuation and shading-normal cosine. Four inverse-CDF draws share one light-table scan. |
| Hybrid | Up to three strongest proposal lights evaluated exactly, with at least one allocated sample reserved for the importance-sampled tail. The proof uses two exact plus two tail samples. |
| Reservoir | Fresh weighted RIS from a bounded number of global-uniform candidates for each visibility sample. The proof uses sixteen candidates per sample. |
| TemporalReservoir | Fresh RIS combined with validated same-pixel reservoirs from the previous frame, with an effective candidate-count cap. |

The proposal omits material BRDF and visibility. The selected source is evaluated
with the full existing BRDF, and a fresh hardware query supplies visibility.
Changing the proposal changes variance and cost; it does not substitute another
lighting model. The sampled surface resolve consumes up to four `(light, weight)`
pairs from the GPU transport. The exact mask representation remains available in
the same owner; there is no second scene or CPU ray renderer.

For a CDF source probability `q(i)` and `B` stochastic samples, a selected visible
source contributes `F(i) / (B q(i))`. The hybrid removes its exact sources from
both the tail distribution and its normalizer. An empty tail issues no samples;
it never converts an invalid sentinel into a light index.

Fresh RIS streams candidates with weights `target(i) / sourcePdf(i)`, then
normalizes the selected reservoir by `candidateCount * selectedTarget`. Temporal
merging reevaluates the old selection's target and combines its normalized weight
with its capped candidate count, followed by normalization of the combined stream.
A small positive proposal floor avoids zero-target normalization. These are authored
VTS implementations of the weighted-resampling approach described in the
[ReSTIR paper](https://research.nvidia.com/labs/rtr/publication/bitterli2020spatiotemporal/);
no RTXDI library or native CPU tracing dependency was added.

Temporal validation requires the same-pixel receiver position to agree within
1 mm and its normal dot product to exceed 0.999. Geometry/background changes
reject history. Explicit control flow guards history reads, including on shader
backends whose boolean operators do not short-circuit. Non-temporal modes bind a
small unused placeholder; only temporal mode allocates the two full history buffers.

This is temporal RIS, not full spatiotemporal ReSTIR DI. It has no spatial reuse,
motion-vector reprojection, area-source samples or dedicated denoiser. Camera
motion and TAA jitter commonly reject same-pixel history. These are concrete next
implementation seams rather than features being claimed by the current mode.

## Qualification

```powershell
tools/aurelian-light-sampling/Qualify.ps1
```

The command includes the full Aurelian and Copeland TypeScript suites, existing
reference/affine/lighting/temporal/hybrid/diffuse proofs, the exact 256-light proof,
and the new sampling proof, with Vulkan synchronization validation and pinned
NVIDIA FLIP 1.7. It records source and binary hashes and rejects edits during a run.
Keep failed attempts alongside the accepted output.

The new workloads are a 256-light hall and a key-plus-fill variant. At 96x64,
each method produces 16-frame and 128-frame means of actual native HDR lighting.
The reference uses independent world-space closest-hit acceleration, the same
BRDF/attenuation, and exact direct-light evaluation with pixel jitter disabled.
Convergence is tested in linear radiance before tone mapping. FLIP uses native
GPU tone mapping on those mean HDR images. A mean-of-HDR image is an offline
convergence specimen, not a claim that a single noisy frame looks like that mean.
Single-frame native captures are retained separately.

The suite also checks:

- An eligible-uniform control alongside global uniform, with GPU query counters
  showing their different use of the same maximum ray budget.
- Non-divisible quotas, deterministic reset, single-source radiance and an empty
  hybrid tail when there are fewer sources than exact slots.
- Moving off-camera occluders while reservoirs remain live: shadows must match
  current exact rendering. A camera shift must reject old receivers.
- Ordinary native TAA over 64 frames. Its control is an exact-light native TAA
  image with the same jitter sequence, explicitly separate from the path-traced
  HDR reference used by the main numerical tests.
- Total interactive GPU frame cost, CPU prepare/submit time and wall throughput
  at 1280x720, using 24 warmups and 40 measured frames. No per-frame qualification
  readback is included in these timings. Steady scene instance visits/uploads
  must remain zero.

## Results and interpretation

The full accepted ledger is `artifacts/local/rendering-light-sampling/final/qualification.json`.
The frozen run passes 3,352 tests with zero failures/skips, all native and FLIP
checks, and Vulkan synchronization validation. Its 78 source hashes and proof/
graphics binaries match the checked implementation. The full build has zero
warnings/errors.

| Method | Whole GPU frame at 1280x720 | Hall single-frame HDR RMSE | Hall 128-frame-mean HDR RMSE |
|---|---:|---:|---:|
| Exact | 11.365 ms | 0.000309 | 0.000309 |
| Uniform | 4.939 ms | 1.388113 | 0.125472 |
| Importance | 9.164 ms | 0.050066 | 0.004332 |
| Hybrid | 10.787 ms | 0.049340 | 0.004494 |
| Reservoir | 7.396 ms | 0.406109 | 0.037776 |
| TemporalReservoir | 8.847 ms | 0.103074 | 0.047548 |
| UniformEligible | 9.149 ms | 0.652653 | 0.066518 |
| TemporalReservoir32 | 8.851 ms | 0.191746 | 0.040767 |

Timings are no-AA interactive frames on the RTX 3070 hall workload. The sampled
modes share a 3,686,400-query upper bound; Exact is the uncapped control. The
`RayBudget` field in timing records stores the configured sampler setting, which
Exact ignores. At 96x64, Importance and UniformEligible issue 9,477 and 9,480 real
queries respectively on the recorded last frame: nearly equal actual work under
the same 24,576-query cap. Their mean HDR errors differ by about 15.4x. CPU
prepare/submit medians are approximately 0.69 ms for both at 1280x720.

Importance lowers total GPU cost from 11.365 ms to 9.164 ms in this run, about
19%. On the key-plus-fill workload, Hybrid reduces mean HDR RMSE from Importance's
0.008746 to 0.005014 and mean-image FLIP from 0.006199 to 0.004773. On the hall,
Importance has lower mean-image error and cost than Hybrid.

With ordinary native TAA, the 64-frame Importance output has display RGB MAE
0.002225 against the matching exact-native-TAA control, versus 0.087582 for global
uniform sampling. This AA control is distinct from the independent direct-light
HDR reference. The moving-occluder single-source controls match current exact
shadows byte-for-byte while 9,216 reservoirs remain live per pose. Camera shifts
reject old receivers, and all sampler modes preserve single-source radiance.

The current findings are workload-specific:

- Weighted importance sampling has much lower error than both uniform controls.
  The eligible-uniform control issues almost the same number of real rays, so
  that comparison isolates the value of weighting rather than merely skipping
  irrelevant global lights.
- Exact-two plus a sampled tail improves the key-plus-fill result, but spends
  more proposal work than fully stochastic importance sampling. It does not
  improve every workload.
- Temporal reservoirs reduce single-frame noise and frame-to-frame variation
  relative to fresh RIS, while correlation worsens the 128-frame mean. Reducing
  the history cap from 256 to 32 reduces persistent error but increases flicker.
  Neither history length wins all metrics.
- The ordinary TAA path benefits strongly from the better light proposal. This
  does not make the current same-pixel reservoir into motion-reprojected ReSTIR.

The baseline visibility transport still costs about 204 logical bytes per pixel.
Temporal mode adds two 24-word histories, another 192 bytes per pixel, for about
348 MiB total at 1280x720 before acceleration and small parameter allocations.
Reducing this transport is a separate optimization; these memory costs are part
of the comparison, not hidden by calling the method GPU-only.
