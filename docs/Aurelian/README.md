# Aurelian documentation

Aurelian owns engine lifecycle, world and game-object models, actuation, frame coordination, renderer-neutral contracts, renderer backends, assets, shader-domain behavior, and engine-runtime Dominatus integration.

## Start here: authoring and runtime contracts

- [Compose a C# game with the starter](game-starter.md): project reference, `GameStarter` entry point, concepts, options, and headless construction.
- [Aetheris boundary](aetheris-boundary.md): which bridge projects may use Aetheris, and why the core, the shell and 2D games never do.
- [Art slots](art-slots.md): SVG placeholders with briefs that painted art replaces without code changes, and the exporter for the art pass.
- [Build a game on the shared shell](game-shell.md): `IGame`, `GameApp.Run`, input, menus, saves, audio, settings, and playtesting.
- [Choose the runtime execution mode](runtime-execution-modes.md): native host, context-free frame loop, finite harness, and caller-owned diagnostic sinks.
- [Compose document scenes and reusable fragments](scene-composition.md).
- [Author Blender/GLB assets and typed materials](static-assets.md).
- [Author game agents and body bindings](games/agent-authoring.md) and [humanoid animation](humanoid-animation.md).
- [Browse the game projects and their launchers](../../Games/README.md).

The docs above describe current code paths and contracts. For exact signatures and behavior, the implementation is authoritative: `src/Integrations/Aurelian.Games` owns the game shell and starter; `src/Aurelian/Aurelian.GameHost` owns the native frame host; `src/Aurelian/Aurelian.Core/Engine/Frames` owns the context-free frame loop.

## Graphics

- [Rendering foundation design](../design/aurelian-rendering-foundation-design.md): the plan to close the gap with production engines (pipelined frames, compiled frame plan, GPU-driven visibility, ray-traced lighting, reference-gated research) and the effects Aurelian will not implement.
- [3D graphics starter](graphics-starter.md): `Graphics3DSettings`, authored lighting, shadows, HDR, and gallery/proof entry points.
- [R0 rendering foundation](rendering-r0.md): retained indexed meshes, instancing, native frame pipelining, capture compatibility and qualification.
- [R1 compiled frame plans](rendering-r1.md): declarative C# authoring, verified flat tapes, transient reuse, queue experiments and native qualification.
- [R2 GPU-driven clustered path](rendering-r2.md): resident instance admission, GPU culling/compaction, Forest measurements and the remaining visibility/material ABI boundary.
- [R2 resident material tables](rendering-r2-bindless.md): shared material evaluation, nonuniform texture/sampler indexing, many-material Forest controls and native qualification.
- [R2 visibility buffers](rendering-r2-visibility.md): typed IDs, primitive remapping, shared material resolve, precision controls and measured rendering tradeoffs.
- [R2 two-phase HZB](rendering-r2-hzb.md): temporal depth selection, conservative current-frame rescue, stable draw ordering and native occlusion measurements.
- [R2 offline asset LOD](rendering-r2-lod.md): deterministic import-time simplification, prepared delivery indices, GPU selection with hysteresis and native image/performance qualification.
- [Rendering policy and native asset boundary](rendering-policy.md): tooling-only native simplification, managed delivery loading, and measured Dominatus HZB/LOD decisions with hysteresis and minimum commitment.
- [R2 hybrid visibility](rendering-r2-hybrid.md): canonical analytic primitives and triangles sharing depth, typed hit identity, materials, HZB and native qualification.
- [R3a progressive reference captures](rendering-r3a-reference.md): GPU path tracing with shared BRDFs, seeded HDR convergence, NEE/MIS controls, furnaces and the compiled floor comparison.
- [R3a shared affine intersections](rendering-r3a-affine.md): shared sphere/box/capsule math in raster and ray queries, reflected instances, and oracle/depth/secondary-visibility controls.
- [R3a lighting and temporal qualification](rendering-r3a-lighting-temporal.md): sun/HDR environment transport, the compiled sun basis, native-output FLIP and scripted TAA/reference comparisons.
- [R3 retained ray shadows](rendering-r3-ray-shadows.md): shared retained instance identity, GPU point/spot occlusion, the exact 256-light baseline, reference/FLIP controls and measured frame costs.
- [R3 fixed-budget light sampling](rendering-r3-light-sampling.md): uniform and eligible-uniform controls, shared-CDF importance sampling, exact-few tails, fresh/temporal RIS, HDR convergence and full-path cost comparisons.
- [R3 spatial ReSTIR and motion reprojection](rendering-r3-restir.md): immutable neighbor reuse, pairwise target MIS, moving instance snapshots, current visibility and native HDR/TAA qualification.
- [R3 retained TLAS updates](rendering-r3-tlas-updates.md): frame-ordered rigid-motion refits, retained scratch and input slots, rebuild controls, queued-frame correctness and moving-frame cost comparisons.
- [Diffuse comparison](diffuse-comparison.md): controlled Lambert, Burley, Frostbite, EON and Aurelian PathDiffuse experiments, live `IGame` lab, paper references and native numerical qualification.
- [Image guided look pilot](image-guided-look-pilot.md): generated appearance targets, a frozen Cloudstep policy sweep, native VTS selection, transfer results and methodology limits.
- [Image guided look refinement](image-guided-look-refinement.md): material-balanced CPU search, palette/lighting calibration, soft gamut compression, richer native captures and a rejected AI reference registration.
- [Semantic look calibration](semantic-look-calibration.md): fixed PathDiffuse, independently labelled AI material swatches, passing palette/lighting transfer criteria and the shaded-default transition.
- [Surface graphics pack](surface-graphics-pack.md): environment lighting, ambient occlusion, tiled local lights, and temporal AA.
- [Presentation graphics pack](presentation-graphics-pack.md) and [temporal graphics pack](temporal-graphics-pack.md).
- [Water graphics pack](water-graphics-pack.md) and [transmission graphics pack](transmission-graphics-pack.md): bounded water/refraction and glass/volumetric contracts.
- [Rain after dark](rain-night-research.md): native GPU rain/night/puddle demo, shader authoring, qualification and a paper survey.
- [Rain art direction](rain-night-art-direction.md): generated 2D atmosphere target and the native scene's response.
- [Procedural surfaces and primitives](procedural-surfaces-and-primitives.md): height-derived normal/PBR maps, textured local primitives and the Aetheris/Blender handoff.
- [Shader library and VFX roadmap](shader-library-and-vfx-roadmap.md) and [GPU VFX foundation](gpu-vfx-foundation.md).
- [GPU compute math pack](compute-math-pack.md).
- [Lighting compilation](lighting-compilation.md), [compiled diffuse lighting](compiled-diffuse-lighting.md), and [lighting expert workflows](scene-lighting-experts.md).
- [Physics 3D pack](physics3d-pack.md), [composition guide](physics3d-composition.md), and [qualification lab](physics3d-qualification-lab.md).
- [Cloth foundation](cloth3d-foundation.md) and [cloth research review](cloth3d-research-2025-2026.md).

Graphics pack pages define supported behavior and link to their qualification commands. Their recorded timings, device details, image hashes, and test totals are evidence from a particular machine and checkout, not portable performance guarantees. Preserve the documented unsupported cases when reusing a pack.

## Inference

- [Inference foundation](inference-runtime.md): current reusable C# graph, tensor, checkpoint, scheduling, and resource-lifetime APIs. The foundation is built by `Aurelian.Inference.slnx`.
- [Kernel comparison protocol](../../tools/kernel-comparison/README.md): frozen FP32 workloads, PyTorch/Inductor/Triton controls, and measured shader/runtime improvements.
- [Kernel comparison results](kernel-comparison-experiment.md): qualified local results, remaining gaps, and the matched single-operation replay control.
- [Vulkan profiling](vulkan-profiling-experiment.md): CPU phase measurements, compiler controls, device-local parameters and explicit CUDA power-state controls.
- [Scheduling and subgroup experiment](scheduling-and-subgroup-experiment.md): queue admission, independent barriers, warp decomposition and the Vulkan-only operating-state counterfactual.
- [Gemma 4 text inference](gemma4-text-inference.md): a pinned model adapter and a measured local execution path; model files and generated quantized weights are local assets and are not included in the repository.
- [TRELLIS.2 Vulkan experiment](trellis2-vulkan-experiment.md), [throughput experiment](trellis2-throughput-experiment.md), and the linked shape, occupancy, image-encoder, and attention experiments.

The inference foundation is reusable library documentation. Gemma and TRELLIS pages are model-specific qualification reports: their measurements and available artifacts are machine-specific, and a passing model path does not establish general model compatibility or answer quality.

## Architecture and project history

- [Current architecture documents](architecture/), including the [Aurelian engine architecture](aurelian-engine-architecture-v1.md).
- [SDSL-V cross-repository audit](sdsl-v-cross-repo-audit.md) and [Copeland GPU profile](../Copeland/sdsl-v-gpu-profile.md).
- [TinyFarm M1-M21 consolidation report](games/tiny-farm-m1-m21-consolidation-report.md) and [game architecture ground report](games/aurelian-game-architecture-ground-report.md).
- [Historical migration and audit records](history/).
- [TRELLIS.2 shape experiment](trellis2-shape-experiment.md), [occupancy experiment](trellis2-occupancy-experiment.md), [image-encoder experiment](trellis2-image-encoder-experiment.md), and [attention-throughput experiment](trellis2-attention-throughput-experiment.md).

Milestone, audit, and experiment reports record bounded work at the time it ran; treat them as historical evidence rather than a promise that their commands, outputs, or measurements remain current. Recheck the linked source and local artifacts before relying on a report operationally.

## Reference records

- [Aurelian solution](../../Aurelian.slnx)
- [Beacon Run project and proof notes](../../Games/Beacon3D/Aurelian.Beacon3D/README.md)
- [Reusable game menu template](games/game-menu-template.md)
- [Aetheris/agent binding architecture](../architecture/AURELIAN_AGENT_BODY_BINDING.md)
- [Legacy Skyrim agent import](../architecture/AURELIAN_LEGACY_AGENT_IMPORT.md)
- [Dominatus candidate selection](../architecture/AURELIAN_AGENT_CANDIDATE_SELECTION.md)
- [Marionette/Aurelian M1 milestone](../milestones/MARIONETTE_AURELIAN_M1.md) and [M2 milestone](../milestones/MARIONETTE_AURELIAN_M2.md)
- [Dominatus 1.0 provenance](../audits/MARIONETTE_AURELIAN_M1_PROVENANCE.md)
- [Current JTF-M0 topology and ownership](../architecture/jtf-m0-topology-and-ownership.md)

The physical move in JTF-M0 does not perform the deferred semantic migrations listed in the current doctrine.
