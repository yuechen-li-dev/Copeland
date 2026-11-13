# Connected ground weather and environmental audio

A single authored ground snapshot now drives surface shading, standing-water geometry, surface queries, and the rain gallery's footstep selection. An authored shelter volume drives both ground exposure and the listener's ambience blend. These are independently authored extensions informed by the [Saltline audit](saltline-capability-research.md).

## Shared ground data

`WetGroundField3D` in `Aurelian.Rendering.Contracts.Models` owns an immutable regular grid of height, wetness, and rain exposure in world XZ metres, plus a horizontal water level. Its constructor snapshots input data and bounds dimensions to 2–257 nodes per axis. Grid dimensions, samples, coordinates, and representable spacing are validated before mesh preparation.

`Sample(position)` uses the same diagonal and barycentric interpolation as the ground mesh. It does not use bilinear interpolation across a triangulated cell. The returned sample exposes `WaterDepth(waterLevel)`, so a caller can classify a footstep or impact using the geometry's actual authored depression. Queries outside the field throw rather than silently returning a dry surface.

`WetGroundSample3D.FromHeight` combines rain exposure with a continuous damp band above the waterline. Standing water retains a wet bed even when rain stops or a roof covers it. `RainShelter3D` supplies a feathered authored volume; it does not infer roofs from arbitrary geometry.

```csharp
var samples = new[]
{
    WetGroundSample3D.FromHeight(.08f, 0, rain: .8f, exposure: 0),
    WetGroundSample3D.FromHeight(.08f, 0, rain: .8f, exposure: 1),
    WetGroundSample3D.FromHeight(.08f, 0, rain: .8f, exposure: 0),
    WetGroundSample3D.FromHeight(-.03f, 0, rain: .8f, exposure: 1),
};
var field = new WetGroundField3D(Vector2.Zero, new(2, 2), 2, 2, 0, samples);
var (ground, water) = WetGroundMesh3D.Create("yard", field, dryMaterial, new WaterSurface3D());
var nodes = new List<SceneNode> { Scene.Model("ground", ground, Vector3.Zero) };
if (water is not null) { nodes.Add(Scene.Model("water", water, Vector3.Zero)); }
var surface = field.Sample(new(.5f, 1.5f));
```

Meshes contain world-space field coordinates; place them at identity to retain correspondence with the query. Physical collision remains separately authored. Heights describe a prepared ground surface, not an evolving fluid simulation.

## Rendering without texture rebakes

`WetGroundMesh3D` in `Aurelian.World.Scenes` creates tangent-framed ground geometry while retaining metre-scaled repeating UVs. Existing high-frequency material textures continue to tile independently of the field resolution. Its water output clips each ground triangle against one millimetre of standing-water depth. It uses the existing water material, optical transport, waves, impacts, and planar capture. There is no second reflection renderer or independently authored puddle outline.

`ModelMaterial.VertexWetness` opts opaque vertex alpha into a wetness channel. Base opacity must be one, and base-color texture alpha must be fully opaque; alpha masks, blends, unlit materials, and transmission reject this mode. The shader applies the default `WetSurface3D` response continuously across triangles: darkening `.35`, roughness multiplier `.3`, and normal multiplier `.55`. Custom CPU profile coefficients do not change these shader constants.

The existing packed material flags carry specular filtering in bit 0 and vertex wetness in bit 1. Forward and shared-surface evaluation use the same flag decoding; bindless, visibility, and secondary material sampling inherit the shared evaluator. Buffer strides, descriptors, and vertex layout remain unchanged. Custom compiled shader bundles must rebuild against the updated flag semantics. Defaults retain their previous behavior and content identity encoding.

Ground height and wetness vary at the authored grid resolution. Fine texture relief remains shading detail. Water-depth queries describe the base water level, excluding animated millimetre-scale waves.

## Audio follows the environment

`EnvironmentalAmbience` in `Aurelian.Audio` owns two ordinary looping voices on identity-specific buses. Explicit rain, shelter, elapsed time, and pause inputs drive exponential smoothing and an authored exterior/interior blend. The interior layer is quieter; `ProceduralSoundKind.ShelteredRain` supplies filtered noise, and `WaterStep` supplies a bounded transient splash. Existing voice capacity, resource preparation, mixer output, and disposal remain authoritative.

The application supplies a unique projection identity and owns its controller for the scene lifetime. Prepare resources during loading, dispose ambience before its runtime, and project focus loss into its pause input. No wall clock, physics query, or rendering dependency enters the audio module.

The gallery's `RainStreetAudio` maps accepted camera movement distance to a 1.3-metre stride. It queries the same ground field for dry, wet, and standing-water footsteps, and projects the shelter volume into ambience. This is an inspection gallery with ground-height following, not a new FPS collision controller.

## Inspect and reproduce

```powershell
dotnet build tools/Aurelian.GraphicsProof -c Release -m:1
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --rain-night --view
dotnet run --project tools/Aurelian.GraphicsProof -c Release --no-build -- --rain-night --output artifacts/local/connected-weather
dotnet run --project tools/Aurelian.SurfaceResponseProof -c Release
```

Arrow keys walk across the street. The awning near X = -3.4, Z = 11 shelters a dry patch; the foreground basin near X = .2, Z = 4.8 produces splash steps. **R** toggles rainfall and its ambience target, **W** overrides surface wetness for comparison, **P** hides puddles and changes footstep classification, **N** toggles normal maps, **Space** pauses, and **Esc** exits. Field wetness persists when rain stops; there is no evaporation or accumulation simulation. The two decorative rain volumes are authored away from the awning, not clipped against roofs by a new particle collision system. The window uses NAudio when available and reports a device error before using silent output otherwise. Hidden smoke and offline qualifications use silent output deliberately.

The finite native proof compares wet and dry ground, checks a sheltered query, and traverses a dry/exposed/puddle audio route. It retains rain replay, rewind, drain, reflection, ripple, normal-map, roughness-map, cavity, non-emission, shutter, and GPU-pool parity checks. On the Radeon 780M the connected field changes **266,017 pixels**. The route produces **1 dry, 4 wet, and 3 splash footsteps**, with the exterior ambience gain falling to **.0235** under shelter and returning to **1.0** outside. These are one-scene qualification results, not a production-scene performance claim.

`WeatherCooperationTests` independently checks mesh/query interpolation, clipped water coverage, immutable field ownership, invalid inputs, shelter response, dry/wet mode admission, mixer pause/disposal, and frame-subdivision independence of the ambience blend. `SurfaceResponseProof` compares CPU and GPU wetness at 0, .5, and 1, with specular filtering independently enabled, in both forward and shared-surface rendering.

The complete standalone regression passes **1,387 tests in 20 assemblies**, with no failures or skips. The core build has zero warnings/errors. The full integration build passes with 14 existing unassigned-field warnings in the vendored OpenFont sources and zero errors. The final native comparison has **zero byte error in all twelve CPU/GPU wetness cases**. The connected gallery also passes its twelve-frame window smoke test with no particle-budget overflow. Native evidence is under `artifacts/local/connected-weather`, `artifacts/local/connected-weather-window`, and `artifacts/local/surface-response`; the separate browser-toolchain test remains outside the local core total.
