# Fixed-budget adaptive lighting receiver refinement

The [compiled diffuse lighting slice](compiled-diffuse-lighting.md) now owns the
shared numerical implementation under `Aurelian.Assets/Lighting/Authoring` and
the shared CPU mesh contract. This research harness continues to import that
implementation for its controlled comparisons.

The [shared-boundary experiment](continuous-lighting-experts.md) found that
quadratic boundary values supplied most of the improvement. This follow-up
spends a fixed numeric payload on conforming edge refinement, chosen from
training residuals. It succeeds through the real native USD → Visual TypeScript
→ Vulkan path on the RTX 3070.

At the same 3,772-byte numeric budget, the training-driven fit reduces held-out
RMSE by 13% versus longest-edge refinement and 19% versus one seeded-random
control. It slightly improves on the earlier discontinuous cubic fit while
preserving C0 boundaries. This is a successful single-scene research experiment,
not default-model adoption or a production lighting qualification.

## Authored budget and selection

`Assets/AdaptiveFitContract.json` declares the receiver, C0 relationship, basis,
ridge, six interior-edge splits, 3,772-byte numeric ceiling, random seed and
four profiles before evaluation. The smaller original quadratic fit is retained
as context; longest-edge, seeded-random and training-driven fits have identical
numeric budgets and effective coefficient counts.

The receiver mesh starts from the same eight retained convex cells and
conforming centroid fans. Refinement adds an edge midpoint and splits both
incident triangles, preserving their original tree leaf IDs and orientation.
Both sides share new vertex and edge coefficient identities. It introduces no
T-junction or independent boundary prediction. Game geometry remains the
original Aetheris BRep scene; this mesh describes its lighting approximation.

For this quadratic, six-channel representation an interior-edge split adds
one vertex, two triangles, three unique edges and four coefficients per channel.
That costs 136 numeric bytes: 8 bytes for the vertex, 32 for triangle indices,
and 96 for coefficients. Six splits take the original 2,956 bytes to 3,772.
Every refined profile has 31 vertices, 48 triangles, 78 unique edges, 66 shared
edges and 109 coefficients per channel. All training matrices have full observed
rank in this fixture.

The three allocation rules are:

- **Longest edge:** bisect the longest current shared edge, with canonical ID
  tie breaking. It uses geometry, not lighting labels.
- **Seeded random:** choose a current shared edge with seed 419. This is one
  reproducible control, not a statistical ensemble.
- **Training residual:** try every current shared edge, jointly refit the
  candidate's quadratic coefficients, and choose the largest decrease in
  training mean squared error. Canonical edge order breaks exact ties. Failure
  to find positive finite training gain is a named stop, not padded capacity.

All fits use the same visible 48×48 training receivers and positive ridge of
`training_count * 1e-6`. The adaptive pass evaluates 48, 51, 54, 57, 60 and 63
candidates across its six steps. It reads only training lighting values, the
retained partition and training receiver positions. It does not read testing
or repeat labels. Training MSE decreases from 8.774920e-6 to 3.071673e-6.
This exhaustive greedy CPU fit is bounded research code, not yet a scalable
authoring compiler for large scenes.

## Native measurements

The fixed diffuse scene, cached 8,192-sample/eight-bounce Cycles reference,
independent-seed repeat and receiver masks are unchanged. Training/test sample
centres are disjoint. Overall measurement uses 3,975 visible receivers; the
independently derived BRep boundary mask uses 798.

| Profile | Numeric bytes | Coefficients/channel | Held-out RMSE | Boundary RMSE |
|---|---:|---:|---:|---:|
| Original shared quadratic | 2,956 | 85 | 0.003878 | 0.007466 |
| Longest-edge refinement | 3,772 | 109 | 0.003578 | 0.006927 |
| Seeded-random refinement | 3,772 | 109 | 0.003838 | 0.007405 |
| Training-driven refinement | 3,772 | 109 | **0.003113** | **0.006281** |
| Previous quadratic + interior bubbles | 3,820 | 121 | 0.003627 | 0.006998 |
| Previous independent cubics | 2,188 | 80 | 0.003229 | 0.006462 |

Training-driven refinement improves overall error by about 20% over the smaller
unrefined quadratic and boundary error by about 9% over its matched longest-edge
control. It has about 3.6% lower overall error than independent cubics, at about
72% more numeric payload. The previous bubble fit and independent cubics are
context, not matched-budget controls.

Numeric payload counts float32 receiver vertices, triangle vertex/leaf indices,
six weights per coefficient and the retained seven-node routing tree. It excludes
USD encoding, provenance, carried scene/control layers, host objects and program
instructions. The experiment specializes coefficients into source constants;
equal numeric payload is not equal total artifact size or GPU memory usage.

| Profile | Actual probe SPIR-V bytes | GPU kernel median, 65,536 probes |
|---|---:|---:|
| Original quadratic | 123,428 | 0.059488 ms |
| Longest edge | 160,580 | 0.064352 ms |
| Seeded random | 162,092 | 0.064416 ms |
| Training driven | 160,220 | 0.065152 ms |

Kernel timestamps are medians of five intervals after two warm-ups. They exclude
compilation, submission, readback and presentation. Runtime decoding is on Vulkan;
offline topology selection and NumPy fitting are on CPU. No runtime ray tracing,
geometry query or constraint solving was introduced. There is no demonstrated
runtime speed gain: this round improves allocation and accuracy.

## Continuity and identity

The existing continuous model and decoder consume the refined artifacts without
new runtime representation types. The compiler emits native USD layers and
reopens them before extraction, checking composition, exact payload checksum
and retained metadata. Artifacts retain the complete contract, source compiler
hash, training/partition identities, rank, selection rule and per-step edge,
training loss and candidate-count history. The compiler identity includes both
the adaptive pass and the existing continuous compiler dependency chain.

C# independently checks payload accounting and matched budget, compilation
identities, coefficient topology, finite values, orientation, area and edge
incidence. Stale contract/compiler/training/partition/decoder identities and
corrupt checksums are exercised and rejected.

Each native decoder checks both lighting bases at 64×64 receivers and combined
RGB on a separate 256×256 grid, including covered floor. Maximum dense CPU/GPU
disagreement is below 0.000245, including RGBA16F quantization.

The CPU compares both incident triangle polynomials at 129 identical positions
per shared edge. Maximum observed mismatch is 2.98e-8. A separate 128×128
Vulkan seam shader forces both sides at identical positions on every shared
edge. Maximum differences are 1.79e-6, 1.73e-6, 1.85e-6 and 1.67e-6 for the four
profiles respectively. These are finite numerical C0 checks; they do not imply
derivative continuity or physical accuracy.

Two room cameras, byte-identical repeated frames, source scaling and lamp-off
pass. Lamp-off changes 15,253 pixels in the selected fit. Only the floor's
transport is reconstructed; other surfaces retain direct lighting/raster shadows.
The room capture is not a full-scene Cycles beauty comparison.

## Reproduce and inspect

From the repository root:

```powershell
dotnet build Aurelian.slnx -c Release -m:1
python tools/Aurelian.GraphicsProof/test_adaptive_lighting_experts.py
python tools/Aurelian.GraphicsProof/test_continuous_lighting_experts.py
python tools/Aurelian.GraphicsProof/test_constrained_lighting_experts.py
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --adaptive-lighting-experts --reuse-reference
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -c Release --no-build -m:1 -- RunConfiguration.MaxCpuCount=1
```

The new flag runs prior controls too. Output remains
`artifacts/local/local-lighting-experts` to reuse the reference. Omit
`--reuse-reference` to rebake or supply `--output`. Existing reference-cache
limits apply; no complete acquisition-history/integrity system was added.

Inspect `adaptive-expert-evidence.json`, `adaptive-fit-contract.json`,
`adaptive-*.usda`, `loaded-adaptive-*.json`, `adaptive-*-decoder/` and
`adaptive-controls.png`. The controls sheet shows predictions above errors;
all error maps retain the common 0–0.05 maximum basis-channel error scale.
`adaptive-comparison.png` includes the earlier cubic controls and room capture.
Source licences and the original bake manifest remain with the artifacts.

Four new numerical tests check paired cross-leaf bisection and arbitrary-weight
continuity, exact quadratic reproduction, deterministic training improvements
at equal storage, and over-budget/boundary-edge rejection. The eight previous
numerical tests pass too. The Release Aurelian solution build has zero warnings
or errors, and all 1,006 .NET tests across 28 projects pass with no failures or
skips. TRX evidence is in `artifacts/local/adaptive-boundary-tests`. No tools were
downloaded or Aetheris sources changed.

## Interpretation and remaining limits

An explicit continuity contract plus training-informed allocation can beat
uninformed allocation at the same numeric budget in this fixture. More capacity
everywhere was less effective than spending it where the training fit benefits.
The representation carries the authored relationship into runtime arithmetic.

The result remains above independent-seed noise: 0.001267 overall and 0.000742
at boundaries. Profiles were fixed before this measurement, but this room and
held-out set have informed earlier research rounds. The reported best-profile
label is exploratory selection; no fresh-scene blind certification follows.
New materials, directional transport, dynamic geometry, large-scene fitting,
and total-storage matched comparisons remain unqualified. A useful next study
is to freeze this policy and budget, then evaluate a new authored occluder/light
layout without changing the selection rule in response to its test labels.
