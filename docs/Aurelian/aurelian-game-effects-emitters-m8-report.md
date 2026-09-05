# AURELIAN-GAME-EFFECTS-EMITTERS-M8

## Outcome

**Outcome A — the small-game effects substrate is complete.** An accepted TinyFarm semantic event can request a hit effect at an explicit world position; `Aurelian.Effects2D` deterministically realizes bounded CPU particle and quad instance data; the existing native ordered-quad renderer draws analytic particles and the Visual TypeScript `SoftShockwave.v.ts` material on Vulkan. Rendering disabled, capacity exhaustion, or effect failure cannot alter TinyFarm state or its semantic hash.

The implementation is deliberately small. It adds no particle editor, node graph, timeline, physics particles, lighting engine, post-processing graph, JavaScript runtime, reflection discovery, generic event bus, or audio coupling.

## Existing machinery audit

| Concern | Existing machinery | Reuse | M8 action |
| --- | --- | --- | --- |
| Native 2D world | `VulkanOrderedQuadRenderer`, shared `VulkanNativeFrameTarget`, `Aurelian.GameWorld2D` world/camera laws | yes | reuse the ordered quad and shared-target path; add one concrete textureless material submission |
| Analytic SDF | `AnalyticShape2D.v.ts`, circle/pill/rounded-rect submissions | yes | particles and screen flash are analytic circles/rectangles; no effect sprite renderer |
| MSDF/vector | compiler-driven atlas and quad path | audited, not needed | retain as a future effect shape source; no fake glyph effects |
| Visual TS / VD-MIR | GPU profile, `GpuGraphicsBinder`, `VdMirGraphicsBackend`, DXC, SPIR-V, compiler-described layouts | yes | add one seven-field `SoftShockwave` material shape and `.v.ts` shader |
| Particle/effect code | no reusable emitter runtime existed | no | add dependency-light `Aurelian.Effects2D` |
| TinyFarm semantic events | accepted `IntentResult.Events`; attack, pickup, crop/forage, movement and scene events | yes | add the narrow `TinyFarmVisualEffectProjector` alongside, but independent from, audio projection |
| Machina/compositor | ordered native direct passes and overlays | yes | effects use explicit painter layers; screen flash is above Machina without becoming UI truth |

CPU simulation is the qualified choice: the target count is hundreds to a bounded 2,048 particles, updates are simple velocity integration, and the GPU is used for appearance. No evidence justified compute simulation.

## Semantic contract and identity

`VisualEffectEvent` contains typed `VisualEffectId` and `VisualEffectEventId`, explicit world/screen space, optional position/direction, scale/intensity, optional source/target, deterministic seed, and semantic variant. It contains no backend handle, shader object, mutable gameplay object, or renderer timing.

`EmitterInstanceId` identifies realized transient instances. Dedupe is keyed by stable event ID and bounded to 4,096 remembered IDs. The default capacity is 256 emitters and 2,048 particles. Exhaustion deterministically rejects the newest request and increments `DroppedEffectCount`; it never steals a combat effect unpredictably. Unknown effects, bad lifetimes/counts/capacities, unsupported enum values, missing world position, wrong screen-flash space, and non-finite inputs are diagnosed.

## Catalog, emitters, lifetime, and save/replay law

The immutable explicit C# catalog contains `SwordHit`, `HarvestPuff`, `PickupSparkle`, `FootstepDust`, `AmbientMotes`, and `ScreenFlash`. Definitions expose lifetime, emitter kind, spawn count/rate, size/speed ranges, painter layer, blend, priority, material ID, and shader-quad participation. There is no reflection scanning.

- Burst: sword hit, harvest puff, and pickup sparkle spawn once, integrate velocity, fade, and expire.
- Ambient: scene-derived motes spawn at a fixed rate under the same bounded particle capacity and expire with the emitter.
- Trail: `FootstepDust` is the smallest useful repeated-fading-quad trail. Movement projection is throttled to every fourth semantic movement event; no spline or ribbon editor was added.
- Screen: a bounded full-frame analytic flash has explicit screen space and lifetime.
- Shader quad: `SwordHit` adds one soft radial shockwave behind its analytic sparks.

Transient emitters and particle arrays are never saved. Scene ambience is re-derived from the semantic scene through a stable scene event ID. Replay records/reconstructs semantic intents and events only; stable effect event IDs plus seeds reproduce the cosmetic spawn trace. GPU buffers, handles, and render-frame timing are excluded.

## Determinism and coordinates

Particle spawning uses a local SplitMix64-style sequence seeded from the explicit event seed, stable event-ID FNV-1a hash, and spawn ordinal. `Random.Shared` is not used. Equal event, seed, and definition produce equal position, velocity, size, rotation, variant, and particle seed traces.

World positions retain TinyFarm's explicit `ScenePosition` units. `EffectCameraTransform` applies the M1 law:

```text
pixel = viewport origin + (world - camera top-left) * pixels-per-world-unit * zoom
```

Screen-space values pass through unchanged. Painter order is world ground/tiles, behind-actor effects (150), actors (200), front effects (250), Machina UI, then screen flash (500). Current effects use straight alpha, which is already qualified and produces the intended visible result; additive blend was not necessary and therefore no second blend-state path was added.

## Visual TypeScript shader proof

Canonical source: `src/Aurelian/Aurelian.Shaders/Assets/SoftShockwave.v.ts`. Its ordinary typed material contains `color`, `age`, `lifetime`, `radius`, `thickness`, `intensity`, and `seed`. The only extracted shader helper is the proven `SoftRing` radial mask. The compiler is a frontend only: no browser, React, or runtime JavaScript is present.

The evidence tool compiled:

```text
.v.ts -> Copeland GPU profile -> VD-MIR -> HLSL -> DXC/SPIR-V -> Vulkan
```

Stable hashes for this checkout:

| Stage | SHA-256 |
| --- | --- |
| source | `ef3c90b98e88dfd4a63a6ae85bfb1fab7cf7b0a76651b63e554704ff7bdb15a4` |
| VD-MIR | `7a23cb87ba989e00f1f2352c9e6f2f38ea2b5ca4d2df67a090a1a80190ebb49f` |
| HLSL | `eb3905801c919f2b81fee685c3ba2b8c8e363d4a9f5897412107dc17c8eed1fb` |
| vertex SPIR-V | `f9fc17f4965ef28b72f8c9c4c870a769c0de1bc74662c80ebd2750fd7da8932e` |
| pixel SPIR-V | `8fc56664ee2911c8ae62623e21142bd62da035cc2a65f32ca24a90ce9fa630b9` |

Both SPIR-V stages passed validation. The negative proof rejects managed allocation in a GPU closure with `COPE-GPU-CLOSURE-0001` and the message `Reachable managed allocation has no closed GPU semantics.` Renderer submission separately rejects non-finite age/lifetime/radius/thickness/intensity/seed.

## Integrated TinyFarm proofs

- InputMan Space -> `UseSelectedIntent` -> authoritative resolver -> accepted `EnemyDefeated` -> world `SwordHit` plus screen flash. The second already-defeated attack is rejected and projects no effect.
- Accepted identity `TakeIntent` -> `ItemTaken` -> eight-particle `PickupSparkle`; the effect runtime cannot mutate inventory.
- Crop/forage accepted events map to deterministic `HarvestPuff`; rejected results are filtered before projection.
- `ProjectAmbience(scene)` supplies one stable, bounded scene-owned sunlight-mote emitter.
- With effects enabled, disabled, or deliberately rejected by capacity, the authoritative game hash is identical.

Visuals use the existing native analytic and ordered quad renderers on one Vulkan shared target. On the qualification machine the NVIDIA GeForce RTX 3070 rendered the effect frames with `VK_LAYER_KHRONOS_validation` enabled and no validation error diagnostic. The hit image contains 14 analytic sparks and one compiled shader quad; the pickup image contains eight particles; the ambient image contains 20 bounded motes; the flash is an explicit final screen-space layer.

## Inspection, performance, and stress

`Inspect()` exposes instance/event/effect IDs, age/lifetime, per-emitter particle count, seed, material ID, total particles, dedupe count, and dropped count. Draw data is allocated only when requested; fixed updates mutate retained lists in place.

Representative Release evidence from `performance.json`:

| Work | Result |
| --- | --- |
| spawn one 14-particle sword-hit burst | 17.7 microseconds |
| update 100 particles | 3.5 microseconds; 40 measured bytes |
| update 1,000 particles | 29.1 microseconds; 40 measured bytes |
| build 100 draw records | 363.9 microseconds |
| build 1,000 draw records | 423.1 microseconds |
| 1,000 spawn requests at 128-particle/16-emitter capacity | peak 128; 984 deterministic newest drops |
| after expiry | 0 emitters; 0 particles |

The 40 update bytes are stopwatch/instrumentation overhead in the evidence method; the update path itself creates no per-particle objects. The long ambient test runs 3,600 fixed updates, remains within capacity, and ends without retained emitters.

## Packages, APIs, definitions, and future authoring

- `Aurelian.Effects2D`: renderer-neutral contracts, catalog, deterministic CPU runtime, transforms, inspection.
- `Aurelian.Effects2D.Graphics`: native projection only; depends on Effects2D and Graphics.
- `TinyFarm.Runtime`: consumer projector; Aurelian packages do not depend on TinyFarm.
- `Aurelian.Graphics`: one concrete `NativeSoftShockwaveSubmission`/pipeline option over the existing renderer.
- `Copeland.TS`: one bounded recognized material ABI; no non-GPU language expansion.

TSON effect definitions were audited and deferred: six immutable C# definitions are smaller and more inspectable than adding loader/schema work, while Visual TS remains the shader implementation lane. A future authoring flow can be C#/TSON definition -> typed effect model -> renderer and Visual TS -> shader implementation. Hot reload is possible later but is not an M8 dependency.

## Validation and artifacts

Focused tests cover catalog validation, stable IDs, seed/spawn determinism, burst/ambient/trail, expiration, capacity/exhaustion, dedupe, transforms, painter order, blend choice, unknown/non-finite inputs, shader compilation/hashes/negative diagnostics, accepted/rejected attack, accepted harvest/pickup, save exclusion, replay trace, authority isolation, 1,000 requests, and long ambient boundedness.

- `dotnet test Aurelian.slnx -c Release -m:1`: 745 passed.
- `dotnet test TinyFarm.slnx -c Release -m:1`: 329 passed.
- `dotnet test JointTaskForce.slnx -c Release -m:1`: 3,479 passed.
- focused Copeland GPU profile: 21 passed.
- `Aurelian.Shaders.Tests`: 137 passed.
- `Aurelian.Graphics.Tests`: 260 passed.
- `git diff --check`: passed.

The final working-tree delta is 13 modified files and 28 new files, including ten required evidence artifacts. The breadth comes from isolated package/test/evidence files rather than a broad rewrite; the tracked-file diff before new files is 210 insertions and 14 deletions.

Artifacts are under `artifacts/aurelian-game-effects-emitters-m8/`: `proof.json`, `effects.json`, `shader.json`, `replay.json`, `performance.json`, `manifest.json`, `01-sword-hit.png`, `02-harvest-or-pickup.png`, `03-ambient.png`, and `04-screen-flash.png`.

## Exact next milestone

Proceed to **AURELIAN-FULL-GAME-SLICE-M9**. M8 leaves no required visual-effect seam open. Additive blending, shader/effect hot reload, GPU simulation, post-processing, and an editor remain deferred until a real slice demonstrates pressure.
