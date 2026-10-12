# TRELLIS.2 learned mesh and PBR material execution

This closes the decoder execution boundary left by the first sparse subdivision
experiment. The generated pot's existing DINO, occupancy and shape artifacts now
continue through every learned mesh decoder stage, texture sampling and texture
decoding, GLB compilation, the normal Aurelian importer and Vulkan rendering.
The motivating witness is an actual generated PBR pot, not exposed voxel faces
with a manually selected terracotta color.

## Runtime ownership

Learned arithmetic executes through C#/Vulkan and the existing Dominatus stage
and feedforward controllers. CPU code owns sparse coordinate dictionaries,
subdivision maps, dual-grid triangulation and asset compilation. Python/NumPy is
an explicitly invoked offline oracle; inference never launches it, Torch or CUDA.

`CreateSparseTensor` provides explicit resident feature/topology allocations;
ordinary `CreateTensor` keeps its 8,192-row contract. Attention retains its own
token bounds. Decoder convolutions use 2,048-row im2col tiles and streamed weight
tiles. MLP expansions use at most 8,192 rows. Sparse channel-to-spatial gathers
consume parent tiles directly and preserve X-low-bit child ordering and repeated
residual channels. Expanded eight-child convolution matrices are never allocated
for the whole parent grid.

The final grid has 1,627,472 rows, so the decoder explicitly opts into a
134,217,728-element allocation budget. Every buffer still passes the device's
storage-buffer range check. CPU topology is bounded at 4,194,304 rows. General
batch dispatches reject oversized workgroup dimensions; normalization uses a
two-dimensional dispatch. These bounds are not an unlimited-memory policy.

## Actual mesh result

Learned grid rows: **5,870 -> 25,365 -> 101,953 -> 405,787 -> 1,627,472**.
All 32 ConvNeXt blocks and four upsampling blocks execute. The final seven
channels encode three vertex offsets, three intersection flags and the learned
dual-quad diagonal weight. CPU extraction follows the pinned inference equations.

The preserved full OBJ has **1,627,472 vertices and 3,254,112 triangles**. There
are 276 predicted edge quads with absent neighbors; these are reported and skipped
as upstream extraction does. This is not a watertightness certificate, and the
upstream hole-filling postprocess is not implemented here.

The renderer's existing one-million expanded-vertex frame budget requires a
preview LOD. Deterministic 128-cell-per-unit vertex clustering produces 100,015
shared vertices and 199,509 triangles, with measured maximum source-to-cluster
position deviation **0.0068353 model units**. The original geometry and voxel
attributes remain untouched. Export expands that LOD to 598,527 vertices for
per-triangle material atlas UVs.

The full mesh decoder's independent NumPy comparison passed at **0.045433%**
relative RMS and **99.999898%** intersection sign agreement. The oracle uses
recorded native subdivision guides to isolate feature arithmetic, checking
predicted child signs separately. Later child-sign comparisons are greater than
99.99%; this does not imply a byte-identical free-running topology trajectory.

## Actual material result

The pinned texture flow consumes 32 noisy material channels concatenated with
32 renormalized shape channels on the same 5,870 coordinates. It uses the existing
30-block flow graph, all 1,029 DINO tokens and twelve Euler steps with time rescale
3 and guidance strength 1. Unity guidance needs twelve positive evaluations and
no redundant negative predictions. Measured material sampling time is **763.0 s**;
this pass also ran mesh/oracle/regression work concurrently, so this is not a clean
throughput benchmark.

The 284-tensor texture decoder reuses the four shape subdivision guides, executing
the same bounded sparse operators. It returns six channels: base-color RGB,
metallic, roughness and opacity. The upstream `value * .5 + .5` conversion is
preserved in `voxel-attributes.f32`. Asset compilation clamps values to the glTF
unit range and aggregates attributes through the explicit LOD source mapping.
The raw decoded features remain available for numerical qualification.

The GLB embeds 2048 x 1560 base-color/opacity and metallic/roughness PNGs with
four-texel triangle patches, linear sampling without mipmaps and glTF's roughness-G,
metallic-B convention. This is a bounded preview atlas, not a production UV unwrap:
LOD aggregation, 8-bit quantization and clamped patch interpolation lose detail.
Opacity is retained in the PNG; this particular pot imports as OPAQUE.

The actual GLB passed `GlbModelImporter` and rendered from its imported materials
through `VulkanSolid3DRenderer`. Material decoding plus asset compilation/rendering
took **87.7 s**. The largest measured owned GPU allocation high-water mark was
**1,898,787,696 bytes (about 1.77 GiB)**. It includes owned staging and dispatch
buffers, not opaque driver or other application VRAM. Shape decoding performs four
subdivision readbacks plus final output; guided material decoding performs one
final output readback. No hidden per-layer readbacks were added.

The complete material decoder also passed the independent NumPy oracle at
**0.051369% relative RMS** on the shape's recorded subdivision guides. Its separate
`decoder-reference-evidence.json` owns this numerical qualification; the original
execution evidence does not pre-assert a later oracle result.

## Reproduce

```powershell
dotnet build tools/Aurelian.GraphicsProof/Aurelian.GraphicsProof.csproj -c Release -m:1
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll --trellis2-finalize
```

The combined command validates/downloads pinned material checkpoints, then runs
mesh decoding, material sampling and material decoding/export. It defaults to
the existing qualified DINO and completed shape artifact directories and writes
`artifacts/local/trellis2/completed/asset/pot.glb`. It does not recompute unchanged
upstream artifacts. This turn qualified the same three real stages individually;
the combined command is a sequential composition of those entrypoints.

Separate flags remain available: `--trellis2-decode-mesh`,
`--trellis2-fetch-material`, `--trellis2-sample-material`, and
`--trellis2-decode-material`. Use `--weights-dir`, `--sample-dir`,
`--features-dir`, `--mesh-dir` and `--output` to select explicit inputs.

The current witness is under `artifacts/local/trellis2/material/decoded/`:
`pot.glb`, three material screenshots, six-channel features/attributes and evidence.
Mesh evidence/full OBJ are under `artifacts/local/trellis2/shape/decoded-mesh/`.
All weights and generated artifacts are ignored, local-only files.

## Qualification and next optimization boundary

Final `dotnet build Aurelian.slnx -c Release -m:1` passed with zero warnings and
errors. All **1,369** tests in the 30 Aurelian solution test assemblies passed,
without skips. Native validation on the RTX 3070 passed **55** compute reference
cases and **nine** sparse-kernel numerical cases, including 70,003-row normalization,
global gathers across 8,192/65,536-row boundaries, child-slot/repeated-channel
gathering and parent-tile/destination offsets. The final proof project also builds
cleanly after updating prefix diagnostics to point at the now-working full path.

Execution success is distinct from complete upstream numerical parity. The prior
occupancy final velocity (1.130% RMS) and shape CFG comparison (2.316% RMS) still
exceed the retained 1% bounds. The texture flow has no independent full-sampler
qualification yet. `FullGenerationQualified` therefore remains false.

Useful next optimization targets are the already measured tiled-attention and
weight preparation/submission costs, sparse neighborhood construction, indexed
native model drawing instead of preview expansion, and better mesh/material LOD
compilation. No precision threshold was relaxed to call this execution successful.

Code/model revision and MIT attribution remain in `tools/trellis2/TRELLIS-NOTICE.txt`.
The separate DINO weight license remains unchanged.
