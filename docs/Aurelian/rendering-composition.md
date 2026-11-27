# Renderer composition and shipping profiles

The game shell already exposes Low, Medium and High through
`GameDisplaySettings.ApplyQuality`. High is the default and preserves the
game's authored look. These are the Beacon choices for ordinary play.

| Tier | Opaque path and resolution | Direct lighting and output |
| --- | --- | --- |
| Low | Native at either admitted raster path | PBR with unshadowed local lights, no directional shadows, AO, bloom or temporal AA |
| Medium | Native at either admitted raster path | Authored shadows, at most one local spot-shadow map, no AO, authored temporal AA and bloom |
| High | Native at either admitted raster path | Authored shadows, local shadow budget, AO, temporal AA and bloom |

The core renderer defaults to material raster. Beacon enables retained visibility
when the device supports visibility and bindless materials and the humanoid
profile is inactive. Material raster covers the fallback. Beacon's standard
profile uses authored raster lighting. The host's default adaptive policy can
probe HZB and prepared LOD for eligible visibility scenes.

The hardware ray, sampled-light and reflection controls are research inputs on
the renderer and in `Aurelian.GraphicsProof`. Beacon exposes runtime
GI and the regional comparison separately through `--gi` and `--regional-gi`.
Dynamic resolution and the ocular passes also have explicit game-shell options.

## Compatibility matrix

“Measured” refers to the named fixture. “Admitted” describes renderer admission
and still requires workload-specific qualification. The current admission code
in `VulkanSolid3DRenderer.PrepareFrame` and
`VulkanSurfaceLighting3D.PrepareRayLighting` owns the constraints.

| Combination | Status and evidence |
| --- | --- |
| Material raster or resident visibility × three shell tiers | Measured by `--rendering-composition` using the existing Beacon manifest and game-shell settings. |
| Resident visibility × bindless × prepared LOD × two-phase HZB | Measured in Forest. HZB requires the visibility path and its compute programs. Prepared LOD requires an authored sidecar. Dominatus probes eligible modes once visibility rendering is selected. |
| Hybrid analytic visibility × directional raster shadows or local spot-shadow maps | Rejected. The hybrid material path requires `Shadows=false`. Hardware local ray shadows are admitted, and R3 exercises them with analytic spheres in the retained hall. |
| Exact ray shadows × punctual local lights | Measured in the 256-light hall. The exact mask cannot encode area emitters. The ray scene requires one retained opaque scene snapshot. |
| Sampled punctual/area lights × tail reconstruction | Measured in the R3 area fixtures. The sampled tail has its own temporal/spatial filter. Exact mode and subsurface scattering are excluded by tail admission. |
| Runtime GI × hardware GGX × scaled rendering | Measured in Beacon, including adaptive scale changes in R6. GI requires retained bindless visibility and zero legacy hemisphere ambient. If supplied, the compiled environment must match its source radiance. |
| Runtime GI × GGX probe guidance or screen bounds | Measured separately in R5. Guidance needs confident runtime probes with a matching emitter. Screen bounds still trace hardware rays. |
| Reflections × ordinary material raster or immediate opaque geometry | Rejected. Hardware reflections require retained bindless visibility and a ray-query device. Temporal AA also requires the reflection-aware temporal resolve. |
| HZB × runtime GI × GGX × two-thirds scale × eye adaptation, glare and rod–cone color | Measured together by `--rendering-composition` on Beacon. This is an advanced research specimen. |
| Eye adaptation × ocular glare × low-light color | Measured during the R7 Beacon bright-to-dark route. Glare and legacy bloom are alternative optical passes. |
| Retained decal projectors × bindless material raster or visibility buffer | The decal proof compares both primary paths, plus sharp reflections and diffuse GI. Immediate geometry and transmission receivers are outside the decal contract. |
| Material layers × constant-material reference | Clearcoat, sheen and anisotropy share R3a's reference material and BSDF path. Textured material reconstruction and a whole-scene OpenPBR ground truth remain open. |
| Water × refraction and temporal presentation | The pond proof qualifies temporal water and planar reflection. The ocean proof checks current and previous displacement fields, rewind and queued sea-state changes. Water uses its own nearest refractive layer. |

There are ten local sampling enum values including exact, and four dynamic
diffuse values including Off. Regional GI is a policy over compiled residuals.
The proof flags retain the losing controls for comparisons. Ordinary play
uses the three measured tiers. Promotion of a sampled
lighting mode requires a matched GPU budget, a reference image sequence, and
both per-frame error and old-shadow leakage on moving receivers.

## Reproduce

```powershell
dotnet build Aurelian.slnx -c Release -m:1
dotnet tools/Aurelian.GraphicsProof/bin/Release/net10.0/Aurelian.GraphicsProof.dll --rendering-composition
```

The proof saves the compiled pass list, native captures, the raster size,
pixel hashes and timings to `artifacts/local/rendering-composition/`.
The review run passed all seven captures with Vulkan synchronization
validation. The Medium and High outputs match on each raster path in this
arena, which has neither authored AO nor local shadow maps in the baseline
fixtures. The combined ray-query capture uses an authored Beacon arena
without the viewmodel.
See [R5 reflections](rendering-r5-reflections.md),
[R6 reconstruction](rendering-r6-reconstruction.md),
[R7 eye presentation](rendering-r7-eye-presentation.md) and
[R8 materials](rendering-r8-materials.md) for the individual comparisons.
