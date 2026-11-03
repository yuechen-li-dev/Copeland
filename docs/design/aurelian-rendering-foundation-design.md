# Aurelian rendering foundation: closing the gap

Status: initial design for a first implementation pass. Written 2026-11-02 against `main` at `71b795de`.
Audience: the implementer (Codex) and Yuechen. Parts marked **Aurelian proposal** are unproven ideas with
an experiment that can falsify them; parts marked **baseline** are established industry practice.

## 0. How to use this document

Work proceeds in milestones R0–R8 (section 4). Each milestone lists what "done" means, what not to do, and
when to stop. The [convergence rule](../../AGENTS.md) applies to every milestone: end in success,
meaningful progression with the next blocker isolated, or an honest stop. A milestone that produces passes
without moving its motivating specimen has not converged.

Two rules are stronger than everything else in this document:

1. **Never lose an existing proof.** Every current graphics proof (`tools/Aurelian.GraphicsProof`, the
   pack qualification commands in `docs/Aurelian/*-graphics-pack.md`, the diffuse lab, rain, water,
   Cloudstep and Beacon captures) must keep passing at every commit. If a refactor changes pixels, the
   change must be explained and the evidence regenerated deliberately, never silently re-baselined.
2. **Do not copy a technique without its comparison.** When this document names an industry baseline and
   an Aurelian proposal, both are implemented to the point of measurement against the reference integrator
   (R3a), and the evidence decides. "Unreal does it this way" is a reason to build the control, not a reason
   to ship it.

## 1. Thesis and non-goals

**Aurelian renders the world as an eye sees it, not as a camera films it.** It aims for the capacity to run
the most advanced real-time rendering research, and for Aurelian-original techniques that beat it, with
every approximation measured against an in-engine ground truth.

### 1.1 Prohibited effects

These are not to be implemented as engine features, settings, or post-processing passes:

- depth of field and bokeh,
- motion blur (camera or per-object),
- lens flare, lens dirt and ghosts,
- film grain and noise overlays,
- chromatic aberration,
- vignetting,
- anamorphic streaks and other lens-specific artifacts.

They simulate a recording device, throw away detail the renderer paid to produce, hide rendering defects
(grain hides banding, motion blur hides low frame rate, DoF hides weak distant detail), confound image-based
qualification, and harm readability and comfort.

These remain, because they model the eye or the display rather than a lens:

- **Bloom, reframed as ocular glare** (R7): scattering inside the eye around very bright sources.
- **Exposure adaptation** (R7): the eye adapts; a fixed exposure is the unnatural choice.
- **Motion vectors**: required for temporal reconstruction. They are never used to smear the image.

If a game needs one of the prohibited looks for a diegetic reason (an in-world camera, a scope, a dizziness
condition), that game authors it as an effect owned by that feature. It is not added to
`Graphics3DSettings`.

### 1.2 Non-goals for this design

- A clone of Nanite, Lumen or Virtual Shadow Maps. Their problems are in scope; their solutions are
  baselines to compare against.
- Mobile GPUs, DirectX 12 and Metal backends.
- Changing simulation identity, saves or replay. Rendering stays outside simulation state, as today.
- Content scale tooling (world partition, streaming editors). R2 builds the GPU side that streaming will need;
  the streaming system itself is a later design.

## 2. Ground truth: the renderer today

Verified in code on `71b795de`:

| Area | Current state | Where |
| --- | --- | --- |
| Frame submission | Each 3D frame records one command buffer and submits with `WaitForCompletion: true` (5 s timeout). CPU and GPU never overlap; there are no frames in flight. Eight renderers block this way. | `VulkanSolid3DRenderer.RenderCore`, `VulkanNativeFrameTarget`, `VulkanOrderedQuadRenderer` and others |
| Pass structure | One ~400-line `RenderCore` method hard-codes the pass order: particles, shadow map, three cascades, spot shadows, scene, surface resolve, refraction, transparency, bloom, tone map. | `Native3D/VulkanSolid3DRenderer.cs` |
| Geometry | Immediate geometry: CPU vertex arrays, max 65,536 vertices. Models: `NativeModel3DBatch(NativeModel3DVertex[] Vertices, ModelMaterial)`, unindexed, finite-checked per vertex on the CPU every frame, re-uploaded, 1M-vertex frame budget. No instancing, LOD, frustum or occlusion culling. | `Native3DContracts.cs`, `VulkanModel3DBatches.cs` |
| Draws | One non-indexed `DrawVertices` per material batch, one descriptor set bound per material. | `VulkanModel3DBatches.Draw` |
| Descriptors | Per-material descriptor sets; no descriptor indexing or bindless tables. | Pipelines, `VulkanModel3DBatches` |
| Queues | One queue family is selected. Timeline semaphores exist (`VulkanTimelineFence`) but are used as CPU-side fences, not for GPU-to-GPU dependencies between queues. | `Device/VulkanPlantInitializer.cs`, `Sync/VulkanTimelineFence.cs` |
| Ray tracing | Optional Vulkan 1.3 ray query, acceleration structure and buffer device address. A retained BLAS/TLAS with batched inline traversal exists, used for spatial queries and the diffuse probe experiment, not for frame lighting. | `RayQueries/VulkanRayQueryScene.cs`, `Native3D/VulkanDiffuseProbeVolume.cs` |
| Lighting | GGX plus switchable diffuse (PathDiffuse default); three 2048² cascades with 3×3 PCF; up to 32 tiled local lights; two 512² spot shadows; no point shadows; procedural or prefiltered environment and one box probe; opt-in horizon AO; compiled static diffuse for a bounded receiver; probe GI as an experiment only. | `docs/Aurelian/graphics-starter.md`, `surface-graphics-pack.md`, `presentation-graphics-pack.md` |
| Presentation | TAA, HDR bloom, a fitted filmic curve, manual exposure. | `temporal-graphics-pack.md` |
| Shaders | Visual TypeScript (`*.v.ts`) through Aurelian.Shaders to HLSL and SPIR-V (DXC), and to WGSL (Naga, browser regression). Cooperative matrices are available as an option. | `src/Aurelian/Aurelian.Shaders`, `VulkanPlantInitializer.Cooperative.cs` |
| Evidence | Fail-closed proofs, GPU timestamps per named pass, deterministic captures and hashes. | `tools/Aurelian.GraphicsProof`, `Vulkan3DGpuTimings.cs` |

The research packs are good. They stay bounded ("one static floor receiver", "one volume", "32 lights")
because each one is added inside a CPU-submitted, single-queue, forward-style frame with no shared scene
representation. Closing the gap means building that substrate (R0–R3), then promoting the research onto it.

## 3. Architecture principles

1. **Capture mode and interactive mode are different.** Proofs need blocking, deterministic, readable
   frames. Games need pipelined frames. Both run the same passes; only scheduling and readback differ.
2. **Deterministic stochastic rendering.** Every random sequence (blue noise, light selection, ray
   directions) is a pure function of `(frame index, pixel, pass id, scene revision)`. Stochastic techniques
   then stay replayable and their captures stay hashable on one device. Industry engines do not guarantee
   this; Aurelian should, because its whole evidence culture depends on it.
3. **One scene representation feeds raster and rays.** The instance table that drives GPU culling (R2) also
   builds the TLAS (R3). There is no separate "ray tracing scene" kept in sync by hand.
4. **Budgets are explicit and overflow is a diagnostic.** As today (`AUR-LIGHT-LOCAL-003` and similar):
   exceeding a budget fails loudly or degrades by a documented rule. Nothing clamps silently.
5. **Capability tiers, not one path.**
   - **Tier R**: Vulkan 1.3, descriptor indexing and hardware ray queries (RTX 20-series, RDNA2 and later).
     This is the target.
   - **Tier V**: Vulkan 1.3 without ray tracing. Raster shadows, probes and compiled lighting.
   - **Tier W**: WGSL/WebGPU through the existing Naga path. No bindless arrays, multi-draw or ray queries.

   Each feature declares its tiers. Tier R is not allowed to make tier V crash; it may make it look plainer.
6. **The renderer stays readable.** AGENTS.md applies to rendering code: no 400-line methods in new code,
   explicit pass types, named resources. R1 exists partly to dissolve `RenderCore`.

## 4. Milestones

Dependency order: R0 → R1 → (R2, R3a) → R3 → (R4, R5) → R6 → R7, R8. R7 and R8 may start any time after R1
for parts that do not depend on R2/R3.

**First pass for Codex: R0, R1 and R3a.** They are low risk and unblock everything else. R2 follows
immediately after.

---

### R0. Pipelined frames and a retained scene

**Problem.** The CPU waits for the GPU every frame, and model geometry is re-validated and re-uploaded
every frame.

**Design.**

- Introduce `FrameInFlightSet` with `N = 2` slots. Each slot owns its command buffer, per-frame uniform
  buffers, descriptor sets that change per frame, and a timeline semaphore value. A frame begins by waiting
  for slot `i`'s value from `N` frames ago, not for the frame just submitted.
- `Native3DFrameMode { Interactive, Capture }`. Capture keeps today's blocking submit plus readback; the
  native game host uses Interactive. Every proof runs in Capture mode and must produce identical hashes
  before and after R0.
- **Retained geometry.** Add `GpuMesh3D` (indexed vertex and index buffers in device-local memory) and
  `SceneInstance3D(meshId, materialId, transform, previousTransform, flags)`. Validation (finiteness,
  triangle completeness) moves to `Prepare`/upload time, once per mesh. A frame submits instance
  transforms only. The current `NativeModel3DBatch` path stays as a compatibility adapter that uploads
  through `GpuMesh3D` and caches by `TemporalIdentity`.
- Draws become indexed, and identical mesh/material pairs are instanced.

**Done means.**

- All existing captures are hash-identical in Capture mode.
- Beacon and Cloudstep run Interactive with measured CPU frame time and GPU idle gap (timestamps around
  submit), before and after.
- A specimen with 5,000 instances of one model shows CPU frame cost that does not scale with vertex
  count.

**Do not.** Do not change any shader output in R0. Do not remove the old batch API; adapt it.

**Stop if.** Pipelining exposes resources shared across frames that the current design cannot slot
without rewriting a pack. List them with evidence instead of patching around them.

---

### R1. Compiled frame plan (render graph)

**Baseline.** Frame graphs (Frostbite 2017 and descendants): passes declare resource reads and writes. A
compiler orders passes, inserts barriers, aliases transient memory, and can assign passes to queues.

**Aurelian proposal: the plan is a compiled, hashed, statically verified artifact.** Most engines rebuild
their graph every frame and trust it. Aurelian should compile the plan once per (settings, scene feature
set, tier). It then emits it as data (JSON for the inspector and Oblivion, hashed into proof evidence) and
statically verifies it before first use. The checks are:

- no read before write;
- every write either consumed or declared as an output;
- a barrier exists for every hazard;
- the declared queue for every pass supports its operations;
- no transient resource is aliased while live.

A plan that fails verification is a compile error with a diagnostic code, not a validation-layer message at
run time. This is the lighting-compilation philosophy (compile once, publish an asset, the runtime
evaluates data) applied to the frame itself.

**Design.**

- `FramePassDeclaration`: name, queue class (`Graphics`, `Compute`, `Transfer`), declared reads and
  writes (`FrameResource` with format, extent rule, lifetime `Transient` | `History` | `Imported`), and
  a record callback.
- `FramePlanCompiler`: topological order, then barrier synthesis using `VK_KHR_synchronization2` (core in
  1.3), then transient aliasing, then queue assignment.
- `FramePlan`: immutable, with a content hash and a `Describe()` dump.
- **Multiple queues.** Select a compute-capable second queue when one exists. Cross-queue edges become
  timeline-semaphore waits and signals recorded at submit time. **No CPU round trip and no `async`/`await`
  between passes.** C# `Task`s appear only where the CPU consumes GPU results (readbacks, multi-frame bakes).
  They complete on the frame thread when a polled timeline value is reached, so continuations never run on
  arbitrary threads. Multi-phase GPU jobs with retries or budgets remain Dominatus controllers, as lighting
  compilation already does.
- **Migration.** Wrap each existing stage of `RenderCore` as a pass declaration without touching its
  shaders. `RenderCore` becomes "build or fetch the plan, execute it". Packs keep their own classes; they
  gain declarations.

**Done means.**

- Every existing capture is hash-identical through the plan.
- `RenderCore` is gone or reduced to plan execution.
- The plan dump appears in the inspector.
- The static verifier rejects four deliberately broken plans in tests (read-before-write, missing barrier,
  live alias, wrong queue).
- AO or the probe update runs on the compute queue in one specimen, with timestamps showing whether
  overlap occurred. On the RTX 3070 a small or zero gain is an acceptable, recorded result.

**Do not.** Do not build a general-purpose graph library with features no pass uses. Do not make graph
construction per-frame dynamic until a pass needs it.

---

### R2. Bindless, GPU-driven visibility

**Baseline.** GPU-driven pipelines (Wihlidal, GDC 2016; Nanite 2021):

- meshlet clusters;
- GPU frustum culling and two-phase occlusion culling against a hierarchical depth buffer;
- indirect draws;
- a visibility buffer (Burns and Hunt, JCGT 2013) that stores instance and triangle IDs, with materials
  resolved afterwards in compute.

**Design.**

- **Bindless tables** (Tier R and V): descriptor indexing, with a global texture array, a sampler table and a
  material SSBO (`MaterialRecord` mirrors `ModelMaterial`). Per-material descriptor binds go away.
- **Clusters**: meshes are split offline into meshlets of 64–128 triangles with bounding spheres and normal
  cones. Use meshoptimizer's C API through P/Invoke with provenance recorded, or an owned
  implementation if licensing or packaging argues against it. Record the choice.
- **Culling** in compute: instance frustum, then cluster frustum and back-face cone, then two-phase HZB
  occlusion (draw last frame's visible set, build the HZB, test the rest). The result is compacted into an
  indirect argument buffer drawn with `vkCmdDrawIndexedIndirectCount`.
- **Visibility buffer**: one `R32G32_UINT` target (instance ID; triangle ID within the cluster plus cluster
  ID), plus depth. A material classification pass bins tiles by material, then compute resolve evaluates the
  existing lighting functions using derivatives reconstructed from barycentrics. The current surface resolve
  becomes a consumer.
- **LOD**: a per-cluster-group simplification chain built offline. Selection is by screen-space error, with
  hysteresis. A Nanite-style DAG with seamless group boundaries is a later step, entered only if crack-free
  continuous LOD is needed by a real scene.
- **Tier W fallback**: the same culling compute, with indirect draws grouped by material and no bindless.

**Aurelian proposal: hybrid analytic and triangle visibility.** Aetheris already provides exact analytic
fields. Let the visibility buffer hold either a triangle hit or an analytic field hit: a primitive-kind bit,
a field-primitive ID, and an exact analytic normal reconstructed in resolve. Field primitives are
sphere-traced in a compute pass that writes into the same visibility and depth targets, so they occlude and
are occluded by triangles correctly, and they shade through the same material resolve.

The payoffs are infinite-resolution CAD-authored surfaces with no tessellation or LOD, and the same field
reused for ray-traced visibility (R3) and GI (R4).

- Falsification: compare against the same Aetheris shapes meshed at matched screen error. Measure GPU time,
  depth-edge correctness at intersections and shading continuity.
- Kill condition: it loses on time at matched quality in the specimen scene and offers no other
  advantage.
- Lives behind the bridge `Aurelian.Lighting.Aetheris` (see `docs/Aurelian/aetheris-boundary.md`).

**Done means.**

- The specimen "Forest" (≥ 20,000 instances, ≥ 20M source triangles) renders with CPU submit cost
  independent of instance count.
- GPU culling matches a CPU reference cull on a frozen camera path; mismatches are counted and must be
  conservative (no visible cluster culled).
- Existing captures are unchanged, or changed only by the documented visibility-buffer precision
  difference.

**Stop if.** The visibility-buffer resolve cannot reproduce a current pack (transparency, refraction,
subsurface) without duplicating it. Then keep those materials on the forward path, documented, and continue.

---

### R3a. Reference integrator (the measuring instrument)

**Aurelian proposal: reference-gated rendering.** Unreal ships a path tracer, but does not gate its real-time
features on it. Aurelian should. Every approximate technique in R3–R8 reports its error against an in-engine
reference on the same scene, camera and materials.

**Design.**

- `ReferenceIntegrator3D`: a progressive unidirectional path tracer with next-event estimation and MIS,
  in compute with inline ray queries, over the existing `VulkanRayQueryScene`.
- It shares the exact BRDF functions used by real-time shading, including every diffuse model, so a
  difference measures transport, not mismatched materials.
- Runs to a declared sample count or convergence target, Capture mode only.
- Metrics:
  - RMSE in linear HDR;
  - [FLIP](https://github.com/NVlabs/flip) (BSD-3) on tone-mapped output;
  - a temporal stability metric for sequences (mean frame-to-frame difference on a static camera, and on a
    scripted path compared with the reference sequence).
- A scene suite, built once and reused by every later milestone:

  | Specimen | Contents |
  | --- | --- |
  | Cornell+ | Bounce color and contact shadows |
  | Hall of 256 Lights | Shadowed point, spot and rect lights |
  | Moving Occluder | Dynamic object in static bounce light |
  | Forest | From R2 |
  | Wet Night | Existing rain scene |
  | Thin Geometry | Fences and foliage cards |

**Done means.** The reference converges with seed-stable results. A white-furnace test passes for every
diffuse model. The existing compiled diffuse floor receiver reports its error against the reference.

---

### R3. Ray-traced visibility and many lights

**Design: shared scene acceleration structure.** BLAS per mesh LOD, compacted. Refits for GPU-skinned
meshes reuse the `VulkanSkinning3D` output buffer directly. The TLAS is built from the R2 instance table on
the compute queue. Alpha-masked materials use any-hit equivalents via inline candidate handling.

**Shadows.** Ray-traced shadow rays for directional, spot and **point** lights, with area-light soft
shadows sampled from deterministic blue noise. Cascades and spot maps remain the Tier V path.

**Many-light direct lighting.**

- **Baselines to implement:**
  - Tiled exact evaluation, today's path, as the control.
  - [MegaLights](https://www.advances.realtimerendering.com/s2025/content/MegaLights_Stochastic_Direct_Lighting_2025.pdf)
    style:
    - 8×8 tile visible-light lists carried from the previous frame;
    - weighted reservoir selection about 1 spp;
    - screen-space plus hardware visibility rays;
    - a single SVGF-like denoiser.

    Epic reports about 5.5 ms on PS5 at 1080p for 941 shadowed area lights. Its stated weaknesses are
    temporal artifacts under fast motion and when many lights matter per pixel, plus BVH cost.
  - ReSTIR DI (Bitterli et al. 2020) with spatiotemporal reuse.
- **Aurelian proposal: exact-few, stochastic-rest, under a constant ray budget.**
  1. Per tile, rank lights by an upper-bound contribution (unshadowed BRDF times intensity times solid
     angle).
  2. The top K lights per tile are evaluated **exactly**: one shadow ray each and no denoising. They carry most
     of the energy in typical game lighting, so they cannot ghost or crawl.
  3. The remaining tail is sampled stochastically, and only the tail goes through the denoiser.
  4. K adapts per tile under a frame-wide ray budget, in the spirit of the variable-rate ray scheduler
     presented for *Modern Warfare 4* at SIGGRAPH 2026, so the total ray count is constant per frame.
  5. Optionally, a compiled light-importance prior: the static-lighting compiler already knows which lights
     can reach which receivers. It can publish a per-region light shortlist, a light "potentially visible
     set", so runtime ranking starts from feasible lights only.

  The hypothesis is that denoising only the tail gives much better temporal stability than denoising
  everything at equal ray count.
  - Falsification: on Hall of 256 Lights and Moving Occluder, compare RMSE/FLIP against R3a, temporal
    stability and GPU ms with MegaLights-style and ReSTIR at equal rays per pixel.
  - Kill condition: no stability gain at equal ms.

**Done means.**

- Point lights cast shadows.
- 256 shadowed lights render inside a declared budget on the RTX 3070.
- All three methods are measured against the reference, and the default is chosen by that evidence.

---

### R4. Dynamic global illumination

**Baselines.**

- Lumen (screen probes, surface cache, world radiance cache).
- DDGI probe grids.
- [GI-1.0](https://ar5iv.labs.arxiv.org/html/2310.19855) two-level hash-grid radiance caching.
- ReSTIR GI.
- [Neural radiance caching](https://arxiv.org/pdf/2106.12372).
- EA SEED's history-free online radiance cache (ORCA, SIGGRAPH 2026 Advances).
- The idTech 8 approach already adapted in `docs/Aurelian/live-diffuse-experiment.md`.

**Aurelian proposal: compiled-plus-residual GI.**

Game worlds are mostly static. Lumen-class systems spend rays every frame rediscovering light that has not
changed. Aurelian already compiles static diffuse transport into assets, and fits learned "experts" for it
(`compiled-diffuse-lighting.md`, `scene-lighting-experts.md`). So:

- `L = L_compiled(static scene, static lights) + ΔL(dynamic lights, dynamic occluders, moving objects)`.
- `L_compiled` scales up from one receiver to all static receivers. It is stored as per-cluster basis
  coefficients, building on the finite-element experiments.
- `ΔL` is estimated at run time by a world-space hash-grid radiance cache traced with R3 rays, **and it is
  signed**. A dynamic occluder must be able to remove compiled bounce light. This is the hard part:
  negative residuals at low sample counts are noisy, and the experiment exists to find out whether it is
  tractable.
- Fallback: when the residual's variance exceeds a bound in a region, that region switches to full runtime
  GI for its cache cells. This is a per-region decision Dominatus can own as a controller with measured
  error and cost, as the lighting-expert work already does.
- Optional neural residual: a small MLP cache evaluated with cooperative matrices in VTS, through the same
  runtime as `Aurelian.Inference`. No other engine has its inference runtime and shading language in one
  toolchain; this is where that advantage is cheapest to test.

Falsification: on Cornell+ and Moving Occluder, compare against a pure runtime cache (hash-grid and probe
baselines) at equal ms, using reference RMSE, temporal stability and light-leak counts.

Kill conditions:

- signed residuals cannot be made stable at all;
- compiled data is too large per square metre for a realistic level.

Either outcome is a worthwhile result; record it.

**Done means.** One default dynamic GI path for Tier R is chosen by evidence. The probe experiment is either
promoted into it or retired with its findings. Compiled lighting works across a whole Beacon arena, not one
receiver.

---

### R5. Reflections

**Design.**

- Ray-traced reflections for roughness below a threshold.
- Rough surfaces read the R4 radiance cache, so there is no separate reflection probe system for them.
- Screen-space tracing is used only as a hit accelerator before the hardware ray, as MegaLights uses it for
  shadows, never as a standalone effect that drops out at the screen edge.
- The existing box probe and planar water reflection stay as the Tier V path. Water may move to rays on
  Tier R if the rain scene shows a measured gain.

**Done means.** Wet Night reflects off non-planar wet geometry. Reference error is reported, and the planar
path is retained for Tier V.

---

### R6. Reconstruction and resolution scaling

**Baseline.** Temporal upscalers (TSR, FSR, DLSS). The 2026 PSSR revision is notable: it returned to
closed-form reconstruction and limited the ML model to pattern recognition, improving quality at lower cost.

**Aurelian proposal: honest reconstruction.** A closed-form temporal upscaler, built from the existing TAA's
history and rejection logic. A small learned model, run by the in-engine inference runtime, predicts only
history confidence (accept, partial, reject per pixel), not colors.

The policy prefers momentary aliasing over ghosting. A ghost is a lie about where something was; this is the
same principle that rules out motion blur. Every frame must show the scene at that instant.

Falsification:

- Specimens with fast thin geometry, particles and disocclusion are compared against a supersampled
  reference frame by FLIP.
- A ghost-energy metric measures luminance left behind at previous object positions.
- Kill condition for the learned part: it does not beat hand-tuned rejection on the ghost-energy metric.

**Done means.** Tier R renders internally at 0.5–0.67 scale and reconstructs to output. The ghosting
specimens pass declared thresholds. TAA remains for native resolution.

---

### R7. Eye-model presentation

This replaces the camera-model post stack with a model of the eye.

- **Exposure adaptation.**
  - A GPU luminance histogram with a metering mask; the final value is an authored EV with adaptation
    rates.
  - Asymmetric, as in human vision: adapting to bright light is fast, adapting to darkness is much slower.
    Times are authored per game, with physiological defaults documented as such.
  - Resets on camera cuts. Manual override stays.
- **Ocular glare instead of lens bloom.** Rework bloom toward a physically motivated eye-scattering
  point-spread function (Spencer et al. 1995, *Physically-Based Glare Effects for Digital Images*). Bloom
  then comes from the eye and is not a lens ghost. Keep it energy-bounded and inspectable.
- **Aurelian proposal: night vision as the eye sees it.**
  - Low-light scenes currently rely on a blue grade.
  - Instead, model mesopic and scotopic response: loss of color saturation, the Purkinje shift, and loss of
    acuity in rod-dominated vision (Kirk and O'Brien 2011, *Perceptually Based Tone Mapping for Low-Light
    Conditions*).
  - Wet Night and every night scene then get a physiologically grounded look instead of an authored
    filter.
  - Falsification: compare with the rain art-direction target in `rain-night-art-direction.md` using the
    existing image-guided calibration method. Kill condition: it loses to the authored grade.
- **Display mapping.** Separate scene-to-display mapping from the filmic curve. Add an HDR output path
  (scRGB or PQ swapchain) where the display supports it. UI stays outside exposure, as today.

**Done means.** A dark-room-to-sunlight transition shows no single-frame pumping. The glare PSF is
energy-checked. The night model is measured against the authored target.

---

### R8. Material richness and decals

**Baseline to align with, not invent:** [OpenPBR Surface](https://academysoftwarefoundation.github.io/OpenPBR/).
Aurelian already carries OpenUSD through lighting work, so OpenPBR parameter names and layering semantics
give interoperability for free.

- Add clearcoat, sheen (fabric) and specular anisotropy as `ModelMaterial` fields mapped to OpenPBR.
  Preserve glTF imports through `KHR_materials_clearcoat`, `KHR_materials_sheen` and
  `KHR_materials_anisotropy`.
- Use energy-conserving layering with albedo scaling, verified by white-furnace tests in the R3a reference
  integrator.
- **Decals in the visibility buffer.** With R2 there is no G-buffer to write decals into. Decals become a
  clustered list evaluated during material resolve (in the style of clustered deferred decals), modifying
  albedo, normal and roughness before lighting. This makes decals cheap and lets them correctly affect
  ray-traced reflections and GI, which is awkward in G-buffer engines.
- **Neural texture compression track** (Tier R, research): [hardware-accelerated neural block texture
  compression](https://arxiv.org/pdf/2506.06040) (HPG 2025) and NVIDIA's RTXNTC show large VRAM reductions.
  Decoding with cooperative matrices in VTS is a natural experiment for the in-engine inference runtime.
  Gate it on decode cost per sample and quality versus BC7.

**Done means.**

- The neutral light rig shows clearcoat, sheen and anisotropy with energy checks passing.
- Scorch and wetness decals appear in reflections and GI.
- A clearcoat glTF sample imports losslessly.

## 5. Aurelian proposals summary

The hypotheses this program tests, beyond reproducing industry practice:

| # | Proposal | Beats industry if… | Milestone |
| --- | --- | --- | --- |
| P1 | Compiled, hashed, statically verified frame plan | Hazards are caught at compile time; plans become evidence | R1 |
| P2 | Deterministic stochastic rendering | Stochastic rendering keeps replay and hashable captures | All |
| P3 | Hybrid analytic and triangle visibility buffer (Aetheris) | Exact CAD surfaces cost less than meshing at matched error | R2 |
| P4 | Reference-gated rendering | Every default is chosen by measured error, not habit | R3a |
| P5 | Exact-few, stochastic-rest many-light sampling | Better temporal stability than full-stochastic at equal rays | R3 |
| P6 | Compiled-plus-residual GI with signed dynamic residual | Fewer rays for equal error in mostly static worlds | R4 |
| P7 | Learned history confidence, closed-form color | Less ghosting than hand rejection or ML-color upscalers | R6 |
| P8 | Eye-model presentation, including scotopic night vision | A night look that matches art direction without a grade | R7 |
| P9 | Shared inference and shading runtime (neural cache, neural textures) | Neural components cheaper to integrate and test than anywhere else | R4, R8 |
| P10 | Gameplay-semantic ray budget | The renderer spends rays on what the player must read | R3, R6 |

**P10, gameplay-semantic ray budget.** Dominatus agents already know what matters: an enemy telegraphing an
attack, an interactable, the player's character. Let them publish a small per-frame importance mask (screen
regions or instance flags) that biases the constant ray budget of R3/R6 and the reconstruction confidence.
Industry budgets rays by variance alone; Aurelian can budget by meaning. It must never cause visible popping;
the falsification test is a blind A/B of telegraph legibility under a fixed ms budget. Implement only after
R3's budget scheduler exists.

Each proposal is expected to fail sometimes. A documented failure with evidence is a successful outcome
under the convergence rule; a proposal kept without evidence is not.

## 6. Qualification additions

- Every milestone extends `tools/Aurelian.GraphicsProof` with a mode and a fail-closed `evidence.json`, as
  the packs do today.
- New metrics, shared by all milestones:
  - RMSE and FLIP against R3a;
  - temporal stability;
  - ghost energy;
  - GPU ms per plan pass from the existing timestamps;
  - CPU frame time;
  - budget and overflow counters.
- The scene suite from R3a is the standing benchmark. Add specimens; never silently change one.
- Device baseline remains the local RTX 3070. Record device and driver in evidence. Tier V must be run
  with ray queries disabled through `VulkanPlantOptions`. Tier W keeps the existing Naga regression.
- Validation layers (synchronization validation included) are clean in Capture mode for every plan.

## 7. Guardrails for the implementer

- Keep the readable-code rule. New passes are small classes with declared resources, not branches inside
  one renderer.
- Never re-baseline a capture to make a test pass. Explain every pixel change.
- Do not add any prohibited effect from section 1.1, including as a "disabled by default" option.
- Do not fork the material model between raster and ray paths: one set of BRDF functions in VTS, used by
  resolve, RT hit shading and the reference integrator.
- Do not let an experiment become a default until its comparison exists.
- When a milestone stops converging, stop and write the blocker down. A precise "this is why R2's
  transparency cannot move to the visibility buffer yet" is worth more than a workaround.

## 8. Open questions for Yuechen

1. Tier W ambition: must the browser path track Tier V features, or is it allowed to stay at today's
   level?
2. meshoptimizer via P/Invoke (fast and proven) or an owned C# cluster builder (no native dependency)?
3. For P3, which Aetheris primitive set is admitted at runtime, all analytic fields or a bounded subset?
4. Whether exposure adaptation defaults are physiological or tuned for play, documented either way.

## References

- [Advances in Real-Time Rendering, SIGGRAPH 2026 course](https://advances.realtimerendering.com/s2026/index.html):
  ORCA (EA SEED), PSSR revision (Sony), variable rate ray tracing (Activision), Smolder (IO Interactive),
  adaptive tessellation (Meta).
- [MegaLights: Stochastic Direct Lighting, SIGGRAPH 2025](https://www.advances.realtimerendering.com/s2025/content/MegaLights_Stochastic_Direct_Lighting_2025.pdf)
  and [Unreal Engine MegaLights documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/megalights-in-unreal-engine).
- [Sousa, idTech 8, SIGGRAPH 2025](https://advances.realtimerendering.com/s2025/content/SOUSA_SIGGRAPH_2025_Final.pdf)
  (already used by `docs/Aurelian/live-diffuse-experiment.md`).
- [GI-1.0: A Fast and Scalable Two-Level Radiance Caching Scheme](https://ar5iv.labs.arxiv.org/html/2310.19855).
- [Real-time Neural Radiance Caching for Path Tracing](https://arxiv.org/pdf/2106.12372).
- [World-Space Spatiotemporal Path Resampling for Path Tracing](https://diglib.eg.org/items/5bfa2632-d39d-47f1-ab6d-07449e207d95/full).
- [Hardware Accelerated Neural Block Texture Compression with Cooperative Vectors, HPG 2025](https://arxiv.org/pdf/2506.06040)
  and [NVIDIA RTXNTC](https://github.com/NVIDIA-RTX/RTXNTC).
- [OpenPBR Surface specification](https://academysoftwarefoundation.github.io/OpenPBR/).
- [FLIP image difference metric](https://github.com/NVlabs/flip).
- Burns and Hunt, *The Visibility Buffer: A Cache-Friendly Approach to Deferred Shading*, JCGT 2013.
- Wihlidal, *Optimizing the Graphics Pipeline with Compute*, GDC 2016.
- O'Donnell, *FrameGraph: Extensible Rendering Architecture in Frostbite*, GDC 2017.
- Bitterli et al., *Spatiotemporal Reservoir Resampling for Real-Time Ray Tracing with Dynamic Direct
  Lighting*, SIGGRAPH 2020.
- Spencer, Shirley, Zimmerman and Greenberg, *Physically-Based Glare Effects for Digital Images*, SIGGRAPH
  1995.
- Kirk and O'Brien, *Perceptually Based Tone Mapping for Low-Light Conditions*, SIGGRAPH 2011.
