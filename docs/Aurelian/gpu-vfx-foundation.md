# GPU VFX foundation

Fire, smoke and sparks now use engine-owned Visual TypeScript compute and
billboard shaders. The CPU sends immutable emitter commands; Vulkan generates
particle positions, lifetimes, seeded variation and six billboard vertices per
slot. The renderer records that compute dispatch and its draw in the existing
scene submission. Normal particle buffers are GPU-only, with no particle
readback or CPU mesh rebuilding.

This is an analytic particle foundation. Fire rises and fades, smoke drifts and
expands, and sparks follow ballistic trajectories. It does not simulate fluid
combustion, smoke self-shadowing, scene collisions, ribbons or mesh particles.
Smoke uses procedural billboard lobes; no texture downloads or flipbook assets
are required. Particle emission does not automatically create gameplay lights.
Use the existing authored local-light API when a fire should illuminate a room.

## Game authoring

The standard native game host provides a lazy default pool of 16 emitters with
512 particle slots per emitter. Set `NativeGameGraphics.ParticleEmitters` to the
visual snapshots to submit. `ParticleBudget` reports reservations, requested
live capacity, over-budget demand and the bytes uploaded for the command.

```csharp
var fire = ParticleEmitter3D.Fire("campfire", new Vector3(0, .3f, 0), seed: 17);
var smoke = ParticleEmitter3D.Smoke("campfire-smoke", new Vector3(0, 1, 0), seed: 99);

// The application/agent clock supplies absolute elapsed time, not the renderer.
graphics.ParticleEmitters = [
    fire with { ElapsedSeconds = elapsed },
    smoke with { ElapsedSeconds = elapsed },
];

// End emission, then let the existing particles age out.
fire = fire with { EmitUntilSeconds = elapsed };
// Replaying earlier time and the original command reproduces the earlier state.
```

For a scene-owned emitter, `ParticleEmitterDefinition` uses the ordinary typed
scene agent API. Its state is the command itself: identity, placement, seed,
time, duration and effect parameters remain inspectable and customizable with
normal C# record copying.

```csharp
Scene.Agent("campfire", new ParticleEmitterDefinition(
    ParticleEmitter3D.Fire("prototype", Vector3.Zero, 17)), new Vector3(0, .3f, 0));

var emitter = mountedScene.Agent<ParticleEmitter3D>("campfire");
emitter.State = emitter.State with { ElapsedSeconds = elapsed };
graphics.ParticleEmitters = [emitter.State];
```

Projection is explicit; a scene agent's presence does not implicitly advance
time or render an effect. Integrate its immutable command into the existing
application cadence. GPU trajectories are evaluated from that state, so reset
and replay do not require a hidden mutable particle simulator. Moving the
emitter moves its current particle population too; detached world-space trails
need a future retained simulation path. Exact replay was qualified on one
device, not across all GPU floating-point implementations. Existing save/rewind
systems must restore emitter commands/time; this pass adds no new persistence
or Dominatus tracing stack.

## Renderer integration

`VulkanParticles3D` accepts the compiled `Particles3D.v.ts` kernel and explicit
pool capacities (1–16 emitters, 1–8192 slots each). Assign it to
`VulkanSolid3DRenderer.ParticleSystem`, supply snapshots through
`ParticleEmitters`, and include `ParticleBillboard3D.v.ts` as
`SurfaceLightingPrograms.ParticleBillboards`. Its caller owns the pool lifetime;
the standard game host owns its lazily created pool.

Compute writes are synchronized before vertex reads. The generated billboards
join existing weighted transparency accumulation and logarithmic revealage,
with no depth writes or CPU sorting. The existing opaque motion/depth texture
provides occlusion and world-distance soft intersections. Each emitter owns its
intersection distance. Height fog or the existing integrated volume is applied
at particle depth. Transparency resolves before TAA, bloom and tone mapping;
the existing translucent-coverage policy rejects particle history and marks it
invalid for subsequent disocclusion. Opaque surface history remains independent.

Each emitter reserves a fixed segment. Its requested live capacity is
`ceil(rate * lifetime)`. If that exceeds its segment, the effective spawn rate
is capped at `slots / lifetime`, preserving lifetimes and reporting the excess
capacity demand. There is no unbounded allocation or atomic spawn race. Too many
emitters and duplicate IDs are rejected with `AUR-VFX-003`; invalid authored
parameters use `001`. Pool/SPIR-V, camera, plant, readback and draw-contract errors
use `002`, `004`, `005`, `006`, `007` and `008`. Empty command lists also clear
the budget inspection.

The packed compute emitter is 96 bytes (five float4 values and four u32 values),
the billboard vertex is 44 bytes, and the draw uniform is 224 bytes. Seeds retain
all 32 bits. The catalog owns both `.v.ts` sources. The existing compiler seam
now admits `Floor(f32)` and unsigned remainder, bitwise and shift operations;
operand rejection and actual DXC/SPIR-V tests cover these additions.

## Qualification

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet build Aurelian.slnx -c Release -m:1
dotnet test Aurelian.slnx -c Release --no-build -m:1
dotnet test tests/Copeland/Copeland.TS.Tests -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --particles-graphics --output artifacts/local/gpu-vfx
```

The native specimen mounts real emitter agents and renders fire/smoke/sparks on
the existing Vulkan surface renderer. It checks visible output from each
preset, finite bounded geometry, full-width seeded variation, exact command
replay, rewind, stop/drain, controlled opaque occlusion, soft-intersection
tuning, fog/volume execution, rate saturation and rejected duplicate/excess
emitter commands. GPU-only output must match qualification buffers. The TAA
removal check covers formerly affected pixels on smooth opaque receivers;
opaque silhouette differences between jitter phases are excluded explicitly.
GPU timestamps report `particles-update` separately; draw plus resolve is in
the existing `transparency` timing. These exclude CPU submission and readback.

Captures and measured results are written to `artifacts/local/gpu-vfx`.
`enableQualificationReadback` is explicit and disabled by default. The proof
scene is a bounded local RTX 3070 qualification, not a large-effects stress
test or a claim of production smoke quality. Next useful work is authored
flipbooks, velocity-aligned spark streaks and detached world-space motion.

Local qualification on 2026-10-11: the Release Aurelian solution build passed
with no warnings or errors. All 1,276 Aurelian tests and 1,504 Copeland TS tests
passed without skips. The final shader sources also passed all 329 shader tests;
the built-in NuGet package retained both Effects3D sources exactly and included
its existing license notices.

The native RTX 3070 specimen at 960×640 changed 39,019 pixels, with 275 live
particles in three 512-slot reservations. Identical commands reproduced both
the image and GPU vertices exactly; rewind, stopped-emitter drain and controlled
occlusion matched the opaque baseline. Soft-intersection tuning changed 2,712
pixels. Removing the particles left a maximum byte error of 1 over 37,280
qualified smooth-receiver pixels with TAA. Normal GPU-only buffers matched the
qualification buffer's image. Across 50 measured frames after warm-up, particle
generation averaged 0.040 ms and the transparency draw/resolve averaged
0.294 ms. These are small-scene GPU measurements on this device, not frame-time
or scalability guarantees. Native surface and transmission regression proofs
also passed with no particles configured.
