# Geometry-guided local lighting experts

This follow-up to [scene lighting experts](scene-lighting-experts.md) tests whether
authored geometry improves a compact compiled transport model. Eight local cubic
fits, routed by a geometry-guided tree, improve both overall and boundary error
in the real USD → Visual TypeScript → Vulkan path. Independent hard partitions
still introduce seams. The result supports the research direction, not production
lighting quality or generalization to arbitrary scenes.

## Reproduce

From the repository root:

```powershell
dotnet build Aurelian.slnx -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --local-lighting-experts
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --local-lighting-experts --reuse-reference
```

Output defaults to `artifacts/local/local-lighting-experts`. `--output` and
`--blender` retain the original experiment's semantics. This run used installed
Blender 5.2.2 LTS, bundled OpenUSD/NumPy, and RTX 3070 OptiX/Vulkan. No downloads.
The first command bakes a fresh reference; the second reuses local samples when
the original cache checks match. Fitting and shader compilation repeat.

Important evidence:

- `local-lighting.usda`: sublayers `lighting.usda`, which sublayers `room.usda`.
- `loaded-local-expert.json`: extracted from a reopened native OpenUSD stage.
- `partition-authoring.json`: candidate planes, source bounds and fitting policy.
- `local-expert-evidence.json`: all candidate metrics, noise, seams and selection.
- `local-expert-inspection.json`: actual Dominatus decision and blackboard state.
- `local-comparison.png`, `local-errors.png`, `partitions.png`, `boundary-mask.png`.
- `baseline-decoder/` and `local-decoder/`: separate retained source/HLSL closures.
- The original reference manifest, bake log, NOTICE and licence texts.

## Controlled comparison

The source is the same Aetheris BRep room used previously, with the same fixed
diffuse materials, sun-indirect and lamp-direct-plus-indirect RGB bases. Values
include receiver albedo. The 48x48 training and 64x64 evaluation centres are
disjoint. This is the same room/sample design as the previous study, not a sealed
blind benchmark or a test on new scenes.

Geometry candidates come from the native-reopened USD meshes: the blue box's
footprint edges and floor projections of its corners from the lamp centre and
four area corners. Nineteen unique line candidates provide potential splits.
This uses authored bounds for this box/emitter fixture; it is not a general BRep
silhouette compiler or an exact area-light visibility function.

The fitter greedily chooses seven splits using training loss only, yielding
eight leaves with at least forty visible training points each. Each leaf has ten
cubic features, a fitted coordinate transform and six ridge-trained outputs.
No held-out lighting values enter fitting or split selection. Partition shapes
therefore follow geometry, while training decides which authored cuts are useful.

Controls use identical training samples:

- Eight uniform locals in a fixed 4x2 partition, using the same cubic fit and
  regularization. This isolates the benefit of geometry-guided routing.
- A 10x10 bilinear grid with each cell averaging twelve nearby visible training
  points, matching the earlier grid construction at a larger resolution.
- The previous global neural and grid-plus-residual hybrid, rerun natively.

The geometry model carries 2,188 bytes of coefficients/routing/transforms;
uniform locals carry 2,048, the grid 2,400, and the old hybrid 2,016. The grid has
about 10% more parameter bytes than the geometry model. These are nearby budgets,
not an exact size match. Shader instructions/binaries are excluded, and all new
models together occupy a 23,390-byte text USDA layer. A generic resource-buffer
weight path remains deferred.

## Independent boundary evaluation

The Aetheris field identifies 3,975 visible held-out floor receivers. Independent
Aetheris BRep raycasts identify 798 boundary receivers: moving the receiver
±0.12 metres along either floor axis changes visibility of the lamp centre or
one of its four corners, or enters the solid occluder. This mask is derived from
geometry, not fitted predictions or reference error. It is a finite visibility
transition probe, not a complete classification of every lighting boundary.
These geometry queries are offline qualification work; the runtime decoder only
evaluates its saved partition and arithmetic.

Final native results on the RTX 3070:

| Model | Overall basis RMSE | Boundary basis RMSE | Overall relative RMSE | Parameter bytes |
|---|---:|---:|---:|---:|
| Previous neural | 0.009058 | 0.011967 | 13.16% | 1,152 |
| Previous hybrid | 0.005706 | 0.009117 | 8.29% | 2,016 |
| Uniform locals | 0.004003 | 0.007469 | 5.82% | 2,048 |
| Geometry-guided locals | **0.003229** | **0.006462** | **4.69%** | 2,188 |
| Larger grid | 0.005826 | 0.008994 | 8.46% | 2,400 |

Geometry-guided locals reduce overall RMSE by about 43% and boundary RMSE by
about 29% versus the previous hybrid. They also improve over uniform locals and
the larger grid. Their maximum individual channel error remains 0.095868.

RMSE aggregates six basis channels; relative RMSE is sqrt(error energy/reference
energy), not a perceptual score. The boundary model's relative RMSE is still
21.66%, so the aggregate improvement does not mean boundary detail is solved.
Independent-seed reference variation is 0.001267 overall RMSE and 0.000742
boundary RMSE. Model error remains materially above those noise measurements.
Cycles still uses finite 8,192-sample integration and an eight-bounce cap.

The final run measured 0.043712 ms for geometry locals over 65,536 GPU probes;
uniform locals measured 0.042528 ms. Exact timings for all candidates are in the
evidence. They are medians of five GPU intervals after two warm-ups, excluding
compilation, pipeline creation, CPU submission, readback and presentation. Costs
vary between runs and are not a whole-game speed claim. Grid access uses explicit
cell selection; this does not compare against hardware texture sampling.

## Real artifact and execution path

`SceneLocalLightingExpert` owns immutable typed data for this experimental
profile. Native USD preserves the exact float/int-array checksum. The C# loader
rejects stale scene/decoder identities, invalid dimensions, colour space/domain/
basis mismatches, nonfinite coefficients, invalid transforms and corrupt checksums.
Routing validation rejects cycles, shared/unreachable nodes, duplicate/missing
leaves and invalid child indices, even when their checksum is otherwise valid.
Plane ties use an explicit `<= 0` convention.

Validated data specializes into `LocalExpertWeights.v.ts`. The original probe and
room shaders now call a small generated `SceneExpert.v.ts` adapter; choosing a
decoder is an explicit module composition. The existing binder, VD-MIR, DXC,
SPIR-V validation and Vulkan realization remain authoritative. There is no HLSL
rewrite or new renderer. CPU/Vulkan maximum disagreement is below 0.000245,
including RGBA16F output quantization. The inference closure has no field tracing
or geometry traversal.

The example policy requires overall RMSE ≤0.004 and boundary RMSE ≤0.010 before
Dominatus cost utility. GeometryLocal is the sole eligible candidate and enters
its actual HFSM state; stale cheap data is excluded. UniformLocal misses the
overall threshold by only about 0.000003, so this policy example is sensitive to
that cutoff. It is not a production quality guarantee or a contest with several
eligible performance strategies.

Two moving views and lamp-off reuse the saved model without retraining/tracing.
Repeated frames are byte-identical; lamp-off changes 15,152 pixels. As before,
only floor transport is reconstructed, while other surfaces use direct sun and
raster shadows. The camera captures are not full-scene Cycles beauty matches.

## Discovered limitation and next experiment

Independent leaf fits do not enforce continuity. Across 835 seam probe pairs
separated by a total of 1 mm, the maximum combined RGB change is 0.124243 in
scene-linear units. This is a model-jump diagnostic; no independent 1 mm Cycles
reference was baked, so it is not itself a measured physical lighting error.
The error heatmap also retains concentrated errors near the occluder.

The next promising question is whether jointly fitting neighbouring locals with
continuity constraints or an explicit overlap/blending scheme reduces seams
without losing the geometry-guided boundary advantage. Authored genuine
discontinuities should remain explicit, rather than forcing every boundary
smooth. A comparison against a hardware-sampled texture baseline would also
separate representation quality from the current grid lowering cost. Neither
extension is implemented in this experiment.

## Validation

Full Release Aurelian build passed with zero warnings/errors. All 1,006 tests
across 28 projects passed with zero failures/skips. Seven new cases cover routing
corruption, ties, immutable ownership, identity/checksum/shape validation, domain
gates and linearity of all three new representations. The fresh-reference native
experiment and cached-reference rerun passed, as did the original
`--lighting-experts --reuse-reference` regression. No desktop game integration,
dynamic geometry, specular transport or multi-GPU execution is claimed.
