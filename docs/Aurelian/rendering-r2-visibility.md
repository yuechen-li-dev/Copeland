# R2: typed visibility targets and primitive identity

The native renderer now has a selectable visibility-buffer path. It rasterizes instance, triangle and
facing IDs into an eight-byte unsigned target, reconstructs vertex attributes from those IDs, evaluates
the shared material implementation once per visible pixel, and feeds the existing surface-lighting,
shadows, temporal AA and output passes.

The local experiment shows a useful workload tradeoff: the unculled many-material Forest improves
from approximately 4.86 ms to 3.73 ms GPU time, and 128 opaque overlapping layers improve from 2.30 ms
to 0.51 ms. The already cluster-culled Forest is faster with material rasterization: about 0.60 ms
versus 0.67 ms with visibility resolve. The existing material path remains the default.

## Select the path

```csharp
var scene = new Native3DScene([], [])
{
    Instances = new NativeInstanceSet3D(authoredInstances),
};
renderer.EnableVisibilityBuffer = true;
renderer.Render(scene, worldToClip, eye, clear);
```

`NativeGameGraphics` registers both new shaders; its public `Renderer` exposes the selector. Direct
construction supplies `RetainedModel3DPrograms.VisibilityBuffer` with compiled `VisibilityRaster3D.v.ts`
and `VisibilityResolve3D.v.ts` programs alongside the existing bindless program. Unsupported devices,
missing programs, mutable instance lists and mixed geometry adapters receive `AUR-VBUFFER-006` when
visibility mode is requested. Opaque/MASK static resident geometry is the admitted scope. Scene revision
and mode changes use the existing frame-slot and timeline retirement machinery.

## Identity is integer data

`graphics.visibility.m1` extends the owned shader path through the Copeland binder, VD-MIR,
compiled graphics contracts, DXC/SPIR-V and Vulkan resources. It adds `uint2`/`u32` pixel targets,
`Texture2D<uint2/u32>` integer texel loads, explicit `SampleGrad`, scalar integer arithmetic/conversion,
and stage-checked `primitive_id` and `draw_id` builtins. Integer textures cannot use filtered `Sample`.
The Vulkan formats are `R32G32_UINT` and `R32_UINT`; clears and target validation preserve scalar types.

The visibility target stores `(packedInstance + 1, meshTriangle * 2 + backFaceBit)`. Zero in the first
word means background. The immutable admission limits keep both words within 32 bits. No float
encoding, interpolation or texture filtering transports IDs. A native transport probe verifies
16,777,217 and 0xffffffff exactly, including the value beyond float32's consecutive-integer range.

Primitive IDs restart for every indirect cluster draw and instance. The vertex stage reads `draw_id`,
looks up that compacted command's `firstIndex`, and passes `firstIndex / 3` flat to the pixel stage.
Adding the fragment primitive ID yields a triangle ID within the original mesh. A per-instance atlas
record carries mesh vertex/index bases and the command-run offset. This handles multiple meshes,
nonzero first-instance offsets and compaction without CPU visibility readback. See
[Vulkan PrimitiveId](https://docs.vulkan.org/refpages/latest/refpages/source/PrimitiveId.html) and
[DXC's DrawIndex mapping](https://github.com/microsoft/DirectXShaderCompiler/blob/main/docs/SPIR-V.rst).

Vulkan requires the Geometry SPIR-V capability for this fragment primitive builtin. The plant queries
and enables the corresponding device feature, plus shader draw parameters; it does not insert a geometry
shader stage. Bindless support, storage/image descriptor limits and integer attachment support also
gate admission. See [device feature definitions](https://docs.vulkan.org/refpages/latest/refpages/source/VkPhysicalDeviceFeatures.html).

## Reconstruction and shared materials

The first pass does face/MASK coverage and writes identity plus depth. Opaque materials skip the base-map
coverage sample. The resolve is a fullscreen fragment pass: it loads integer IDs, fetches the triangle's
three original vertices and static instance transform, and reconstructs perspective-correct attributes.
Homogeneous barycentrics avoid dividing by vertex w when a triangle crosses the eye or near plane.
Normal and tangent transforms/normalization occur per source vertex before interpolation, preserving
the existing vertex-stage semantics. The resolve also writes reconstructed depth and temporal motion.

The original grouped/bindless shader and visibility resolve call the same `EvaluateSurfaceSamples`
implementation in `SurfaceMaterial3D.v.ts`. Sampling is selected by the caller. Visibility rasterization
and the material path share `TestSurfaceCoverage`; resolve does not repeat the alpha test using a
different texture footprint.

We tried analytic UV derivatives first, then reconstructed a complete two-by-two pixel quad from the
hit triangle, including its helper lanes across visibility-ID boundaries. Finite quad differences reduce
texture footprint error relative to the original raster sampler. The quad variant reduces RGB RMS error
in all twelve Forest views with effectively unchanged GPU time. Analytic derivatives remain the fallback
when the extrapolated quad has a zero denominator. Neighboring pixels with unrelated IDs never supply
derivatives to this triangle's resolve.

The fullscreen resolve realizes the visibility-buffer separation described by
[Burns and Hunt](https://jcgt.org/published/0002/02/04/) within the current graphics backend. Compute
material classification/resolve scheduling remains a later experiment; no compute image ABI or material
binning is claimed here. The existing surface lighting remains the consumer of resolved surface data.

## Resources, tape and costs

The frame tape declares `cluster-visibility` when culling is active, then `visibility-raster`, followed
by `visibility-material-resolve` and existing surface consumers. Integer identity production/consumption
has an explicit resource edge. The compacted command mirror copy and its compute-to-transfer-to-vertex
barriers stay inside the identity stage. Named GPU timing includes `visibility-raster` separately.

The immutable native transport atlas holds original vertex/index data and 160-byte instance records.
The existing bindless table remains the sole material table and borrows the existing texture/mipmap
owner. Native admission verifies every transport field, stride and descriptor declaration against the
compiled program. Each frame slot owns its command mirror and frame uniform. Resource replacement
retires after the actual graphics timeline use.

This first implementation duplicates resident geometry in its read-only transport atlas, bounded to
256 MiB, and copies the full command capacity for every active mesh run on the GPU. The Forest copies
3.6 MB of command data per culled frame. Those costs are included in the measurement and help explain
the small-scene/strongly-culled regression. Identity storage at 640x360 is 1,843,200 bytes, plus a separate
depth target and the transport tables. Streaming, dynamic transforms, transparency, compute material
binning and HZB are outside this implementation. No Aetheris source or dependency was added.

## Qualification

```powershell
pwsh -File tools/aurelian-rendering-visibility/Qualify.ps1 `
    -Baseline artifacts/local/rendering-r2-visibility/before `
    -Output artifacts/local/rendering-r2-visibility/final
```

The baseline binaries were saved outside the repository from `938254da` before editing, and all nine
existing proof modes were captured. Qualification builds/tests `Aurelian.slnx`, runs the full compiler
test project, runs visibility, bindless and clustered native proofs under synchronization validation,
then compares all 225 existing captures. The command never regenerates the baseline. Logs, source/binary
hashes and machine-readable acceptance accompany the result.

New qualification gates include:

- All twelve Forest views and the boundary scenes stay within a frozen RGB budget: RMS <= 1 byte,
  mean absolute error <= 0.25 byte, and at most 0.1% of channels differing by more than three bytes.
  Maximum error is reported, including sparse nearest-sampling boundary changes.
- A white-foreground debug rendering matches byte-for-byte for every comparison, proving coverage and
  MASK holes independently of RGB shading differences.
- Direct and compacted cluster draws produce identical complete identity images for the Forest and
  a three-run/two-mesh fixture. An independent double-precision ray/triangle oracle checks 433 triangle
  interiors across 417 distinct triangles, including triangle 256 and both facing directions, plus
  8,733 background samples. Fifty near-edge samples are explicitly excluded from that grid oracle.
- A colored triangle crossing the eye/near plane has 1,440 additional oracle interior hits. A globally
  scaled clip matrix, shadows/TAA, static revisions and two in-flight revision replacements also pass.
- Full-width unsigned attachment transport and shader/native type rejection are exercised.
- Both ABBA visibility measurements must beat the fastest raster control by at least 20% on overlapping
  layers and 5% on the unculled 512-material Forest. The culled negative control remains in the ledger.

Timings use the production native frame at 640x360 on an RTX 3070, tiered compilation disabled, ABBA
order, 128 warmup frames and 96 measured frames. They include the complete configured GPU frame and
record CPU preparation, submit and drained frame-loop throughput separately. Admission/upload,
qualification readback, application projection and swapchain presentation are excluded. GPU clocks
remain unlocked. Timing cases disable shadows, TAA, AO and bloom; separate boundary scenes exercise
those supported consumers. Existing captures retain their exact-pixel gate.

The accepted quad experiment is under `artifacts/local/rendering-r2-visibility/quad/`; the initial
analytic experiment is under `native/`, with its shader source saved as `analytic-resolve.v.ts`.
Earlier diagnostic directories retain the timing-schema/readback/fixture failures. The final complete
qualification ledger is `artifacts/local/rendering-r2-visibility/final/qualification.json`.

## Final-source measurements, 2026-11-03

The full build passes with no errors and 14 warnings in unmodified upstream font code. All 1,578
Aurelian tests and 1,730 compiler tests pass, with no skips. Visibility, bindless and clustered native
proofs pass synchronization validation. All 225 existing PNG captures are byte-identical to the
pre-change baseline. The final-source controls retain both repetitions:

| Workload | Material-raster GPU frame | Visibility GPU frame | Material-raster frame-loop FPS | Visibility frame-loop FPS |
| --- | ---: | ---: | ---: | ---: |
| Forest, 1 material | 4.907–4.910 ms | 3.644–3.646 ms | 198.9 | 267.7–267.9 |
| Forest, 64 materials | 4.862–4.865 ms | 3.702–3.714 ms | 200.7–200.9 | 262.9–263.5 |
| Forest, 512 materials | 4.859–4.867 ms | 3.723–3.736 ms | 200.4–200.9 | 260.8–262.2 |
| Opaque layers, 128 | 2.299–2.302 ms | 0.507 ms | 423.5–424.2 | 1778.1–1837.5 |
| Cluster-culled Forest, 512 materials | 0.600–0.607 ms | 0.670 ms | 909.6–1588.4 | 1386.2–1404.9 |

The first culled raster throughput sample is an outlier at 909.6 FPS despite a 0.607 ms median GPU
interval; its repeated control is 1588.4 FPS. It remains in the ledger. The repeated GPU intervals
consistently show a small visibility regression in that culled workload. CPU preparation remains
independent of instance count through the resident boundary; visibility adds a pass and can slightly
increase recording cost. The experiment demonstrates GPU rendering wins, rather than a CPU-speed claim.

Across the twelve final Forest comparisons, RGB RMS error is at most 0.2371 byte per channel. Sparse
local texture-boundary differences reach 76 bytes; at most 0.01013% of RGB channels differ by more than
three bytes. All full-frame coverage comparisons are exact. The grid and scaled-clip comparisons are
byte-identical; the colored eye-plane crossing differs by at most one byte. Source images, hashes,
raw identity comparisons, oracle counts and per-pass timings are in the native evidence.

[Two-phase HZB](rendering-r2-hzb.md) now builds on this depth/identity path. It uses GPU depth-buffer
copies and storage-buffer reductions, conservative current-frame rescue and a stable final raster.
Compute material scheduling, LOD and the optional analytic comparison remain subsequent experiments.
