# Authored continuity constraints for compiled lighting

This experiment applies Firmament's Concept Struct ownership pattern to the
[geometry-guided local lighting experts](local-lighting-experts.md). An authored
continuity contract compiles into a joint coefficient fit, specializes the same
Visual TypeScript decoder, and runs through the existing Vulkan plant. Exact
constraints remove artificial value jumps, but increase held-out lighting error
at this fixed model budget. This is a successful research experiment, with a
negative result for replacing the current most accurate model.

The [shared boundary/interior experiment](continuous-lighting-experts.md) now
tests that follow-up. It recovers accuracy with structurally continuous boundary
values and optional edge-vanishing interior detail, at explicitly reported budgets.

## What the Firmament audit established

The current Aetheris source has several relevant seams:

- `Aetheris.Kernel.Firmament/FirmamentV2/ConceptIr.cs` retains typed Concept values,
  stable identities, provenance, consumer bindings and conformance diagnostics.
  `ConceptIrDocument.ErasureStatus` identifies construction scaffolding erased
  before Feature AIR. Compile-time values and materialized semantic references
  are explicit categories.
- `ConceptDerivationTests` exercises real profile extrusion and Assembly
  placement. Published frames drive offset/clocking; edits invalidate dependent
  consumers; unresolved, cyclic and placement-dependent derivations fail closed.
- `FirmamentV2ConceptStructStepPipelineTests` verifies Concept-driven AIR,
  provenance, STEP export/reimport and absence of extra materialized Concept
  geometry.
- The repository CLI successfully inspected
  `fixtures/Canonical/Scene/WarmModernHouse/house.firmament`. Its eight retained
  `layoutFrames` include `HouseLayout.mainFloor`, derived from `main.floor`, and
  `HouseLayout.living`, derived from that guide with a 2500/1450 mm translation.
  The actual compilation contains 254 occurrences. Guides own relationships;
  components consume them.

The focused existing `ConceptDerivationTests` lane passed all 16 tests. This is
not a claim that the complete Aetheris suite ran. Aetheris source was unchanged.
`firmament-audit.json` records the inspected revision, frames, fixture and scope.

The transferable idea is **named authored facts → validated retained relationships
→ compilation → erased scaffolding**, rather than asking samples or a renderer
to rediscover intended relationships. Firmament does not supply a general
lighting constraint solver. This experiment uses an explicit JSON authoring
contract; it adds no Firmament syntax or general-purpose language feature.

## Authoring and compilation

`tools/Aurelian.GraphicsProof/Assets/LightingFitContract.json` names the receiver,
lighting bases, domain, cubic degree, C0 continuity, soft penalty and explicitly
exempted discontinuous seams. Its physical assumption applies to this fixed
diffuse floor and finite area emitter. Covered floor samples are excluded.
Material/geometry discontinuities in other fixtures need their own declarations.

The compiler consumes the original routing planes, tree topology and local
coordinate transforms from a reopened native USD stage. Tree-path halfspaces
define convex leaf cells. Their intersections define twelve named shared edges,
such as `leaf-0/leaf-2`, retaining the two consumer leaves and segment endpoints.
No lighting samples are used to infer adjacency or continuity. Unknown seam
exemptions, invalid contracts and invalid trees receive named stop diagnostics.

A cubic restricted to a straight edge is a univariate polynomial of degree at
most three. Equality at four distinct positions therefore establishes equality
along the complete edge in exact arithmetic. Twelve edges produce 48 linear
equations, of rank 43 for this fixture. Each equation applies to all six output
channels. Float-array rounding is checked after native USD reloading.

All three candidates reuse the same tree, transforms, training receivers and
per-leaf ridge regularization:

- **Independent:** jointly solve the block-diagonal system without constraints.
  This reproduces the previous local model within the explicit 0.00001 maximum
  CPU-output difference gate.
- **Soft:** append normalized continuity equations weighted by the authored
  penalty of 10. This reduces jumps but does not guarantee continuity.
- **Exact:** solve only in the equality matrix's nullspace. The available
  coefficients fall from 80 to 37 per output channel. Stored/runtime coefficient
  count remains unchanged; this is a loss of fitting freedom, not a payload gain.

The penalty was fixed before evaluating these candidates. The fitter reads only
`training.bin`; it does not read test or repeat labels. The existing 48x48
training and 64x64 held-out centres are disjoint, but this is the same studied
room, not a sealed blind benchmark or new-scene generalization test.

Each USD variant sublayers `local-lighting.usda` and overrides only fitted
coefficients plus compilation metadata. The native-reopened projection retains
the source contract, its byte hash, original partition checksum, training byte
hash, compiler/dependency hash, named edges, equation count, rank and residuals.
C# rejects stale contract/compiler/training/partition identities and verifies
that routing, transforms and control models remain unchanged. The existing local
loader validates coefficient/routing checksums and the fixed decoder profile.
These are experimental provenance checks, not a cryptographic trust protocol.

## Results on the RTX 3070

All values below come from the actual USD → Visual TypeScript → Vulkan path.
Lighting RMSE compares six scene-linear basis channels against the finite Cycles
reference. Boundary RMSE uses the same independently derived 798-receiver BRep
visibility-transition mask; overall uses 3,975 visible receivers.

| Fit | Overall RMSE | Boundary RMSE | Maximum edge mismatch | Maximum RGB change over 1 mm |
|---|---:|---:|---:|---:|
| Independent | 0.003229 | 0.006462 | 0.143333 | 0.124243 |
| Soft continuity | 0.003792 | 0.007614 | 0.051714 | 0.053476 |
| Exact C0 | 0.005459 | 0.009333 | 0.000000019 | 0.000229 |

The edge mismatch is a separate CPU polynomial check on native-reloaded float
coefficients: 129 positions per shared edge, six channels, both leaf evaluations
at exactly the same location, before nonnegative clamping. The exact normalized
equation residual after float rounding is approximately 6.43e-9. These checks
confirm coefficient continuity, rather than claiming GPU equality at arbitrary
edge positions. Vulkan/CPU agreement is tested at all 64x64 receiver centres;
maximum disagreement is below 0.000244 including RGBA16F quantization.

The 835 one-millimetre probe pairs cross actual routed leaf boundaries and exclude
covered receivers. Their remaining RGB change includes legitimate gradients;
there are no independent Cycles samples at those paired locations. Exact C0
reduces that diagnostic by about 99.8%, but this is not a measured physical
shadow error reduction.

Exact C0 increases overall RMSE by about 69% and boundary RMSE by about 44%.
Soft continuity increases them by about 17% and 18%. Both trade away accuracy to
reduce jumps. Exact C0 misses the previous overall RMSE gate of 0.004. It is
rendered as a research candidate, and is not adopted as the default lighting fit.
Continuity alone does not establish correctness.

Independent-seed reference variation remains 0.001267 overall and 0.000742 at
boundaries. Integration uses cached GPU Cycles results with 8,192 samples and an
eight-bounce cap. This is not an exact infinite-bounce oracle. Linear algebra runs
offline on CPU with installed NumPy. Runtime stays at 2,188 parameter bytes and
the same cubic/branching decoder, with no geometry queries or constraint solve.
No new tools were downloaded.

Two camera views, lamp-off and byte-identical repeated-frame checks passed through
the existing native renderer. Lamp-off changed 14,796 pixels. Only floor
transport is reconstructed; other surfaces still use direct sun and raster
shadows. These captures are not full-scene Cycles beauty matches. GPU kernel
times are retained in evidence and exclude compilation, submission, readback and
presentation; no whole-game performance conclusion is drawn.

## Reproduce and inspect

From the Copeland repository root:

```powershell
dotnet build Aurelian.slnx -c Release -m:1
python tools/Aurelian.GraphicsProof/test_constrained_lighting_experts.py
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --constrained-lighting-experts --reuse-reference
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -c Release --no-build -m:1 -- RunConfiguration.MaxCpuCount=1
```

The default output shares `artifacts/local/local-lighting-experts` to reuse the
existing reference. Omit `--reuse-reference` to rebake; use `--output` for another
directory. The original cache checks still apply; this experiment does not add
complete reference-cache integrity or dependency tracking. All fitting and
shader compilation repeat. The new provenance hashes bind the actual training
bytes used, not their full acquisition history.

Inspect:

- `constrained-expert-evidence.json`: metrics and native qualification.
- `lighting-fit-contract.json`: copied authored contract.
- `constrained-{independent,soft,exact}.usda`: native USD layers with retained facts.
- `loaded-constrained-*.json`: projections extracted after native USD reopening.
- `constrained-*-decoder/`: ordinary specialized VTS, HLSL and SPIR-V artifacts.
- `constrained-comparison.png`: reference, three reconstructions, error maps and
  native room view. Error maps share the previous 0–0.05 channel-error scale.
- Original reference manifest/log, independent geometry masks, NOTICE and licences.

Four numerical tests cover exact continuity between collocation points,
continuous-field representability, the cost of a false continuity declaration,
explicit discontinuity exemptions and fail-closed invalid input. The full
Aurelian solution passed 1,006 tests across 28 test projects, with no failures or
skips. The Release solution build and final native proof passed. The focused
Firmament derivation lane also passed after building from source. Its build
reported existing nullable, obsolete API and test-analyzer warnings.

`Accepted` in the experiment evidence means the experiment completed its native
execution, identity and continuity checks. It does not mean the exact model meets
the prior 0.004 overall-error admission policy; its recorded error fails that gate.

## Direction supported by this experiment

Keep authored contracts and approximation quality as separate authorities. The
compiler can enforce a relationship that is explicitly provided. It must still
reject a resulting artifact that fails measured quality requirements.

The next useful experiment is a continuity-preserving basis with enough local
freedom: a shared boundary representation plus local interior residuals, or a
continuous finite-element basis. Compare at matched effective degrees of
freedom as well as parameter bytes. Simply adding more hard constraints to the
same small independent cubics would further reduce their freedom. C1 continuity,
larger bases, explicit seams for new materials and other scenes remain deferred.
