# R2: offline LOD assets and GPU selection

Static model import/build can now prepare up to three cheaper index buffers alongside LOD0. The native
resident renderer selects a prepared level per object on the GPU. Geometry generation, simplification
and vertex-cache optimization happen at import/compile time; the frame loop does none of those jobs.
This implements discrete asset LOD, without a runtime simplification DAG or streaming hierarchy.

## Author and deliver

Opt in per model in `assets.toml`:

```toml
[[models]]
id = "forest.tree"
path = "Models/tree.glb"
lod = true
scale = 1.0
```

Build through the existing asset compiler:

```powershell
dotnet run --project src/Aurelian/Aurelian.AssetTool -c Release -- build-assets `
  --manifest path/to/assets.toml --output path/to/delivery
```

Delivery contains the original GLB, `models/<id>.lod.bin`, import evidence and `models.toml`. Ship the
whole set and load the generated manifest through `ModelAssetCatalog.Load`. The generated entry has
`lod = true` and `lod_file = "models/<id>.lod.bin"`. Delivery loading decodes/validates prepared indices;
it never calls the simplifier. Missing, corrupt, stale or mismatched sidecars fail explicitly. Reimport
retains the previously published slot on failure. Import settings and compiler version enter the asset
content identity, and the sidecar contains that identity plus its own SHA-256 checksum.

Runtime loading of `lod = true` without `lod_file` is rejected with `AUR-LOD-012`. Generation is an explicit
`Aurelian.AssetCompiler`/AssetTool operation; ship the generated delivery manifest. Existing manifests default to
`lod = false` and retain their previous content identity and geometry.

The renderer-neutral `ModelPrimitive.Lods` stores index arrays and object-space error estimates.
`NativeMesh3D.FromPrimitive` transports them automatically through the ordinary scene adapter.
Programmatic asset compilers can call `ModelLodCompiler.Compile` once before admitting a resident scene.

## Generation policy

The tooling-only `Aurelian.AssetCompiler` project pins MIT `Meshoptimizer.NET` 1.0.7, repository commit
`8ae428f8ef490afed28bf516723859f24cbc6ea8`, as the native binary carrier. The owned adapter calls
`meshopt_simplifyWithAttributes`, `meshopt_simplifyScale` and `meshopt_optimizeVertexCache` through the
native C ABI. [Provenance and both MIT notices](../../src/Aurelian/Aurelian.AssetCompiler/Models/Licenses/Meshoptimizer-NOTICE.txt)
are retained and copied to tooling outputs. Runtime `Aurelian.Assets` has no compiler/native meshoptimizer
reference. No Aetheris source or module was ported.

The package supplies Windows x64 and Linux x64 binaries; this qualification executes Windows x64.
Other native platforms need a supported package/build before authoring-time generation can run.
Fresh published delivery consumers and starter games contain no meshoptimizer/compiler files or dependency
entries; the delivery proof also checks loaded process modules. Existing Vulkan/driver and Skia dependencies
remain separate from this LOD boundary. See [native boundary and adaptive policy](rendering-policy.md).

Each primitive/material is simplified independently from LOD0 toward 50%, 25% and 12.5% of its original
triangle count, stopping at a 5% normalized combined-error limit. Errors are converted to object units
and made monotonic. Levels that save less than 10% versus the previous accepted level are omitted.
Failing to reach a requested ratio is allowed and visible in the import evidence.

Exact duplicate vertices may be welded for simplification, including every position/normal/UV/tangent/
colour component in equality. Open borders are locked. Attribute seams are preserved; permissive seam
crossing, isolated-component pruning and sloppy simplification are disabled. Normal weights are 0.5,
UV weights 2, tangent XYZ weights 0.5, tangent handedness and colour weights 1. All accepted levels
reference the immutable original vertex pool. Material boundaries therefore remain separate primitives,
and simplification cannot invent new vertex attributes. Transparent/refractive primitives and primitives
under 32 triangles stay at LOD0 in this version.

The native adapter admits at most one million vertices/indices per primitive before calling native
code. Existing whole-model import limits still apply. Delivery sidecars are bounded to 128 MiB, with
strict primitive coverage, decreasing triangle counts, finite monotonic errors and in-range indices.

The error value is an approximation from the simplifier's combined position/attribute metric. It is
**not a Hausdorff bound or a guarantee of an exact pixel difference**. The image comparisons below
qualify the chosen default on actual rendered specimens. See the upstream
[simplification documentation](https://meshoptimizer.org/#simplification) for the metric and seam rules.
Changing compiler parameters or native dependency requires a new `ModelLodCompiler.Version` so delivery
artifacts cannot silently retain an earlier generation policy.

## Runtime path

`VulkanSolid3DRenderer.LodPixelError` defaults to **0.25 pixels**. Set it to zero to force LOD0, or tune it
within `[0,16]` for an asset/view. This is a selection policy over prepared data, not runtime generation.
It works with grouped or bindless resident GPU draws and the visibility-buffer path. A mutable instance
list or a device/path without GPU culling uses LOD0. GPU culling's descriptor layout requires eight
storage buffers per stage/set in addition to the existing indirect-draw capabilities; lower-capability
devices keep direct retained drawing.

The game host's [Dominatus policy](rendering-policy.md) now chooses baseline, LOD, HZB or both from completed
GPU timings, with hysteresis and minimum commitment. The per-object GPU selector remains inside the chosen
quality limit. Disable `NativeGameGraphics.EnableAdaptiveVisibility` for fixed manual experiments.

The immutable GPU index buffer concatenates LOD0 and the accepted prepared ranges. Each LOD object
uses one visibility candidate, with a conservative transformed whole-object sphere, prepared ranges
and world-space error estimates. Models without prepared LODs retain their original cluster candidates
and cone culling. LOD assets trade cluster-level occlusion granularity for fewer whole-object commands.

Selection projects each world-space error using the actual world-to-clip X/Y/W rows, viewport dimensions
and a minimum W over the bounding sphere. It accounts for off-axis perspective, transform scale/shear,
and possible W changes under the estimated displacement. A sphere intersecting the near plane uses
LOD0. Transform error scaling uses the same spectral-norm upper bound as culling.

Coarsening requires an estimate below 80% of the budget; an existing level remains selected up to 100%.
Each candidate owns persistent GPU history, serialized on the graphics queue across the two frame
slots. Scene replacement initializes fresh history. Both HZB phases select the same prepared geometry;
the seed remains a predictor and the current-frame final cull rescues disocclusions as before.
The compiled frame plan declares the LOD history reads/writes explicitly.

Cull/scan/scatter emits the chosen range as a real indexed indirect command. Visibility raster identity
continues to encode the triangle's offset in the packed index buffer, so material resolve fetches that
exact prepared triangle and its original attributes. No CPU per-instance selection, geometry rebuild
or instance upload occurs in steady resident frames. Shadows and forward compatibility expansion use
LOD0. LOD switches are discrete; geomorphing/cross-fades and shadow LOD are outside this implementation.

## Qualification

Run the native specimen directly or the complete qualification:

```powershell
$env:VK_LAYER_VALIDATE_SYNC = '1'
$env:DOTNET_TieredCompilation = '0'
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll `
  --rendering-lod --output artifacts/local/rendering-r2-lod/manual
pwsh -NoProfile -File tools/aurelian-rendering-lod/Qualify.ps1
```

The complete script builds/tests `Aurelian.slnx`, tests the TS compiler, runs LOD plus the four prior R2
native proofs with synchronization validation, and compares all 225 existing captures to the preserved
pre-change binaries' output. The final ledger is
`artifacts/local/rendering-r2-lod/final/qualification.json`; its source/binary hashes identify the run.

The forest source is an actual GLB compiled by `ModelAssetBuilder` and loaded back through the generated
manifest. It contains 20,000 trees: 23.04M source triangles, including 21.44M nondegenerate triangles,
with 64 textured materials. Its prepared triangle counts are **1152 / 576 / 288 / 220**. Requested LOD3
would be 144 triangles; the seam/border/quality policy stops at 220. That limit is retained.

The final native qualification uses an NVIDIA GeForce RTX 3070 and the same resident object-culling path for both controls, forcing
LOD0 versus selecting prepared data. At 640×360 with a 1000-metre far plane, the elevated forest view
submitted **18,041,472 → 6,969,888 triangles** (61.4% fewer), with the same accepted object count.
Complete GPU frame medians in ABBA order were **6.540 / 2.183 / 2.008 / 4.511 ms**. CPU prepare/record
remained 0.468–0.555 ms with zero resident instance visits/uploads. Throughput was **150.2–216.9 FPS** at
LOD0 versus **440.4–487.0 FPS** with prepared selection. These are workload/device results, not a claim
that every view improves by the same factor.

Across four views at the default budget, full-frame RGB mean absolute error versus LOD0 was at most
**0.690/255**; changed foreground/background coverage was at most **0.0599%**. The fixed gates are
MAE ≤1.2/255 and changed coverage ≤2%. The mixed-scale/sheared specimen uses LOD0/0/2/3. Its visibility
resolve matches explicit selected-geometry material rasterization to **0.000165/255** mean RGBA error;
8,443 foreground identity pixels point into their selected prepared ranges. Independent indirect-command
readback checks index counts, first indices, first-instance IDs and stable compaction order.

Additional native checks cover exact HZB images, exact HZB shadow/temporal images after matching history
resets, queued camera cuts, twelve stable threshold-jitter samples, refinement outside the hysteresis
band and near-plane LOD0 retention. These image gates are specimen evidence, not universal bounds for
all topology, textures, animation or camera projections.

Rejected runs remain under `artifacts/local/rendering-r2-lod/experiment-*`. The first timing setup
incorrectly reused a 95-metre far plane and submitted no forest geometry; it is rejected, and timing now
requires at least 10M LOD0 / 1M selected triangles. Budgets of 1 and 0.5 pixels failed the fixed distant
forest image gate (MAE 2.213 and 1.517/255). The default was reduced to 0.25, preserving the quality gate.

Prepared indices increase asset/residency size; all levels remain resident. This step does not add
streaming, runtime cluster regeneration, seamless cluster-group boundaries, authored multi-mesh LOD import,
skinned LOD, compute material scheduling or hybrid analytic visibility. Full R2 remains open for its
remaining research topics; offline asset LOD and runtime selection are now implemented.
