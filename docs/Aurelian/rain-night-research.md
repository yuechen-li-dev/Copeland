# Rain after dark: research and a native demo

Research checked on 2026-10-21. The first implementation is a small native Vulkan
street scene with GPU rain, wet pavement, shallow puddles, animated impact rings,
coloured lights, height fog and HDR bloom. It extends Aurelian's existing particle
shaders and reuses the water/reflection renderer.

The current street follows a generated 2D atmosphere reference: warm lanterns,
dark masonry, one teal storefront, thinner rain and local volumetric mist. See
the [art target and matching notes](rain-night-art-direction.md). The measurements
below describe the original neon gallery; generate a new evidence file for the
current art-directed scene.

## Papers and engineering references

| Source | Useful result | Fit for Aurelian |
| --- | --- | --- |
| Garg and Nayar, [Photorealistic Rendering of Rain Streaks](https://cave.cs.columbia.edu/old/publications/pdfs/Garg_TOG06.pdf), SIGGRAPH 2006 | Drop oscillations and illumination produce structured streak brightness; a database supports changing views and lighting. | A future close-up profile. This demo uses simple distant streaks, not their database or photometric model. |
| Lagarde et al., [Dynamic rain and its effects](https://seblagarde.wordpress.com/2012/12/27/water-drop-2a-dynamic-rain-and-its-effects/), 2012 | Rain, splashes and surface response can be separate systems. A rain-direction depth capture supplies surface locations. | Keep decorative impacts independent now; a rain occlusion map is the next prerequisite for roofs and geometry-aware splashes. |
| Lagarde, [Physically based wet surfaces](https://seblagarde.wordpress.com/2013/03/19/water-drop-3a-physically-based-wet-surfaces/), 2013 | Porous wet substrates and water films need distinct treatment. | Our wet asphalt uses authored dark albedo and reduced roughness. Puddles use actual water transport. A general layered wet-material model remains future work. |
| AMD, [FidelityFX SSSR 1.5](https://gpuopen.com/manuals/fidelityfx_sdk/techniques/stochastic-screen-space-reflections/) | Hierarchical depth traversal, roughness-dependent sampling and denoising support glossy screen-space reflections. | Useful for nonplanar wet surfaces later. Requires a depth hierarchy, reflection history and reliable fallback; a reflected object outside the main view cannot be recovered from screen depth alone. |
| Kneiphof and Klein, [Real-Time Rendering of Glints in the Presence of Area Lights](https://arxiv.org/abs/2408.13611), 2024, revised 2025 | Combines area-light integration with a microfacet counting model for sparse highlights. | Candidate for wet stone and asphalt sparkle under broad signs. Requires glint counting and area-light shading infrastructure first. |
| Kneiphof and Klein, [Real-Time Image-based Lighting of Glints](https://arxiv.org/abs/2507.02674), EGSR 2025 | Extends glints to dynamic environment lighting using filtered lighting-region probabilities and a multinomial counting approximation. | Most attractive bounded research experiment after the base weather pack: compare wet aggregate with ordinary GGX while moving the camera. No implementation in this demo. |
| Junkins et al., [Compatibility-Guided Neighbor Selection for ReSTIR](https://research.nvidia.com/labs/rtr/publication/junkins2026compatibility/), HPG 2026 | Improves spatial sample reuse by selecting compatible neighbours. | Longer-term reference if Aurelian adopts reservoir-based lighting. It presupposes ReSTIR; it is not a standalone rain or puddle shader. |

The recommendation is an engineering inference from those sources and the current
renderer. A simple rain shader plus coherent surface and lighting response is the
first useful baseline. Glints are a promising experiment, while a full ReSTIR
lighting pipeline would be a separate project. No paper's benchmark is a measured
Aurelian result, and no external shader implementation or asset was copied.

## Run the real demo

From the Copeland root:

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --rain-night
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --rain-night --view
```

The first run renders qualification captures and writes
`artifacts/local/rain-night/evidence.json`. The window animates the same street:
**R** toggles rain, **P** toggles puddles, **Space** pauses, **Esc** closes. The camera
drifts slightly to expose reflection and temporal behaviour. Resize uses the shared
native graphics surface lifecycle. `--launch-smoke` presents twelve frames in a
hidden native window, saves `native-window.png`, and exits.

The scene contains authored lanterns, a teal storefront, masonry buildings, wet
asphalt and three irregular coplanar water meshes. It is a procedural
technical gallery, not finished environment art. Point lights illuminate the street;
emissive geometry is visible to the opaque planar reflection capture. Rain itself
uses explicit incident lighting per authored volume plus sun/sky, so the two rain
volumes approximate warm and cool illumination rather than querying point lights
for each drop.

## Reuse the rain shader

```csharp
var rain = ParticleEmitter3D.Rain("street-rain", new Vector3(0, 12, 0), seed: 773) with
{
    ElapsedSeconds = presentationTime,
    RainGroundHeight = 0,
    RainShutterSeconds = 1f / 60,
    RainIncidentRadiance = new Vector3(.25f, .65f, 1.1f),
};
// Supply these ordinary commands to renderer.ParticleEmitters.
// A VulkanParticles3D pool must have enough particlesPerEmitter for Rate * Lifetime.
```

`ParticleEffectKind3D.Rain` extends `Particles3D.v.ts` and
`ParticleBillboard3D.v.ts`. Drop motion is a deterministic function of seed, command
and presentation time. Wind is the normalized emitter direction; speed is metres
per second. Lifetime and the existing pool budget bound the work. Shutter time
sets the projected motion streak; opacity is divided by its stretch to preserve
opacity times projected area. The rain branch is terminal-speed analytic motion,
not the fire/smoke integrator. It uses the scattered-light OIT channel, with no
blackbody emission. Existing depth fading and fog apply.

The emitter's existing 112-byte ABI and 92-byte particle vertex remain unchanged.
For Rain only, `emission.w` carries shutter seconds and `glow` carries incident
radiance plus floor height. Other particle kinds retain their meanings.

## Explicit limits and qualification

Rain supports authored open-air volumes and one horizontal floor per emitter.
It does not collide with roofs, awnings or invisible geometry. Main-camera opaque
depth hides drops behind visible surfaces; this is not rain-direction occlusion.
Do not use it to claim dry sheltered areas. The particle opacity check removes
centres below the floor; the fragment shader clips the remaining streak edge.

Puddles share the existing above-water, nearest-refractive-layer renderer and one
coplanar opaque reflection. Rain particles, glass and recursive views are absent
from that reflection capture. Impact rings are eight explicit decorative events
on the same clock, not actual drop collisions. No runoff, accumulation, drying,
corona splashes, lens droplets, fluid simulation, stochastic glints or hardware
ray-traced reflections are implemented here. Wet pavement is an authored GGX
material, not a new layered wetness solver.

The qualification command checks native pixel changes for rain, animation,
reflection and impacts; exact image/vertex replay and rewind; stopped-emitter
drain; finite vertices and bounded pool use; live centres above the floor; shutter
length and opacity-area conservation on GPU output; and black-background renders
showing that unlit rain emits no light. It warms eight moving-camera frames and
records 32 frames of per-pass GPU timings. Captures/readbacks occur only in the
qualification path or explicit window smoke run.

Correctness uses an explicitly readable particle pool. Before timing, the harness
checks an identical image from the GPU-only pool used by the window, then measures
that pool. Host-visible qualification memory is not a valid performance substitute
for ordinary device-local particle storage.

For regression:

```powershell
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --particles-graphics --output artifacts/local/rain-particle-regression
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --water-graphics --output artifacts/local/rain-water-regression
```

Per-pass GPU measurements exclude CPU shader compilation, command preparation,
capture encoding and presentation. They are not whole-frame performance claims
or production certification. Use the generated evidence for this checkout/device.

The local RTX 3070 run at 1280x800 passed with 5,688 live drops, no pool overflow,
83,689 rain-changed pixels, 37,870 reflection-changed pixels and 1,306
impact-changed pixels. GPU-only output matched the readable qualification pool.
Median GPU costs were 0.291 ms for particle update, 0.143 ms for the transparency
pass, 0.984 ms for refraction and 0.331 ms for planar reflection. These pass values
include the scene's associated work; they are not isolated incremental rain costs.

Release build passed with zero warnings/errors on the final focused build. The
full Aurelian solution passed **1,499 tests in 33 assemblies**, with zero failures
or skips. Existing native particle and water proofs passed, and the native window
smoke presented twelve frames. Keyboard interaction and subjective art quality
are not certified by that automated window smoke.

## Next experiments, in order

1. Add a bounded rain-direction depth map to the existing scene renderer; prove
   that an awning shelters the floor from drops and splash placement. This enables
   proper environmental interaction without a CPU particle simulation.
2. Add a wetness material profile with a substrate/film distinction. Compare dry,
   damp and puddled reference patches under grazing and overhead light.
3. Prototype the 2025 glint counting and filtered environment probabilities in an
   isolated material experiment. Measure camera-motion stability, highlight energy,
   aliasing, texture memory and GPU time against ordinary GGX. Do not replace the
   default material until this comparison is convincing.
4. Consider SSSR with environment fallback for nonplanar wet geometry after the
   reflection-history and depth-hierarchy prerequisites are in place.
