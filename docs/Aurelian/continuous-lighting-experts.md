# Shared boundaries and local interior lighting detail

This follows the [authored continuity experiment](constrained-lighting-experts.md).
Replacing independent cubic coefficients with a conforming finite-element basis
preserves boundary values by construction and restores local fitting freedom.
The actual USD → Visual TypeScript → Vulkan experiment passes the previous 0.004
overall RMSE gate with continuous profiles. It improves on the previous continuous
cubic, while the most accurate discontinuous model still has lower overall error.
Storage and effective degrees of freedom differ; this is not an equal-budget win.

## Authored relationships and the compiled basis

`tools/Aurelian.GraphicsProof/Assets/ContinuousFitContract.json` fixes four
structural profiles, C0 continuity, the receiver/domain/bases and ridge policy
before evaluation. It carries the same Concept-inspired authority pattern:
authored facts are retained for inspection, consumed at compilation and removed
from runtime work. No additional Firmament syntax or production asset API is added.

The compiler consumes the saved eight-leaf routing tree. Tree-path halfspaces
define convex cells; their actual intersections supply polygon vertices. It then:

1. Shares coincident boundary vertices by identity.
2. Inserts every retained T-junction into both incident polygon edges.
3. Adds one interior centroid per cell and triangulates each conforming ring as
   a fan. This is the transport approximation's receiver mesh; game geometry
   continues to come from the original Aetheris BRep scene.
4. Rounds receiver vertices once to float32 before fitting.
5. Fits shared nodal values jointly, with optional independent triangle interiors.

The resulting mesh has 25 vertices, 36 triangles, 60 unique edges and 48 shared
interior edges. Independent per-cell fans without the T-junction step would leave
disconnected boundaries and would not establish C0 continuity.

For barycentric coordinates `a`, `b`, `c` of a triangle:

- Linear boundary values use `a`, `b`, `c` and the three shared vertex values.
- Quadratic boundaries use the three vertex functions `a(2a-1)`, etc., plus edge
  functions `4ab`, `4bc`, `4ca`. Each shared edge has one shared midpoint value.
- The optional interior function is `27abc`. It is one at the centroid and zero
  on every edge. Each triangle owns its own coefficient for this detail.

Shared edges therefore evaluate the same boundary polynomial from either side;
local interior detail does not alter boundary values. The runtime need not solve
equality equations or blend independent predictions. Continuity is structural.
No derivative continuity is implied.

The four profiles are linear, linear plus bubble, quadratic, and quadratic plus
bubble. All use the same visible 48x48 training receivers, joint least squares
and the same declared ridge of `training_count * 1e-6` per coefficient. This
regularizer differs from the earlier per-leaf cubic regularizer; comparisons
with that earlier model include this difference. Training matrices have full
observed rank for all four profiles in this fixture.

The fitter reads only `training.bin`. It does not read testing/repeat labels or
adapt mesh topology to their error. Profiles were fixed before measurement.
Choosing the displayed best profile uses the studied held-out set, so this is
exploratory selection rather than blind certification or new-scene validation.

## Native qualification and results

The experiment uses the same static diffuse floor, fixed materials, sun-indirect
and lamp-direct-plus-indirect RGB bases. The independently derived BRep mask
contains 798 boundary receivers; overall evaluation covers 3,975 visible floor
receivers. The 48x48 training and 64x64 test centres are disjoint.

Results from the actual Vulkan decoder on the RTX 3070:

| Profile | Effective coefficients/channel | Numeric payload | Overall RMSE | Boundary RMSE |
|---|---:|---:|---:|---:|
| Previous independent cubics | 80 | 2,188 B | **0.003229** | **0.006462** |
| Previous exact-C0 cubics | 37 (80 stored) | 2,188 B | 0.005459 | 0.009333 |
| Shared linear | 25 | 1,516 B | 0.008621 | 0.010955 |
| Shared linear + interior | 61 | 2,380 B | 0.006598 | 0.009531 |
| Shared quadratic | 85 | 2,956 B | 0.003878 | 0.007466 |
| Shared quadratic + interior | 121 | 3,820 B | **0.003627** | **0.006998** |

Quadratic plus interior reduces overall error by about 34% and boundary error by
about 25% relative to the previous continuous cubic. It still has about 12% more
overall error and 8% more boundary error than independent cubics, using about
75% more numeric payload. Quadratic without interior already passes the prior
0.004 overall gate with 85 coefficients, close to the independent model's 80,
but its numeric payload is about 35% larger due to the receiver mesh. The added
interior detail improves overall error only about 6.5% over shared quadratic.
Improving the shared boundary representation accounts for the larger gain.

These payload totals count float32 vertices, triangle vertex/leaf indices, six
weights per coefficient and the original routing tree. They exclude JSON/USDA
encoding, the carried scene and control layers, retained provenance, program
instructions and host objects. They are not total file size or GPU memory usage.
The two-stage compiled SPIR-V sizes are reported separately:

| Profile | Probe shader bytes | GPU kernel median, 65,536 probes |
|---|---:|---:|
| Linear | 70,132 | 0.049792 ms |
| Linear + interior | 86,564 | 0.052736 ms |
| Quadratic | 123,428 | 0.057312 ms |
| Quadratic + interior | 139,860 | 0.059712 ms |

Shader sizes are sums of the actual exported vertex and fragment SPIR-V arrays,
saved alongside retained source/HLSL. The experiment specializes numeric data
into ordinary source constants; a compact resource-buffer decoder is not yet
qualified. GPU timestamps are medians of five intervals after two warm-ups and
exclude CPU work, compilation, submission, readback and presentation. No whole-game
performance conclusion follows from them.

## Continuity, coverage and artifact identity

Every profile passes both forms of continuity measurement:

- The CPU checks both incident triangle polynomials at 129 identical positions
  on each shared edge, using native-reloaded float32 coefficients/vertices and
  double-precision barycentric arithmetic. Maximum RGB mismatch after conversion
  to float32 is at most 2.98e-8.
- A separate real VTS/Vulkan seam shader forces both adjacent triangle evaluations
  at the same locations. A 128x128 probe visits all 48 shared edges, with 128
  along-edge centres. It adds absolute sun/lamp differences per RGB component.
  Maximum measured difference is 4.17e-7, 2.15e-6, 1.79e-6 and 3.10e-6,
  respectively. This is numerical GPU continuity evidence, not exact equality
  in floating-point arithmetic or a physical lighting error measurement.

Unlike the previous one-millimetre diagnostic, these probes compare identical
locations and do not mix seam changes with genuine lighting gradients. Endpoints
are included in the CPU lane; GPU along-edge probes sample centres. The GPU probe
does not test derivatives or every possible floating-point location.

Native inference agrees with the CPU at all 64x64 receivers and a separate dense
256x256 grid including covered floor positions. Maximum discrepancy is below
0.000245, including RGBA16F quantization. Dense inference compares the combined
sun-plus-lamp RGB response; sparse inference checks each basis independently.
Triangle containment uses a one-micro-unit normalized tolerance for float rounding.
This is finite coverage qualification, not a global floating-point coverage proof.

The artifact is a native USD layer with its own typed scope and a sublayer link to
the prior scene/partition artifact. It retains shared vertices, triangle/leaf
indices, weights, the contract, coefficient count, observed rank, and source
contract/compiler/training/partition keys. Extraction happens after native USD
reopening; exact float/int payload checksums and retained metadata are checked.
C# validates domain/basis/colour space/decoder identity, finite arrays, index
ranges, triangle orientation, coefficient topology, area, cracks and folded or
nonmanifold shared edges. Provenance checks reject stale compilation inputs;
decoder and payload corruption are exercised in the real experiment.

`ContinuousLightingModel` remains a bounded internal research type. It generates
`ContinuousExpertWeights.v.ts`, composed through the existing scene adapter and
ordinary binder, VD-MIR, DXC, SPIR-V validation and Vulkan realization. There is
no HLSL rewrite, renderer repair, runtime constraint solve or geometry tracing.
Runtime routing and barycentric arithmetic consume the precompiled receiver mesh.

Two camera views, lamp-off, identical repeated frames and linear source scaling
pass. Lamp-off changes 14,978 pixels. Only floor transport is reconstructed; the
other surfaces retain direct lighting and raster shadows. The camera captures
are not full-scene Cycles beauty comparisons.

## Reproduce

From the repository root:

```powershell
dotnet build Aurelian.slnx -c Release -m:1
python tools/Aurelian.GraphicsProof/test_continuous_lighting_experts.py
python tools/Aurelian.GraphicsProof/test_constrained_lighting_experts.py
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --continuous-lighting-experts --reuse-reference
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -c Release --no-build -m:1 -- RunConfiguration.MaxCpuCount=1
```

The new flag runs original/global/local/constrained controls before the new
experiment. Default output remains `artifacts/local/local-lighting-experts` to
reuse the existing reference. Omit `--reuse-reference` to rebake, or supply
`--output` for another directory. Existing cache checks still apply; this adds
no complete acquisition-history or reference-cache integrity system.

Inspect `continuous-expert-evidence.json`, `continuous-fit-contract.json`,
`continuous-*.usda`, `loaded-continuous-*.json`, `continuous-*-decoder/`,
`continuous-comparison.png` and `continuous-controls.png`. The comparison shows
reference, independent cubic, previous continuous cubic and the best new fit;
the controls sheet shows all four new profiles with their error maps. Maps retain
the common 0–0.05 maximum basis-channel error scale. Original source licences,
NOTICE, sample manifest and bake log continue to accompany the study.

Four new numerical tests exercise conforming T-junctions, arbitrary shared
boundary weights with independent bubbles, quadratic-field reproduction,
edge-vanishing interior detail and invalid profile/tree rejection. The earlier
four constrained-fit tests also pass. Native proof and the .NET solution suite
are separate qualification lanes. The Release solution build passed with zero
warnings/errors; all 1,006 Aurelian tests across 28 projects passed, with no
failures or skips. Aetheris source was unchanged in this round.

## What this establishes

Continuity-preserving representation recovers much of the accuracy lost by
constraining independent cubics. The prior overall-error gate can now be met
while measured GPU seams remain at floating-point noise levels. This supports
compiling authored relationships into the representation itself.

The result does not establish production lighting quality. It remains above the
reference's independent-seed noise of 0.001267 overall and 0.000742 at boundaries.
The cached GPU Cycles reference uses 8,192 samples with an eight-bounce cap, and
offline fitting uses installed NumPy on CPU. No tools were downloaded. New
scenes, materials, geometry discontinuities, dynamic geometry and directional
transport remain unqualified.

The next useful research direction is training-only refinement of boundary
elements where residuals remain large, measured at a fixed payload budget.
Quadratic boundaries supplied most of this round's gain; allocating more local
interior functions everywhere is a less efficient choice in this fixture.

The [fixed-budget adaptive follow-up](adaptive-lighting-experts.md) now qualifies
this direction through native USD and Vulkan, with training-driven choices
beating geometry-only and seeded-random allocation at identical numeric payloads.
