# Saltline capability integration

Three independently authored capabilities now integrate with Aurelian's existing material and audio owners. The [source audit](saltline-capability-research.md) records the inspiration and the larger candidate list. No Saltline implementation or bundled library was copied.

## Wet material response

`WetSurface3D` applies authored darkening, roughness reduction, and normal damping to an ordinary `ModelMaterial`. Alpha, metallic response, emission, and texture references remain unchanged. Always apply it to the original dry material; repeatedly applying it to the previous wet result compounds the response.

```csharp
var response = new WetSurface3D();
ModelMaterial visible = response.Apply(dryMaterial, wetness: .85f, exposure: .7f);
float exposed = WetSurface3D.RainExposure(Vector3.UnitY, shelter: .2f);
```

The result uses existing forward, surface, retained, and bindless material upload paths. Changing explicit presentation state requires no texture rebake or new shader ABI. A zero response returns the original material. Rain exposure is an orientation approximation with explicitly supplied shelter; it does not trace roofs, simulate accumulation, or create a water layer.

The rain gallery now bakes dry asphalt once and applies runtime wetness to each presentation snapshot. **W** toggles this substrate response independently of **R** rain and **P** puddles. The follow-up stabilization pass restores the sibling workspace, builds the full solution, and launches the gallery for a twelve-frame native smoke test. The independent native proof below qualifies the material response directly; manual toggle behavior remains a separate interaction check.

## Specular filtering

Set `ModelMaterial.SpecularAntiAliasing = true` to increase roughness when filtered normal vectors have lost length. The response accounts for normal scale and is disabled when no normal map is present. It is shared by surface and visibility evaluation and used by forward opaque, transparent, and refractive shaders.

The opt-in flag occupies the existing unused fourth material flag component. Uniform sizes, descriptor layouts, and resident-table strides remain unchanged. Scene identity includes an explicit marker when filtering is enabled; defaults retain their previous identity encoding.

This is a filtered-normal variance approximation. The native witness establishes a changed highlight response and agreement between two paths, not a measured reduction in temporal shimmer across production scenes. Distance and motion qualification remain necessary before making this a default.

## Procedural audio preparation

`ProceduralSoundRecipe` produces seeded mono PCM resources for footsteps, metal impacts, and rain. Resources enter the existing `AudioResourceScope` and mixer, so normal event dedupe, gain, spatial projection, voice lifetime, and disposal apply.

```csharp
var recipe = new ProceduralSoundRecipe(ProceduralSoundKind.MetalImpact, Seed: 73);
AudioClipResource impact = resources.Add(recipe.Prepare(new("metal-impact")));
runtime.Play(new AudioCue(new("accepted-impact-42"), impact.Id, AudioBusId.Sfx));
```

Preparation bounds sample rate to 8–96 kHz and duration to .01–10 seconds, supports cancellation, and hashes canonical little-endian sample bytes plus rate. Seeds reproduce PCM on the tested runtime. Generate during loading or on a worker; generation is synchronous and does not belong in the audio callback. Rain defaults to looping with quiet endpoints. It is a synthetic ambience recipe, not a seamless stationary recording or a streamed resource. Synchronized music stems and environmental reverb routing remain separate capabilities.

## Reproduce the native witness

```powershell
dotnet run --project tools/Aurelian.SurfaceResponseProof -c Release
dotnet test tests/Aurelian/Aurelian.Rendering.Contracts.Tests -c Release
dotnet test tests/Aurelian/Aurelian.Audio.Synthesis.Tests -c Release
dotnet test tests/Aurelian/Aurelian.Shaders.Tests -c Release --filter "FullyQualifiedName~StaticModelShaderTests|FullyQualifiedName~GraphicsStarterShaderTests|FullyQualifiedName~BindlessGraphicsTests|FullyQualifiedName~VisibilityGraphicsTests"
```

The independent tool uses existing Vulkan rendering and built-in shader compilation. It compares identical 128×128 geometry with response settings changed, checks exact zero-wetness output and unchanged unmapped output under specular filtering, writes RGBA8 captures, exports three PCM16 WAV examples, and publishes `artifacts/local/surface-response/evidence.json` only after its image checks pass. It initially writes `Accepted: false`, so a failed run cannot leave a successful evidence record.

On the AMD Radeon 780M, wetness changes **6,084 pixels** and specular filtering changes **5,616 pixels**, in both forward and shared-surface paths. The CPU/mixer suites pass **80 material-contract tests** and **4 synthesis tests**. The focused shader suites pass **51 tests**, including validated SPIR-V and unchanged resource contracts. Graphics and World projects build with zero warnings/errors.

The [follow-up stabilization pass](stabilization.md) fixes the invalid-SPIR-V driver submission, restores sibling dependencies and checkout fixtures, and adds standalone engine CI. The graphics suite now passes 351 tests, the core lane passes 1,379 tests, and the full solution builds successfully. Windows Application Control initially blocked several assemblies; the user disabled that block and the retry passed. The separate browser-toolchain test and full optional integration test matrix are outside that core result.

## Remaining candidates

The [connected-weather extension](connected-weather.md) now provides shared triangulated wet-ground data, clipped water boundaries, authored shelter exposure, surface footsteps, and environmental ambience. The rain gallery consumes these together and replaces its independent puddle outlines with the shared field.

The [Beacon first-person integration](beacon-first-person.md) now adds a reusable accepted-state motion controller, spring recoil/landing response, bob, look sway, aim/sprint FOV, surface footsteps, water impacts and shelter ambience in the ordinary FPS path. Its shared Ambient voices follow the host lifecycle and volume controls.

The [viewmodel/quality extension](beacon-viewmodel-and-quality.md) now adds separate geometric weapon depth, a near-field camera, conservative wall clearance and a persisted Adaptive quality switch over the existing measured Dominatus LoD/HZB policy. Beacon's opaque arena/ground is resident, while water remains in the later refractive pass. The policy bridge retains measurements across continuing scene snapshots.

## Remaining-source ledger

The embedded source is **not exhausted**. This ledger separates qualified extractions from distinct follow-ups; completed surface/audio/FPS work does not imply the remaining capabilities:

| Idea | Current disposition | Useful acceptance case |
| --- | --- | --- |
| Distance-aware alpha coverage | Still useful. `SL_ACUT` adjusts cutoff/coverage as fence wires shrink; Aurelian's ordinary material cutoff remains fixed. | A fence consumer with distance and moving-camera coverage/shimmer comparisons in material raster and visibility resolve. |
| Synchronized music layers | Implemented by [LayeredMusic](gameplay-extraction.md): one mixer cursor and logical voice with sample ramps. Beacon consumes it. | Exact loop/channel PCM, pause/reset, Music volume, atomic admission, whole-group stealing and exported real score mixes pass. |
| Jump forgiveness | Extracted from Cloudstep into [PlatformerCharacter3D](platforming.md), including air control, short hops, platform momentum and edge handling. | Full before/after tour traces are byte-identical; native course completion, mid-ride restore, coyote expiry and buffered landing pass. |
| Crouch clearance | Remains a separate stance feature; Cloudstep's jump helper does not implement crouching. | A crouching consumer with obstructed stand-up and saved stance/body dimensions. |
| Visibility/recent-use spawn scoring | Implemented by [SpawnSelector3D](gameplay-extraction.md), consumed by Beacon's existing resolver and brains. | Distance, cover, recent use, occupancy/fit, deterministic history and nine-creature fight completion pass. Staggered scheduling remains separate. |
| Wet orientation, streaks and beads | Partial extraction. Shared wetness/normal damping and ground exposure exist; those extra procedural visual details were not ported. | One authored wet prop with sheltered/upward/vertical faces and close/distant captures, avoiding a parallel material system. |
| Reverb/convolution routing | Distinct audio work. Saltline uses convolver nodes; the current shelter blend uses prepared filtered layers. | A bounded mixer-owned effect with dry/wet routing, lifecycle and offline audio qualification. |
| Cooperative/prioritized audio preparation | No current demonstrated loading bottleneck in the small bounded recipes. | A measured boot/frame stall before adding a new scheduling/cache owner. |
| Automatic render scale/preset ladder | Defer until a measured workload needs it. The new setting reuses actual GPU-cost LoD/HZB decisions; Beacon's measured baseline beats HZB and already fits the reference frame budget. | A GPU-bound consumer showing a net gain after scale/resize/history transitions, respecting manual resolution and capture reproducibility. |

Existing planar reflections, water transport, rain, authored PBR maps, exposure/post-processing and playtest diagnostics already cover those foundations. Recreating them from the bundle would duplicate owners. The remaining work is narrow integration and qualification of the rows above, not a wholesale HTML port or a claim of visual parity.
