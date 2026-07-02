# Aurelian Visible Triangle sample

This A71 sample executable is the M14b golden path for a real visible triangle through the Presenter architecture. The opt-in runtime slice is:

```text
Presenter/Silk.NET backend
  -> required Vulkan instance extensions before plant creation
  -> native window host + event pump
  -> swapchain acquire / present
  -> prepared Aurelian Vulkan setup
  -> Aurelian.Assets manifest shader load (`Assets/assets.toml` -> `smoke_triangle` -> checked-in `.spv.hex`)
  -> AurelianEngine
  -> AurelianRuntimeSession
  -> AurelianFrameLoop
  -> runtime tick each frame
  -> frame pump
  -> Runtime compositor policy
  -> Core compositor bridge
  -> Vulkan compositor mechanism
  -> Vulkan presentation mechanism
  -> presenter present
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
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug -- --presenter silk --frames 120
```

Optional flags:

* `--presenter silk` selects the M14b Presenter backend. `silk` is currently the only supported backend in this sample.
* `--validation` enables Vulkan validation when the plant is created.
* `--no-hold` presents through the finite frame loop and exits immediately instead of pumping the visible window for a short pause.
* `--frames N` selects a positive finite frame count. The default is `3`; values above `300` are capped to avoid an accidental long-running loop.

## Expected behavior

When Vulkan presentation and a windowing environment are available, the sample opens a small window titled **Aurelian Visible Triangle**, asks the Presenter/Silk.NET backend for required Vulkan instance extensions before plant creation, loads `Assets/assets.toml` through `Aurelian.Assets`, resolves shader id `smoke_triangle` to the checked-in TOML + SPIR-V shader artifact, renders a static triangle once to an offscreen Vulkan color target, starts `AurelianEngine`, starts a Dominatus-backed `AurelianRuntimeSession`, and runs `AurelianFrameLoop` for the selected finite frame count.

For each frame, the Presenter backend pumps platform events before acquire, checks whether close was requested, and stops by returning `null` if the user has closed the window. Otherwise it acquires a fresh swapchain image, creates a frame-specific `PresentationTargetRef`, creates frame-specific `AurelianFrameInput`, and records presenter diagnostics. The runtime tick and compositor policy run each frame; the Core bridge dispatches the Vulkan compositor passthrough each frame; the Vulkan presentation mechanism then presents the exact image index acquired for that completed frame and pumps presenter events again after presentation.

When a close request is observed before a new acquire, `AurelianFrameLoop` stops through its existing `InputProviderCompleted` completion path and the sample prints `Window close requested; stopped frame loop.` along with frame/pump diagnostics. If the requested finite frame count is reached first, the sample exits normally after the selected number of frames.

The offscreen triangle is static/reused for M0. Setup creates finite `PlantOutputRef` wrappers for each planned frame ID, and each wrapper resolves to the same offscreen texture so this milestone exercises acquire/present lifecycle rather than animation or redraw scheduling.

If Vulkan, presentation, or the windowing platform is unavailable, the sample prints typed diagnostics returned by the setup or frame-loop path and exits nonzero. This sample is intended for human/local runs; CI should build it but should not run it in headless environments.

## Boundaries

The sample deliberately does **not** implement a full host framework, Machina/Oblivion integration, a generalized scene system, runtime shader compilation, Copeland package extraction, Slang/PTX backends, shader/kernel MIR split, or a compiler-default switch. The triangle shaders are checked in as the A69/A69b primary artifact shape: TOML metadata (`shader.toml`), text-safe `VSMain.spv.hex`/`PSMain.spv.hex` files decoded by `Aurelian.Assets` into raw SPIR-V bytes, and optional debug `generated.hlsl`. C# SPIR-V byte arrays are fixture/bootstrap-only and are no longer used by the sample runtime path.

M14b keeps the architecture split explicit: Aurelian renders, Presenter owns window/frame/input/present, and the sample only composes the two. Core frame loop and frame pump remain free of Vulkan/window/swapchain creation details even though this sample currently hosts the first Silk.NET Presenter backend implementation.

The future M14c/M14d layering direction keeps that split intact: the visible triangle path will eventually be treated as a Presenter `world` layer entry inside an explicit semantic screen stack, with future Machina HUD/overlay content added above the world layer rather than fused into the renderer path.
