# Aurelian documentation

Aurelian owns engine lifecycle, world and game-object models, actuation, frame coordination, renderer-neutral contracts, renderer backends, assets, shader-domain behavior, and engine-runtime Dominatus integration.

## Start here: authoring and runtime contracts

- [Compose a C# game with the starter](game-starter.md): project reference, `GameStarter` entry point, concepts, options, and headless construction.
- [Build a game on the shared shell](game-shell.md): `IGame`, `GameApp.Run`, input, menus, saves, audio, settings, and playtesting.
- [Choose the runtime execution mode](runtime-execution-modes.md): native host, context-free frame loop, finite harness, and caller-owned diagnostic sinks.
- [Compose document scenes and reusable fragments](scene-composition.md).
- [Author Blender/GLB assets and typed materials](static-assets.md).
- [Author game agents and body bindings](games/agent-authoring.md) and [humanoid animation](humanoid-animation.md).
- [Browse the game projects and their launchers](../../Games/README.md).

The docs above describe current code paths and contracts. For exact signatures and behavior, the implementation is authoritative: `src/Integrations/Aurelian.Games` owns the game shell and starter; `src/Aurelian/Aurelian.GameHost` owns the native frame host; `src/Aurelian/Aurelian.Core/Engine/Frames` owns the context-free frame loop.

## Graphics

- [3D graphics starter](graphics-starter.md): `Graphics3DSettings`, authored lighting, shadows, HDR, and gallery/proof entry points.
- [Surface graphics pack](surface-graphics-pack.md): environment lighting, ambient occlusion, tiled local lights, and temporal AA.
- [Presentation graphics pack](presentation-graphics-pack.md) and [temporal graphics pack](temporal-graphics-pack.md).
- [Water graphics pack](water-graphics-pack.md) and [transmission graphics pack](transmission-graphics-pack.md): bounded water/refraction and glass/volumetric contracts.
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
