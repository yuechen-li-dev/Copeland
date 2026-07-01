# Aurelian Visible Triangle sample

This A71 sample executable demonstrates the current prepared-visible Aurelian engine spine across a small finite set of visible frames:

```text
sample-owned prepared Vulkan setup
  -> Aurelian.Assets manifest shader load (`Assets/assets.toml` -> `smoke_triangle` -> checked-in `.spv.hex`)
  -> AurelianEngine
  -> AurelianRuntimeSession
  -> AurelianFrameLoop
  -> runtime tick each frame
  -> frame pump
  -> sample-local event pump before acquire
  -> per-frame swapchain acquire
  -> Runtime compositor policy
  -> Core compositor bridge
  -> Vulkan compositor mechanism
  -> per-frame present
  -> sample-local event pump after present
```

`Assets/assets.toml` is consumed by the sample at startup and contains the shader reference:

```toml
[[shaders]]
id = "smoke_triangle"
path = "Shaders/SmokeTriangle/shader.toml"
```

The sample code loads this manifest through `ShaderAssetManifestLoader`, resolves shader id `smoke_triangle`, and passes the loaded neutral `CompiledShaderProgram` into pipeline setup. The C# sample path no longer directly loads `Assets/Shaders/SmokeTriangle/shader.toml`.

## Run

```bash
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
```

Optional flags:

* `--validation` enables Vulkan validation when the plant is created.
* `--no-hold` presents through the finite frame loop and exits immediately instead of pumping the visible window for a short pause.
* `--frames N` selects a positive finite frame count. The default is `3`; values above `300` are capped to avoid an accidental long-running loop.

## Expected behavior

When Vulkan presentation and a windowing environment are available, the sample opens a small window titled **Aurelian Visible Triangle**, loads `Assets/assets.toml` through `Aurelian.Assets`, resolves shader id `smoke_triangle` to the checked-in TOML + SPIR-V shader artifact, renders a static triangle once to an offscreen Vulkan color target, starts `AurelianEngine`, starts a Dominatus-backed `AurelianRuntimeSession`, and runs `AurelianFrameLoop` for the selected finite frame count.

For each frame, the sample-local input provider first pumps the owned window's platform events, checks whether close was requested, and stops by returning `null` if the user has closed the window. Otherwise it acquires a fresh swapchain image, creates a frame-specific `PresentationTargetRef`, creates frame-specific `AurelianFrameInput`, and records sample diagnostics. The runtime tick and compositor policy run each frame; the Core bridge dispatches the Vulkan compositor passthrough each frame; the sample-local presentation mechanism then presents the exact image index acquired for that completed frame and pumps window events again after presentation.

When a close request is observed before a new acquire, `AurelianFrameLoop` stops through its existing `InputProviderCompleted` completion path and the sample prints `Window close requested; stopped frame loop.` along with frame/pump diagnostics. If the requested finite frame count is reached first, the sample exits normally after the selected number of frames.

The offscreen triangle is static/reused for M0. Setup creates finite `PlantOutputRef` wrappers for each planned frame ID, and each wrapper resolves to the same offscreen texture so this milestone exercises acquire/present lifecycle rather than animation or redraw scheduling.

If Vulkan, presentation, or the windowing platform is unavailable, the sample prints typed diagnostics returned by the setup or frame-loop path and exits nonzero. This sample is intended for human/local runs; CI should build it but should not run it in headless environments.

## Boundaries

The sample deliberately does **not** implement an infinite game loop, editor host, `Aurelian.Host`, production input system, engine-owned window lifecycle, full asset manager/cache/hot reload, world integration, render graph, scheduler/threading system, differential compositor, material/mesh/texture asset loading, runtime shader compilation, or a runtime DXC/SDSL dependency. The triangle shaders are checked in as the A69/A69b primary artifact shape: TOML metadata (`shader.toml`), text-safe `VSMain.spv.hex`/`PSMain.spv.hex` files decoded by `Aurelian.Assets` into raw SPIR-V bytes, and optional debug `generated.hlsl`. C# SPIR-V byte arrays are fixture/bootstrap-only and are no longer used by the sample runtime path.

The sample still owns Vulkan/window/swapchain setup externally and passes prepared resources into Core. Core frame loop and frame pump remain free of Vulkan/window/swapchain creation.
