# Saltline techniques for Aurelian

Saltline contains useful material and presentation techniques, especially coherent wet surfaces, procedural audio, and adaptive quality. Aurelian already implements many of the underlying rendering capabilities. The best next step is a small extension of its material system, followed by a playable scene that combines existing systems.

This is a source audit and extraction proposal. It introduces no engine implementation and makes no visual parity or performance claim.

Implementation follow-up: [integrated capabilities and qualification results](saltline-extraction.md).

## Source snapshots

- Saltline: [`09c51774cd77aad750bd2d6c42af2b1020cb668d`](https://github.com/realcodebear/saltline-fable-5.2/tree/09c51774cd77aad750bd2d6c42af2b1020cb668d).
- Copeland/Aurelian: [`213376717ca69b5e47878e3e1a616ff6fbb43c37`](https://github.com/yuechen-li-dev/Copeland/tree/213376717ca69b5e47878e3e1a616ff6fbb43c37).
- Inspection date: November 12, 2026, America/Los_Angeles.

The Saltline mirror contains only a README and `index.html`. The HTML is 1,429,762 bytes and embeds a JavaScript bundle containing Three.js, GLSL strings, procedural content, gameplay, audio, and diagnostics. No TypeScript source, source map, or original module tree is checked in. Embedded symbols below identify the inspected implementation; formatted scratch-file line numbers are deliberately omitted.

The [README](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/README.md) attributes the original artifact to Claude Fable 5.2 and reports its generation circumstances secondhand. The reusable findings below come from the embedded implementation.

The mirror has no repository license file or declared license. Keep copied game code and bundled third-party code out of Aurelian until reuse terms are established. The proposals here concern techniques and independently authored engine extensions.

## Capability comparison

| Candidate | Saltline mechanism | Existing Aurelian boundary | Extraction value |
| --- | --- | --- | --- |
| Runtime wet materials | Shared wetness affects exposure by surface orientation, streaks, beads, albedo, roughness, and normal strength. | Procedural asphalt already bakes wetness into maps; the common surface evaluator has no equivalent weather profile. | High: extend the shared material path. |
| Continuous wet ground | A common mask drives dry patches, damp margins, soaked ground, water depth, and reflected sheen. | Wet asphalt and water patches already exist as separate authoring facilities. | High: unify transitions and surface queries. |
| Procedural sound recipes | Synthesized PCM variants, staged generation, ambience, synchronized music layers, and environment-dependent mixing. | Audio resources, PCM playback, buses, fades, spatial coefficients, and music crossfade already exist. | High: add preparation recipes and a presentation projector. |
| Adaptive quality | Measured frame intervals drive resolution scale with separate up/down thresholds and cooldowns. | GPU timestamp infrastructure and rendering policies already exist. A general equivalent controller was not identified in this audit. | Medium: build on existing timing and presentation ownership. |
| Alpha coverage at distance | Fence thresholds change with footprint, then use stable screen-space dithering. | Shared surface coverage uses a fixed material alpha cutoff. | Medium: a narrow optional material feature. |
| Specular anti-aliasing | Shortened filtered normal vectors increase roughness to reduce distant glitter. | Common material evaluation normalizes the sampled normal and directly clamps roughness. | Medium: a small experiment in the shared evaluator. |
| FPS presentation | Recoil, weapon springs, movement bob, landing response, aim/sprint FOV, and surface footsteps respond together. | Beacon already supplies mouse aim, movement, jumping, combat, reloads, agents, and playtest infrastructure. | Medium: presentation profiles on top of accepted game state. |
| Planar reflection, rain, PBR maps | Mirrored camera, oblique clip plane, procedural textures, rain and ripple effects. | These foundations already exist in Aurelian. | Reuse existing owners; evaluate refinements rather than add parallel systems. |

## The strongest material techniques

### Wetness as a shared material response

Saltline's `h3` shader injection combines world-space macro variation with orientation-dependent rain exposure. Upward faces collect more water, vertical faces receive streaks, and downward faces receive little. A low band adds dampness near ground contact. Close-range beads fade as their projected size becomes too small. Wetness also reduces fine normal strength and changes roughness and color together. See [`h3`, `gA`, `vA`, and `yA`](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/index.html#L5318).

Aurelian's [procedural map recipes](../../src/Aurelian/Aurelian.Assets/Materials/ProceduralSurfaceMaps.cs) already provide coherent height, albedo, normal, and ORM data. Its [shared material evaluator](../../src/Aurelian/Aurelian.Shaders/Assets/Surfaces/SurfaceMaterial3D.v.ts) is the right place to apply a common runtime response. A proposed `WetSurfaceProfile3D` would describe authored absorption, roughness response, normal damping, and exposure. An explicit presentation frame would supply wetness and time. These are proposed names, not existing APIs.

Start with explicit authored exposure. Surface orientation alone cannot tell whether a roof shelters an object. Actual rain visibility and accumulation would be separate later work, consistent with the limits in [Aurelian's rain research](rain-night-research.md).

### Continuous dry ground to puddles

Saltline's ground shader derives several connected states from a common mask and detail fields. Damp and soaked margins darken the substrate and reduce roughness before the standing-water region begins. Reflection filtering changes with roughness and view angle: damp ground receives a broad sheen while puddles retain a clearer image. Fine rain ripples fade with distance. The CPU exposes related `puddleDepth`, `isPuddle`, and `wetnessAt` queries. See [`TR`, `RR`, and `O1`](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/index.html#L5549).

The reusable idea is a common authored wetness field shared by rendering and surface classification. Aurelian should retain its [existing water transport and planar capture](water-graphics-pack.md), then improve the transition around those water surfaces. Saltline's CPU queries are related approximations of its shader fields; they do not establish exact CPU/GPU agreement.

A proposed surface query would return substrate identity, wetness, and standing-water depth. Footstep selection and impact presentation could consume that result without reading renderer internals. A small field fixture should compare query values with GPU debug output at fixed positions before gameplay relies on it.

### Two bounded shader experiments

The alpha-tested fence path estimates texture footprint, lowers its cutoff as wires shrink, and transitions to a dithered coverage approximation. It also dims the distant surviving fragments. This is a useful answer to disappearing wire fences, with temporal stability to verify under camera motion. See [`SL_ACUT`](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/index.html#L5397).

The normal-map path uses the length of a filtered tangent-space normal before normalization as a roughness correction. This reduces sharp highlights where fine normals have averaged out. See [`vA`](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/index.html#L5407). Aurelian's current evaluator provides a narrow insertion point, but the experiment must verify actual mip/filter behavior and apply consistently to its raster and visibility-based material paths.

## Audio contains substantial reusable design

Saltline synthesizes effects, ambience, and music buffers using noise, oscillators, envelopes, filters, and reverberation recipes. Some buffers prepare during boot; others prepare in cooperative chunks with roughly a six-millisecond work budget. Requests can prioritize a pending sound. Several music layers start on one shared audio clock, then vary gain with encounter intensity. Indoor/outdoor ambience blends as the listener crosses authored volumes. See [`r_`, `a_`, `u_`, and the recipe table](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/index.html#L5928).

The appropriate Aurelian seam is a preparation facility producing ordinary `AudioClipResource` instances. [AudioResourceScope.Add](../../src/Aurelian/Aurelian.Audio/AudioResources.cs) already accepts prepared PCM resources. Keep synthesis away from the device callback, bound duration and sample count, and cache using recipe parameters, version, seed, and sample rate.

Suggested first recipes are material footsteps, a short mechanical impact, and a rain loop. A separate presentation projector should map accepted movement/impact events and explicit environment state to sound cues. Existing buses, voice allocation, focus handling, and disposal remain owned by `Aurelian.Audio` and the game host. See the [audio ownership guide](aurelian-game-audio-m4-report.md); current source also supports MP3 loading, beyond that historical report's original scope.

Synchronized music stems are a distinct follow-up: ordinary music crossfade does not by itself prove sample-aligned multi-stem playback. Saltline also uses `Math.random()` in audio and other systems, so its seeded startup RNG does not guarantee deterministic replay. Aurelian should preserve explicit recipe seeds and event identities.

## Rendering quality and atmosphere

Saltline's `Hg` controller waits for startup settling, aggregates frame intervals, decreases resolution above about 21.5 ms, and allows increases below about 17.4 ms after a delay. Scale stays between 0.6 and 1.0. Repeated slow samples at the floor lower the quality preset. Increasing cooldowns discourage oscillation. Capture/showcase modes disable automatic scaling. See [`Hg`](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/index.html#L4733).

These are policy examples, not thresholds to adopt unchanged. Saltline measures animation-loop intervals, which include CPU work and presentation effects. Aurelian already has [completed GPU timestamps](../../src/Aurelian/Aurelian.Graphics/Vulkan/Native3D/Vulkan3DGpuTimings.cs). A controller should distinguish CPU, GPU, and presentation limits, respect manual settings, report why a tier changed, and coordinate resource resize with temporal history reset. Prove that changing resolution improves the motivating GPU-bound scene before adding automatic preset changes.

The atmospheric result comes from connected choices: directional sky/cloud lighting, shadow-aware volumetric light, ambient occlusion, bloom, exposure adaptation, color treatment, and reflected wet surfaces. The shader code is useful reference material for composition, but the entire post chain would duplicate Aurelian ownership. Saltline also renders the weapon through a separate camera/depth treatment; Aurelian should evaluate that only if a shared first-person presentation path needs independent near-field handling.

## Gameplay and diagnostics worth retaining as patterns

Saltline's player combines coyote time, buffered jumps, crouch clearance, distance-based footsteps, recoil recovery, movement bob, and landing response. The game scores spawn positions using distance, visibility, and recent use, then staggers waves under an alive-count limit. These are useful tunable gameplay policies, rather than reasons to replace Aurelian physics or Dominatus agents. See the [player implementation `x_`](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/index.html#L5928) and [game flow `mM`](https://github.com/realcodebear/saltline-fable-5.2/blob/09c51774cd77aad750bd2d6c42af2b1020cb668d/index.html#L6182).

The bundle also exposes subsystem showcase modes, camera overrides, fixed-delta capture controls, startup/error counters, and a scripted benchmark camera with average, median, and p99 frame times. These are practical inspection patterns. Aurelian's existing [playtesting kit](aurelian-playtesting-inspection.md), graphics proofs, and [Beacon FPS proof](../../Games/Beacon3D/Aurelian.Beacon3D/README.md) provide the corresponding integration seams. Keep explicit failure diagnostics; Saltline's optional-module stubs and catch-and-continue behavior should not become passing qualification evidence.

## Proposed extraction order and acceptance

1. **Wet material response.** Add a small explicit profile through the shared material evaluator and all its callers. Demonstrate the same material transitioning from dry to wet without rebuilding texture assets. Compare albedo, roughness, normal, and shaded views; dry defaults must preserve existing output. Include both forward and visibility material paths.
2. **Wet ground continuity.** Reuse water capture and transport, adding a shared authored field and surface queries. Demonstrate a smooth dry/damp/soaked/puddle traverse, coherent footsteps, stable reflection edges, and sheltered regions. Keep water physics outside this milestone.
3. **Procedural audio preparation.** Produce bounded seeded PCM resources for three recipes through the existing mixer. Verify repeatable sample hashes, finite samples, resource limits, event dedupe, disposal, and offline rendered output. Inspect the real audible result as well.
4. **Shader filtering experiments.** Qualify fence coverage and normal-map roughness correction in focused camera-motion scenes. Record temporal shimmer and image differences, with each feature independently disabled for comparison.
5. **Playable integration.** Use Beacon or a small sibling scene to combine wet materials, rain, audio, recoil, and existing combat. Preserve the common shell and input/replay path. Capture the real playable route and GPU timings; a static screenshot is insufficient.
6. **Adaptive quality if measurements justify it.** Test synthetic timing traces for hysteresis, then measure a real GPU-bound scene through scale changes and resize/history transitions. Keep captures reproducible with automation disabled.

The first milestone has the clearest engine value and the smallest new ownership surface. A wholesale port of the HTML, a second reflection renderer, or a new generic FPS engine would obscure the useful techniques and duplicate working Aurelian capabilities.

## Local inspection

Saltline is cloned beside Copeland as `../saltline-fable-5.2`. Its original files remain unchanged. To inspect readable JavaScript without committing the bundled library into Copeland, extract the single script block to a temporary `.js` file and format that copy with Prettier. Search for the symbols named above; the original HTML contains very long lines.

Copeland uses a shallow partial clone with a sparse checkout of `src`, `libraries`, `tests`, `tools`, `scripts`, `Games/Beacon3D`, and `docs/Aurelian/architecture`. Cone mode also retains files in their parent directories, including the Aurelian guides. Generated artifacts and large media directories remain available from the remote. Use `git sparse-checkout add <directory>` to bring in another directory when needed; the repository has no Git submodules.

The research involved source and architecture inspection. Browser gameplay, native Aurelian visual comparisons, builds, tests, and performance measurements are acceptance work for the proposed implementation milestones, not results of this audit.
