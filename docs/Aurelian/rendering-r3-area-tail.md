# R3: spherical area lights and stochastic-tail reconstruction

The normal retained renderer now samples finite spherical emitters and can
reconstruct only the stochastic local-light tail. Exact delta-light heads remain
current-frame signals. The existing exact, CDF importance, hybrid and ReSTIR
controls remain available. Tail reconstruction is opt-in; exact punctual lighting
remains the default sampler.

## Authoring

```csharp
renderer.EnableVisibilityBuffer = true;
renderer.EnableRayShadows = true;
renderer.LocalLights =
[
    new LocalLight3D(new(0, 3, 1), new(1, .8f, .55f), 12, 12)
    {
        Kind = LocalLightKind.Sphere,
        EmitterRadius = .65f,
        CastShadows = true,
    },
];
renderer.LocalLightSampling = new LocalLightSampling3D
{
    Mode = LocalLightSamplingMode3D.Hybrid,
    FrameRayBudget = width * height * 4,
    ExactLights = 2,
    ReconstructTail = true,
    TailHistoryLimit = 8,
    TailSpatialPasses = 1,
};
```

Sphere intensity is emitted radiance multiplied by `Color`, rather than legacy
point/spot intensity. Radius is in world metres, finite, at least .001 and at most
100; the influence range must exceed twice the emitter radius. The authored range
fade still applies. Receivers inside the one-sided spherical emitter receive zero
direct emission. The light itself does not create visible scene geometry.

Area lights require the sampled retained ray-light path, including when their
shadow flag is disabled. Exact punctual masks cannot represent area visibility and
reject area sources with `AUR-AREA-001`. Sphere volume injection has no matching
estimator and is rejected with `AUR-AREA-002`. Tail reconstruction currently admits
sampled opaque lighting without subsurface diffusion; `AUR-TAIL-002` identifies
that boundary. Alpha visibility and deforming geometry retain their earlier
unsupported boundaries. Rectangle/disk emitters and general emissive-mesh sampling
are not claimed by this milestone.

Renderer construction supplies `TailTemporal`, `TailSpatial` and `TailCompose` in
`SurfaceLightingPrograms`, as shown by `RenderingBindlessProof.CreateRenderer`.
Surface resolve now has a third typed HDR output for the stochastic signal;
its uniform and input bindings remain unchanged. Fullscreen transport supports
three declared outputs and checks their dimensions/count against the compiled ABI.

## Sampling and visibility

`LocalEmitter3D.v.ts` is shared by interactive shading, query generation and the
reference integrator. An external receiver samples directions uniformly over the
sphere's subtended solid angle. The emitted-radiance estimator multiplies the
shared BRDF/cosine response by `solidAngle * rangeFade`, which is division by the
directional sampling PDF. The sampled endpoint is the near sphere intersection,
so visibility stops at the emitting surface rather than its center. The stable
solid-angle expression avoids subtracting nearly equal numbers for small sources.
The method follows [spherical solid-angle sampling](https://www.pbr-book.org/4ed/Shapes/Spheres)
and the [area-light sampling contract](https://www.pbr-book.org/4ed/Light_Sources/Area_Lights).

The existing 16-word per-pixel transport remains. Word 14 carries a numeric
24-bit seed, exactly representable in RGBA32F; shading and visibility regenerate
the same conditional emitter sample from it and the sample slot. Hybrid word 15
contains the exact-head count. Query/quota and temporal/spatial acceptance counters
keep their existing locations. The surface uniform distinguishes hybrid packets
from other sampled packets, so spatial-rejection metadata is not read as a head
count. No NaN payload or filtered bit reinterpretation is used.

Light-selection proxies estimate integrated emitter power and include an angular
normal bound so a sphere crossing the receiver's horizon retains useful proposal
support. Hybrid ranks only point/spot lights for its exact head. Area sources
always remain in the stochastic tail; one random area sample is never described
as an exact area integral. A frame still allocates one to four samples per image
pixel, with the same complete-image visibility-query cap.

ReSTIR reuses the discrete light identity and its normalized contribution weight.
After final temporal/spatial selection it generates a fresh conditional area
sample at the current receiver and traces current visibility. Continuous emitter
coordinates are not reprojected or reused, so this is not a claim of full
continuous-domain area-sample reuse. The finite-ID estimator and the conditional
directional PDF remain separate.

## Reconstruction

Surface resolve writes the current exact head/ambient/emission separately from
the stochastic tail when reconstruction is enabled. It does not recover the head
by subtracting rounded HDR signals. The final composition adds the reconstructed
tail to the preserved current head. Normal rendering with reconstruction disabled
continues to write the complete lighting result.

The tail owner uses ping-pong GPU history and the actual previous UV/depth from
the G-buffer. Validation checks previous depth in world space, normal agreement,
albedo and roughness. History is clamped to the current local color range, its
length is bounded to 1..16, and large changes in the sampled visibility fraction
reject it. An encoded history count/visibility fraction occupies alpha, without
changing the lighting signal. Camera cuts, sampler changes, light changes and
surface-history invalidation reset the reconstruction history.

Zero to three spatial passes use geometry/material-guided 3x3 kernels with steps
1, 2 and 4. These are bilateral temporal/spatial reconstruction passes, not a full
SVGF implementation with variance moments. One spatial pass is the opt-in default:
the second pass improves many-light noise but overblurs the simpler specimens.
No shadow rays are added by reconstruction. All work remains in the normal
surface-lighting owner and frame command buffer; no CPU tracing or additional
native runtime dependency was introduced.

Six RGBA32F history images, three RGBA16F filter targets, a composite target and
the raw-tail attachment cost approximately 136 additional bytes per pixel, about
120 MiB at 720p. History/filter/composite allocation is lazy on first reconstruction
use; the raw MRT attachment is present in the lighting owner. Histories are reused
across frames, not reallocated for motion.

## Experiments

The 96x64 static specimens use one spherical source; two exact punctual keys plus
one sphere; and two keys plus sixteen spheres. The independent GPU reference has
512 samples per pixel. Each variant measures 64 frames after 16 warm-up frames,
at a complete-image cap of four selected visibility samples per pixel.

| Scene | CDF importance raw | Hybrid raw | Temporal only | One spatial pass | Two spatial passes |
|---|---:|---:|---:|---:|---:|
| One sphere | .009405 | .009405 | .004624 | .004142 | .006650 |
| Keys plus sphere | .023984 | .013214 | .007301 | .005271 | .007064 |
| Many spheres | .029207 | .024636 | .022854 | .012300 | .008506 |

Values are mean per-frame linear HDR RMSE. One-pass reconstruction reduces hybrid
error by 50..60% in these specimens. Native final-frame FLIP also improves in all
three: .020144 -> .014214, .019844 -> .014061 and .036857 -> .029640. Two passes
worsen FLIP in the simpler scenes despite reducing flicker; this negative result
is retained. Long frame averages of the unfiltered estimator have less persistent
bias than filtered averages. Reconstruction buys current-frame stability by
accepting spatial/temporal bias, rather than improving Monte Carlo convergence.

The reference's shared sampling math is independently checked against the closed
form for a Lambert receiver facing a sphere: outgoing radiance is
`baseColor * emittedRadiance * radiusSquared / distanceSquared * rangeFade`.
The 4096-sample one-pixel GPU result has relative RMS error .000029. This prevents
agreement between two uses of the same sampler from being the only normalization
evidence.

Both head-only and mixed key/area scenes require 32 frames of exact-head agreement
against punctual exact lighting. Nonzero-tail camera-cut resets repeat
byte-identically. Moving occluders and camera translation compare against
128-sample independent per-frame references. The default one-pass result reduces
occluder RMSE from .013117 to .007654, and camera RMSE from .013842 to .008552.
On stable floor receivers, the residual's projection toward the preceding
reference measures retained old-shadow energy: the filtered run measures 1.1%,
below the declared 10% gate, while the raw control measures zero. This is bounded
lag, not zero ghosting or a qualification of arbitrary fast motion.

The prior per-frame [raster admission bottleneck is removed](rendering-r3-raster-motion.md).
Its final 720p run reduces CPU-side prepare/submit from 14.390 ms to .699 ms and
improves complete throughput from 43.5 to 173.7 frames/s, with no motion topology
admissions after warm-up.

## Full-path cost and final verification

The final area benchmark has 18 sources, nine retained hybrid instances,
1280x720 output, 24 warm-up and 40 measured frames, with capture disabled. The
ordinary frame includes geometry, queries, lighting, reconstruction and tone map.
The target is offscreen; throughput is not a display/compositor benchmark.

| Mode | Tail reconstruction | GPU frame ms | CPU prepare/submit ms | Wall frames/s |
|---|---|---:|---:|---:|
| CDF importance | Off | 3.371 | .632 | 287.6 |
| CDF importance | One spatial pass | 3.500 | .643 | 280.7 |
| Hybrid | Off | 3.466 | .562 | 285.7 |
| Hybrid | One spatial pass | 3.679 | .657 | 263.1 |

The hybrid reconstruction adds .213 ms GPU time in this workload. The area and
raster benchmarks have different light counts and workloads; their GPU times
must not be presented as successive measurements of one scene.

`artifacts/local/rendering-area/final/qualification.json` accepts the final source.
All **3,366 tests** pass with zero failures/skips, including 1,730 Copeland TS tests.
The build succeeds with 14 warnings in unchanged upstream/integration code. Every
earlier native proof, the new raster/area proofs and applicable FLIP gates pass,
with clean Vulkan and synchronization validation. `verification.json` confirms
all **98 source hashes** and both tested assemblies match the qualified files.

Of 53 comparable PNG captures/maps from the preceding punctual ReSTIR ledger,
47 remain byte-identical. Three native images each differ in one channel of one
pixel by one 8-bit code value; their three corresponding FLIP maps also change.
The native differences are at `(53,60)` in camera-pan combined reuse and `(50,56)`
in static/moving CDF importance. They are consistent with rounding after routing
punctual samples through the shared emitter helper and extending resolve's MRT
outputs. This is a precision-level change, not a replaced golden image. Exact
values are retained in `punctual-image-differences.json`; all reference/error gates
pass. The 96 raster-retention comparisons and 32 exact-head controls remain exact.

`area/area-comparison.png` shows the native reference, raw hybrid and one-pass
reconstructed images, enlarged four times with nearest-neighbor sampling. This
contains actual rendered pixels, not generated artwork.

## Reproduction

```powershell
tools/aurelian-area-lights/Qualify.ps1 -Output artifacts/local/rendering-area/final
```

The wrapper runs the full solution build and tests, Copeland TS tests, all earlier
reference/affine/lighting/hybrid/diffuse/shadow/sampling/ReSTIR/TLAS proofs, raster
motion comparisons, area experiments and pinned NVIDIA FLIP. It freezes recorded
source hashes and records the tested proof/graphics assembly hashes. Vulkan and
synchronization validation must be clean. Failed and exploratory attempts remain
beside the final artifact directory.
