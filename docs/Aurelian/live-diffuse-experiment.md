# Live diffuse probes and reflection refitting

This bounded experiment separates GPU visibility, cached surface shading, and
irradiance reconstruction. It is inspired by Tiago Sousa's
[idTech 8 SIGGRAPH 2025 presentation](https://advances.realtimerendering.com/s2025/content/SOUSA_SIGGRAPH_2025_Final.pdf),
especially slides 15–22 and 26. It also tests a compilation-oriented extension:
retain exact visibility connections for explicitly authored receiver sites.

**Success for the research experiment on the RTX 3070.** This does not enable a
new default GI implementation in games. It is not a complete idTech reproduction,
a converged path tracer, or a whole-game performance qualification.

## Owners and execution

- `Rendering.Contracts/Lighting/DiffuseProbeGrid.cs` owns the finite grid,
  constant Lambertian triangle and point-light declarations. Dimensions,
  finite values, sampling budgets and material ranges are validated explicitly.
- `VulkanDiffuseProbeVolume` owns retained GPU resources and bounded updates.
  It uses the existing Vulkan plant, allocator, command pool and submitter.
- `VulkanRayQueryScene.TraceTexture` makes the existing hardware traversal
  packet directly available to graphics shaders. The original CPU `Trace` API
  retains its geometry, filtering and stable-tie behavior.
- `Diffuse*.v.ts` contains the actual cache, probe, visibility, decoder and
  reflection source. Compilation follows Copeland GPU binding → canonical
  VD-MIR → Aurelian HLSL/DXC → validated Vulkan SPIR-V. No generated-source edits.
- `LiveDiffuseExperiment` owns the fixture, negative specimens, captures and
  independent brute-force CPU qualification oracle. The oracle never supplies
  runtime shading or visibility to the GPU update path.

There are no downloads, added dependencies, changes in other repositories, or
new license combinations. The fixture reuses `SceneCompiler` and
`SceneGeometry3D`; this experiment adds no second scene-geometry authoring path.

## Bounded GPU pipeline

The fixture has 60 triangles, 144 probes, 64 fixed spherical directions per
probe and a 16×16 surface grid per triangle. Authored triangle identity and
barycentrics locate surface-cache entries. This direct atlas avoids hash
collisions and mixed primitive identities. It is not idTech's demand-driven,
spatially hashed world cache.

Surface shading stores emission plus shadowed outgoing Lambertian point-light
radiance. Hardware ray queries determine direct visibility at cache sites.
Probe queries then reuse that radiance, with bilinear surface reconstruction.
Each probe reconstructs an 8×8 octahedral irradiance tile and directional first
and second hit-distance moments. Irradiance stores **E/π** in scene-linear RGB;
a diffuse receiver multiplies it by albedo. Misses see a black environment in
this witness. There is one indirect bounce and no temporal multibounce feedback.

The decoder blends eight adjacent probes using spatial weights, normal weights,
and a moment-based visibility estimate. Back-facing hits invalidate interior
probes; receivers ignore invalid neighbors. Out-of-domain, unavailable or stale
data returns an explicit invalid result. This is conservative rejection, not
probe relocation or a proof for arbitrary open/nonmanifold meshes.

At the selected budget a generation takes fifteen surface batches of 1,024
cells followed by five probe batches of at most 32 probes: twenty host ticks.
At a 60 Hz host cadence that would be about one third of a second. These stages
run synchronously on the existing graphics queue. Updates perform no hit or
lighting-image readback, but they do upload CPU-authored ray descriptors and
wait for each submission. This is not async compute or a 60 FPS dynamics claim.

`SetLight` immediately increments the generation and withdraws readiness.
Shading and decoders reject previous generations. Stable completed scenes
dispatch no further work. Geometry changes require constructing a replacement
owner with the new triangle scene; incremental BLAS refits are not implemented.
Native update cursors are plant state, not a new application agent runtime.
Dominatus publication, inspection and regional scheduling remain a production
integration step, using the existing lighting controller infrastructure.

## Exact receiver connections

Distance moments do not resolve every thin-wall case. `CompileConnections`
traces eight receiver-to-probe segments on the GPU and writes a mask for each
explicitly authored point/normal. The connected decoder excludes obstructed
probes. It performs no further visibility tracing when reusing those masks.

The mask is valid for those exact sites, grid, geometry and generation. Moving
the receiver or changing geometry requires another compilation. It is not a
universal visibility field and is not automatically interpolated onto arbitrary
pixels. Conservative generation tagging currently also invalidates it on light
changes. The ordinary diagnostic room view uses moment interpolation; the
connection improvement is qualified separately at nine receiver sites.

This is a promising next extension to compiled scene artifacts: retain useful
visibility information alongside fitted radiance, rather than expect a compact
radiance fit or two distance moments to rediscover it.

## Reflection experiment

The harness captures scene radiance from one environment-probe position using
the actual GPU surface cache, then uses the existing GPU GGX/environment compiler
to generate the normal engine atlas. This explicit one-time authoring step reads
back the captured image for the existing compiler's input API. Live updates do
not recapture, rebake, or read back that environment.

The reflection experiment applies:

```text
captured specular × current local E/π ÷ captured environment E/π
```

Captured irradiance comes from the environment atlas's cosine diffuse band,
not from a copy of the live receiver field. The reusable VTS helper guards each
denominator at 0.01 and caps gain at 4. This is a bounded relighting heuristic.
It changes brightness and bounce color, but cannot create new reflected geometry,
restore missing directional lighting, or replace physical specular transport.

## Qualification results

Local evidence: `artifacts/local/live-diffuse/evidence.json` and `comparison.png`.
The evidence retains the actual scene variants, grid/light inputs and program
content keys. `programs/manifest.json` names thirteen retained SPIR-V stages
whose bytes are checked against the compiled checksums; the embedded VTS sources
are retained alongside them. Files outside that manifest are not qualified stages.

| Witness | Result |
| --- | --- |
| Room diffuse versus direct-only | 19,815 pixels materially changed |
| Matched-quadrature independent per-hit reference | 9,216 primary rays; cache RMSE 0.01250; relative RMSE 5.60%; max channel error 0.14297 |
| Hit reuse at referenced surface cells | 4,981 hits reference 1,282 nearest cells; 3.89 hits/cell |
| Nine thin-wall sites, unweighted probes | RMSE 0.12009 |
| Same sites, distance moments | RMSE 0.10428 |
| Same sites, compiled connections | RMSE 0.01996, about 83.4% below unweighted; 72 one-time connection rays |
| Lamp off | Exactly zero probe radiance and refitted foreground energy |
| Moved lamp | 25,144 changed pixels |
| Replacement wall | Maximum probe response change 1.07077 |
| Body moved onto a probe | One interior probe rejected |
| Constant-field GPU reconstruction | Zero observed error |
| Outside domain / stale generation | Invalid output; no old energy sampled |
| Reflection identity, color scaling, zero lighting, gain guard | Zero observed error against the stated heuristic |

The thin-wall reference uses 2,048 fixed spherical directions per site and an
independent CPU ray/shading implementation. It is finite single-bounce quadrature,
not an exact path-traced oracle. All nine connected sites produce valid lighting;
the error reduction is not obtained by falling back to black.

The atlas reference deliberately matches the GPU's 64-direction quadrature to
isolate cache approximation. It does not measure sampling error against a
converged integral. Reported hit reuse is potential sharing, not a speedup:
the prototype shades all 15,360 atlas cells per generation, including unused
cells, which exceeds the 4,981 hit count in this fixture. Demand-driven active
cell shading is a useful subsequent experiment.

In the recorded run, GPU timestamps for visibility plus one update reconstruction
batch gave approximately 0.030 ms median and 0.067 ms p95. These exclude CPU ray
generation/upload, submission/wait, BLAS construction, environment acquisition,
diagnostic readback and rendering the room view. They are not frame timings or
a comparison against a runtime path tracer. Cold CPU tick time is reported
separately in the evidence.

Captures use a diagnostic cache-based ray view. Coarse direct-light/shadow detail
in those captures is not a change to the game's raster shadow or TAA paths.
Screen-space gather, screen-space radiance reuse, radiance hashes, cascaded grids,
textured/skinned surfaces, transparent receivers, froxel fog and multi-GPU work
remain outside this experiment.

## Reproduce

```powershell
dotnet build Aurelian.slnx -m:1
dotnet run --project tools/Aurelian.GraphicsProof --no-build -- --live-diffuse
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx --no-build -m:1 -- RunConfiguration.MaxCpuCount=1
dotnet test tests/Copeland/Copeland.TS.Tests -m:1
```

Full Aurelian validation passed 1,059 tests across 28 projects; Copeland TS passed
1,469 tests, with no failures or skips. The final solution build reported zero
warnings/errors. Six new shader specimens compile to validated SPIR-V through
the usual source loader and exporter. Contract tests cover budgets, corner
ordering, boundary indexing, degenerate geometry and invalid materials.
The final focused shader run passed all 202 tests with the same installed Naga
path. The existing `Aurelian.Spatial3DProof` also passed: all 4,096 CPU/GPU rays
agreed, with filtering, stable ties, shared triangle edges, analytic spheres,
doorway traversal and wall blocking preserved. Its input was the existing
`artifacts/aurelian-spatial3d/room-rebaked.json`; output is under
`artifacts/local/live-diffuse-spatial-regression`.
