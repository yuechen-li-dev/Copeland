# GPU VFX foundation

Fire, smoke, sparks and embers use engine-owned Visual TypeScript compute and
billboard shaders. The CPU sends immutable emitter commands; Vulkan generates
particle positions, lifetimes, seeded variation and six billboard vertices per
slot. The renderer records that compute dispatch and its draw in the existing
scene submission. Normal particle buffers are GPU-only, with no particle
readback or CPU mesh rebuilding.

Particles stay analytic: each slot's state is a pure function of the command and
the clock, so replay and rewind need no retained simulation. It does not
simulate fluid combustion, scene collisions, ribbons or mesh particles. Smoke
and flames are procedural; no texture downloads or flipbook assets are required.
Particle emission does not automatically create gameplay lights. Use the
existing authored local-light API when a fire should illuminate a room (the
[Hearth](../../Games/Hearth/README.md) showcase drives flickering fire lights
from the same clock).

## How it looks the way it does

| Technique | Where | Reference |
| --- | --- | --- |
| Curl-noise turbulence: the curl of a four-octave spectral vector potential, so the flow is divergence-free and smoke swirls instead of collapsing. Octave amplitudes fall by 2^(-1/3) (Kolmogorov scaling). The field is integrated over each particle's age in eight fixed steps, which keeps motion stateless. | `Particles3D.v.ts` | Bridson, Hourihan and Nordenstam, [Curl-noise for procedural fluid flow](https://www.dneg.com/wp-content/uploads/2022/11/2007.pdf), SIGGRAPH 2007; Stam and Fiume, [Turbulent wind fields for gaseous phenomena](https://history.siggraph.org/?p=118629), SIGGRAPH 1993 |
| Blackbody flame colour. Each pixel has a temperature from the flame's noise-eroded core, the particle's age and its height in the tongue. Hue comes from Planck's law at 610, 550 and 465 nm (Wien regime); brightness follows Stefan-Boltzmann (T/1800 K)^4. Sparks and embers cool the same way. | `ParticleBillboard3D.v.ts` | Nguyen, Fedkiw and Jensen, [Physically based modeling and animation of fire](https://graphics.stanford.edu/papers/fire-sg02/), SIGGRAPH 2002 |
| Additive emission. Emitted radiance accumulates in the transparency pass's spare `optical.yzw` channels instead of the weighted average. Order-independent transparency keeps no order, so the resolve treats emitters and absorbers as uniformly mixed: emission leaves a homogeneous slab of optical depth tau scaled by (1 - e^-tau)/tau. | `TransparencyResolve3D.v.ts` | Max, [Optical models for direct volume rendering](https://courses.cs.duke.edu/cps296.8/spring03/papers/max95opticalModelsForDirectVolumeRendering.pdf), 1995 |
| Spherical billboards for smoke. Each pixel integrates the density (1 - r^2/R^2) along the view ray's chord through the particle's sphere, in closed form, clipped at the opaque depth. Puffs have real thickness and meet geometry without flat-card seams. | `ParticleBillboard3D.v.ts` | Umenhoffer, Szirmay-Kalos and Szijarto, [Spherical billboards and their application to rendering explosions](https://graphicsinterface.org/proceedings/gi2006/gi2006-8), GI 2006 |
| Smoke lighting. Sunlight uses Beer-Lambert transmittance toward the sun through the rest of the sphere, a Henyey-Greenstein phase (g = 0.35) and a "powder" term that darkens thin edges facing the light. Hemisphere ambient is dimmed by the puff's thickness. Fire `Glow` lights the plume's underside and falls off with height. | `ParticleBillboard3D.v.ts` | Schneider, [The real-time volumetric cloudscapes of Horizon Zero Dawn](https://www.guerrilla-games.com/read/the-real-time-volumetric-cloudscapes-of-horizon-zero-dawn), SIGGRAPH 2015 Advances |
| Velocity-aligned streaks for sparks and embers. They stretch over the distance travelled in a 1/60 s shutter, and intensity is divided by the stretch so motion blur conserves energy. Flames lean along their screen-space velocity. | `Particles3D.v.ts` | |

## Game authoring

The standard native game host provides a lazy default pool of 16 emitters with
512 particle slots per emitter. Set `NativeGameGraphics.ParticleEmitters` to the
visual snapshots to submit. `ParticleBudget` reports reservations, requested
live capacity, over-budget demand and the bytes uploaded for the command.

```csharp
var fire = ParticleEmitter3D.Fire("campfire", new Vector3(0, .3f, 0), seed: 17);
var smoke = ParticleEmitter3D.Smoke("campfire-smoke", new Vector3(0, 1, 0), seed: 99) with
{
    Glow = new Vector3(1, .42f, .12f) * 2.6f, // fire light on the plume's underside
};
var embers = ParticleEmitter3D.Embers("campfire-embers", new Vector3(0, .4f, 0), seed: 5);
// Every preset takes an optional scale: Fire(id, position, seed, scale: .4f) is a torch.

// The application/agent clock supplies absolute elapsed time, not the renderer.
graphics.ParticleEmitters = [
    fire with { ElapsedSeconds = elapsed },
    smoke with { ElapsedSeconds = elapsed },
    embers with { ElapsedSeconds = elapsed },
];

// End emission, then let the existing particles age out.
fire = fire with { EmitUntilSeconds = elapsed };
// Replaying earlier time and the original command reproduces the earlier state.
```

Emitter parameters beyond placement and timing:

| Parameter | Meaning |
| --- | --- |
| `Turbulence` | Peak speed of the curl-noise flow in m/s; zero gives smooth trajectories |
| `Temperature` | Birth temperature in kelvin for fire, sparks and embers: about 1000 K is dull red, 1500 K orange, 2000 K yellow-white |
| `Color` | Tint and scale of emission (fire, sparks, embers) or smoke albedo; `W` is opacity |
| `Glow`, `GlowHeight` | Smoke only: fire light reaching the underside of the plume, and the height over which it fades |

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

The packed compute emitter is 112 bytes (six float4 values and four u32 values).
The billboard vertex is 92 bytes: position, then uv/kind/softness, colour, sphere
centre and radius, detail (seed, age, temperature, phase) and glow. The draw
uniform is 288 bytes and adds the sun direction and intensity, sun colour, and
sky and ground ambient for smoke lighting. Seeds retain
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
test or a claim of production smoke quality. Next useful work is detached
world-space motion, heat-haze refraction, and lighting smoke from local lights
rather than only the authored `Glow`.

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

The 2026-10-14 look upgrade (turbulence, blackbody emission, spherical-billboard
smoke, streaks, embers) postdates those RTX 3070 measurements, and its GPU
timings have not been re-measured on that device. It was checked on Mesa
lavapipe (software Vulkan) through the Hearth gallery playtests: shader catalog
compilation and SPIR-V validation, ABI checks at pipeline creation, and visual
inspection of the captures. Particle cost now includes per-pixel noise
(about four lattice-noise evaluations per flame or smoke fragment), so measure
fill-heavy scenes before relying on the earlier numbers.
