# Separate weapon depth, wall clearance and adaptive quality

Beacon's ordinary geometric FPS now draws its weapon as an explicit foreground layer, retracts it at nearby colliders and exposes the existing Dominatus visibility policy through Display settings. These independently authored extensions continue the [Saltline extraction](saltline-extraction.md).

## Foreground ownership

`GameView.Viewmodel` accepts a `NativeViewmodel3D`: triangle geometry, its world-to-clip camera and eye. The shared shell renders the world first, then this layer, followed by native 2D layers and HUD/menus. Beacon removes its geometric weapon from both immediate and resident world geometry, so it no longer appears in world shadows, water reflection captures, visibility IDs or world temporal history. Third-person and terminal states omit the layer. The optional skeletal player retains its existing GPU presentation path.

`NativeGameGraphics.RenderViewmodel` lazily creates a surface-owned `VulkanSolid3DRenderer` with `preserveColor: true`. That mode loads an initialized display target in TransferSource layout and uses a separately allocated, cleared D32 depth attachment. The foreground tests/writes its own depth; turning off depth testing would break self-occlusion. This is separate depth, not an always-on-top draw with all depth tests disabled.

The foreground uses the existing vertex-color Basic lighting shader and follows the world's sun direction. It does not add a second HDR lighting, bloom or temporal resolve chain. HDR/surface-output configurations reject color-preserving mode. Direct callers must initialize the display target before loading it. Geometry, finite-value and shader validation remain owned by the existing renderer.

Beacon shares the roll and FOV between cameras, while using a .025 m / 4 m foreground frustum and .1 m / 80 m world frustum. As the physical proxy retracts toward the eye, the foreground camera pulls back by up to .4 m to retain a readable weapon size. This affects only the near-field projection; the world eye and shooting ray stay fixed. Resize and VSync reconfiguration dispose and recreate the foreground resources with the surface. Empty weapons do not allocate or render a layer.

## Wall response from collision facts

`FirstPersonWeaponProbe3D` in `Aurelian.Spatial3D` sweeps a sphere from the eye to the hip/aim origin, then forward along the requested reach. The lateral sweep detects side walls and initial overlap that a single centre ray would miss. It accepts the existing query filter and reads the same authored triangle world as movement. It mutates no physics or combat state.

`FirstPersonMotion3D.WeaponClearance` supplies the resulting distance to `FirstPersonPresentation3D`. `FirstPersonProfile3D` now bounds reach, retracted clearance, obstruction offset/pitch and recovery time. Default response begins inside 1.05 m and reaches full retraction at .25 m. It enters immediately and releases exponentially over .15 s, centres/retracts the gun and points its barrel downward. Aim placement/FOV and recoil/reload rotation yield to obstruction. `PoseWithoutMotion` preserves clearance response when **B** disables cosmetic motion.

Beacon queries clearance on fixed ticks. Its geometric gun origin remains at least .20 m in front of the eye, keeping recoil/reload and retraction within the foreground frustum. The qualified pillar route keeps every weapon vertex on the player's side of the wall, including during firing and reload. The model remains visible in a lowered stance. Canonical aim, projectile origins, accepted shots, collision and the simulation hash are unchanged by view projection or motion toggling.

This is a conservative presentation probe and an authored pose, not a general mesh-versus-world collision solver. A different weapon needs matching reach, pose and origin dimensions. It uses Beacon's static arena colliders; dynamic creature bodies are not added to that world. Foreground depth deliberately does not make the gun occlude or receive world effects.

## Adaptive quality is a persisted setting

**Settings → Display → Adaptive quality** controls `GameDisplaySettings.AdaptiveQuality`. It defaults on, including when loading older preferences that omit the property. Edits remain pending until Apply and then persist through the existing settings store. Graphics presets and window resolution remain separately authored choices.

The switch controls `NativeGameGraphics.EnableAdaptiveVisibility`, reusing `VisibilityPolicyController` and its Dominatus utility decisions, hysteresis and minimum commitment. It compares completed GPU-frame costs for the available baseline, prepared LoD, HZB and combined modes. Turning it off invalidates pending measurements, disables HZB and forces LoD0; those settings survive a surface rebuild. Re-enabling starts fresh measurements. Runtime decisions do not overwrite the player's chosen preset or preferences.

The policy bridge now accepts continuing `NativeInstanceSet3D` snapshots instead of discarding measurements merely because each frame publishes a new snapshot object. Renderer replacement, broken correspondence and large camera changes still invalidate stale comparisons. Inspection prints both world/foreground plans and measured policy costs without adding nondeterministic timing to gameplay/replay facts.

Beacon's normal geometric arena and wet ground now use resident meshes. The existing visibility-buffer opaque pass admits these, followed by forward transparent/refractive batches such as water. Immediate opaque, GPU-deformed and mutable-list adapters remain rejected in visibility-buffer mode. Ordinary Beacon selects visibility buffers only on admitted devices; other devices use the existing resident material path. GI retains its stricter explicit admission.

The arena's simple boxes and diamonds have no prepared lower-detail chain, so Beacon exposes a real baseline/HZB choice, not invented box LoDs. Games supplying prepared LoDs use the same setting/policy. There is no new automatic resolution or preset ladder. In the measured small arena, HZB cost more than baseline and the policy retained baseline; a quality controller should not force an optimization that makes this workload slower.

## Reproduce

```powershell
dotnet test Games/Beacon3D/Aurelian.Beacon3D.Tests -c Release -m:1
dotnet test tests/Integrations/Aurelian.Games.Tests -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --viewmodel --output artifacts/local/viewmodel-depth
dotnet run --project Games/Beacon3D/Aurelian.Beacon3D -c Release -- --playtest-script Games/Beacon3D/Aurelian.Beacon3D/Playtests/weapon-depth.json --resolution 960x600 --output artifacts/local/beacon-weapon-depth --save-root artifacts/local/beacon-weapon-depth/saves
```

The independent native depth proof compares a world occluder, a conventional hidden weapon and a foreground weapon. At 160×120 and 200×128 it checks unchanged surrounding world color, identical pixels in opposite weapon submission orders, failure when weapon depth is disabled, and close geometry surviving only with the foreground near plane. It records 2,704 / 3,136 foreground pixels and writes accepted evidence only after these checks pass.

The FPS script exercises physical input against a pillar, aim rejection near the wall, firing/reload, motion disable, retreat, rewind, third/first-person switching, pending/applied quality settings and resize. It captures eight native frames. The original presentation script separately exercises wet ground, water impulses and environment audio through the new resident/foreground path. Synthetic timing tests retain independent evidence for Dominatus commitment, hysteresis and stale-completion rejection; actual measured costs are workload-specific, not an FPS guarantee.

Qualification passes 1,395 standalone engine tests across 20 assemblies, 33 Beacon tests and 86 shared-games integration tests, with no failures or skips. The core's 56-project closure still reaches no sibling repository or game project. The full Aurelian solution builds successfully; a rebuild of the vendored OpenFont sources retains their 14 existing unassigned-field warnings. The weapon and original presentation native scripts both complete, producing 17 captures in total. A four-frame hidden host smoke also passes with the ordinary frames-in-flight renderer. Evidence is under `artifacts/local/viewmodel-depth`, `artifacts/local/beacon-weapon-depth`, `artifacts/local/beacon-presentation-depth-regression` and `artifacts/local/weapon-depth/final-core-ci`.
