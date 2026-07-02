# Aurelian.VisibleTriangle Topology Audit M13g

## Purpose

M13g audits `samples/Aurelian.VisibleTriangle` as the future visible proof target for `VD-MIR` work without implementing `VD-MIR`, changing the current SDSL-V compiler path, or changing renderer behavior.

M14a later implements the first compiler-side `VD-MIR M0` smoke-triangle slice upstream of this sample, but the sample itself still stays on its existing checked-in shader artifacts in this milestone.

This milestone is topology and proof-boundary work only:

- the sample is documented in its current Aurelian-owned shape
- the sample is restored to `Aurelian.slnx` because the project is present and builds cleanly
- no `VD-MIR` implementation or wiring is added
- no SDSL-V migration occurs
- no Slang/PTX backend work occurs
- no Machina/Aurelian/Vulkan integration work occurs

## Sample location

- Sample directory: `samples/Aurelian.VisibleTriangle`
- Project file: `samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj`
- Entry point: `samples/Aurelian.VisibleTriangle/Program.cs`
- Sample-local setup/runtime glue:
  - `VisibleTriangleSampleFrame.cs`
  - `VisibleTriangleFrameInputProvider.cs`
  - `VisibleTriangleSamplePresentationMechanism.cs`
  - `VisibleTriangleShaderAssets.cs`
  - `VisibleTriangleWindowState.cs`

## Solution topology

Current solution position after M13g:

```text
Aurelian.slnx
  /samples/
    Aurelian.VisibleTriangle
  /src/
    Aurelian.Actuation
    Aurelian.Assets
    Aurelian.AssetTool
    Aurelian.Core
    Aurelian.Graphics
    Aurelian.Rendering.Contracts
    Aurelian.Rendering.Null
    Aurelian.Runtime
    Aurelian.Shaders
    Aurelian.World
  /tests/
    Aurelian.* test projects
```

- `Aurelian.VisibleTriangle` was not present in `Aurelian.slnx` at the start of this audit.
- M13g adds it under `/samples/` because the project exists and `dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj` succeeds without topology fixes.
- The sample is not added to `Copeland.slnx` or `Copeland.Slow.slnx`.

## Project topology

`Aurelian.VisibleTriangle.csproj`:

- SDK: `Microsoft.NET.Sdk`
- Output type: `Exe`
- Target framework: `net10.0`
- Direct package references: none
- Direct project references:
  - `../../src/Aurelian.Assets/Aurelian.Assets.csproj`
  - `../../src/Aurelian.Core/Aurelian.Core.csproj`
  - `../../src/Aurelian.Graphics/Aurelian.Graphics.csproj`
  - `../../src/Aurelian.Runtime/Aurelian.Runtime.csproj`
  - `../../src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj`
- Content copied to output:
  - `Assets/assets.toml`
  - `Assets/Shaders/SmokeTriangle/**`

Transitive/native package pressure comes from the referenced Aurelian projects rather than from the sample project file itself:

- `Aurelian.Graphics` references `Silk.NET.Vulkan`, `Silk.NET.Vulkan.Extensions.KHR`, and `Silk.NET.Windowing`
- `Aurelian.Runtime` references `Dominatus.Core`
- `Aurelian.Assets` references `Tomlyn`
- `Aurelian.Shaders` references `Microsoft.Direct3D.DXC`

## Package and project references

Direct sample references:

| Kind | Reference | Role |
| --- | --- | --- |
| Project | `Aurelian.Assets` | loads `assets.toml` and shader artifact files into `CompiledShaderProgram` |
| Project | `Aurelian.Core` | owns engine/frame-loop/compositor bridge contracts used by the sample |
| Project | `Aurelian.Graphics` | owns Vulkan/window/device/swapchain/pipeline/compositor implementation used by the sample |
| Project | `Aurelian.Runtime` | owns the `AurelianRuntimeSession` started and ticked by the sample |
| Project | `Aurelian.Rendering.Contracts` | owns `CompiledShaderProgram`, compositor, and presentation contract types |

Important transitive package references:

| Package | Owning project | Why it matters to the sample |
| --- | --- | --- |
| `Silk.NET.Vulkan` | `Aurelian.Graphics` | Vulkan instance/device/swapchain/native interop |
| `Silk.NET.Vulkan.Extensions.KHR` | `Aurelian.Graphics` | Vulkan presentation extension support |
| `Silk.NET.Windowing` | `Aurelian.Graphics` | visible window/surface creation and event pumping |
| `Dominatus.Core` | `Aurelian.Runtime` | runtime session dependency |
| `Tomlyn` | `Aurelian.Assets` | TOML manifest parsing |
| `Microsoft.Direct3D.DXC` | `Aurelian.Shaders` | build/export path for shader artifacts elsewhere in the lane, not runtime compilation inside the sample |

## Asset layout

Current sample asset layout:

```text
samples/Aurelian.VisibleTriangle/
  Assets/
    assets.toml
    Shaders/
      SmokeTriangle/
        shader.toml
        generated.hlsl
        VSMain.spv.hex
        PSMain.spv.hex
```

Asset meaning:

- `Assets/assets.toml` is the top-level sample shader manifest
- `Assets/Shaders/SmokeTriangle/shader.toml` is the checked-in shader artifact manifest
- `generated.hlsl` is a debug/export artifact, not a runtime compilation input for the sample
- `VSMain.spv.hex` and `PSMain.spv.hex` are checked-in text-safe SPIR-V payloads consumed at runtime through `Aurelian.Assets`

Output artifact shape after build:

```text
samples/Aurelian.VisibleTriangle/bin/<Configuration>/net10.0/
  Aurelian.VisibleTriangle.dll
  Assets/assets.toml
  Assets/Shaders/SmokeTriangle/shader.toml
  Assets/Shaders/SmokeTriangle/generated.hlsl
  Assets/Shaders/SmokeTriangle/VSMain.spv.hex
  Assets/Shaders/SmokeTriangle/PSMain.spv.hex
```

## Current sample behavior

The sample currently attempts to:

1. load the checked-in smoke-triangle shader artifact through `Aurelian.Assets`
2. create a prepared visible Vulkan setup owned by the sample
3. create a small visible window titled `Aurelian Visible Triangle`
4. create a Vulkan swapchain and offscreen color target
5. draw a static triangle once into the offscreen target
6. start `AurelianEngine`
7. start a Dominatus-backed `AurelianRuntimeSession`
8. run `AurelianFrameLoop` for a finite number of frames
9. acquire/present one swapchain image per completed frame through sample-local input/presentation glue

Behavior notes:

- Yes, the sample opens a window when the environment supports it.
- Yes, the sample creates a Vulkan device and swapchain through `Aurelian.Graphics`.
- The triangle is rendered directly through Vulkan pipeline setup in `VisibleTriangleSampleFrame.Create`.
- The sample does consume Aurelian runtime abstractions:
  - `AurelianEngine`
  - `AurelianFramePump`
  - `AurelianFrameLoop`
  - `AurelianRuntimeSession`
  - prepared graphics/compositor bridge contracts
- The sample consumes compiled shader artifacts, not raw source compilation at runtime.
- The sample uses generated assets:
  - top-level `assets.toml`
  - shader artifact `shader.toml`
  - checked-in SPIR-V files
  - optional checked-in debug HLSL

## Current shader path

The current sample shader path is:

```text
historical SDSL-V source
  -> Aurelian.Shaders HLSL emission
  -> DXC
  -> SPIR-V artifact export
  -> checked-in shader.toml + VSMain.spv.hex + PSMain.spv.hex + generated.hlsl
  -> Assets/assets.toml
  -> Aurelian.Assets ShaderAssetManifestLoader
  -> CompiledShaderProgram
  -> Aurelian.Graphics Vulkan pipeline creation
  -> visible triangle sample
```

Important current facts:

- The sample does not compile shaders at build time in its own project file.
- The sample does not compile shaders at runtime.
- The sample does not load SDSL-V source directly.
- The sample does not load inline HLSL directly.
- The sample does load checked-in shader artifact files at runtime.
- `generated.hlsl` is documented in `shader.toml`, but the runtime path uses the SPIR-V stage files.

## Current runtime/render path

The current runtime/render path is:

```text
sample-local prepared Vulkan/window/swapchain setup
  -> checked-in shader artifact load
  -> sample-local offscreen triangle draw setup
  -> AurelianPreparedGraphicsSubsystem
  -> CompositorActuationBridge
  -> AurelianEngine
  -> AurelianFramePump
  -> AurelianRuntimeSession
  -> AurelianRuntimeTickFrameStep
  -> AurelianFrameLoop
  -> sample-local swapchain acquire input provider
  -> runtime compositor policy
  -> Core compositor bridge
  -> Vulkan compositor passthrough
  -> sample-local present mechanism
```

Ownership by concern:

- Window/device/swapchain creation:
  - sample-owned orchestration
  - concrete implementation comes from `Aurelian.Graphics` Vulkan/windowing types
- Render plan / prepared graphics:
  - sample builds the prepared visible graphics bundle and finite output mapping
  - `Aurelian.Core` validates and pumps that prepared graphics subsystem
- Shader loading:
  - `Aurelian.Assets` owns manifest parsing and shader artifact loading
- Draw call setup:
  - sample-local code creates the offscreen render pass, framebuffer, graphics pipeline, vertex buffer, upload, and initial triangle draw
- Lifecycle:
  - sample owns outer setup/disposal and local event pumping
  - `AurelianEngine`, `AurelianFramePump`, `AurelianFrameLoop`, and `AurelianRuntimeSession` own their existing engine/runtime lifecycles
- Sample-only glue:
  - `VisibleTriangleFrameInputProvider`
  - `VisibleTriangleSamplePresentationMechanism`
  - `VisibleTriangleWindowState`
  - finite frame/output mapping and pending present queue

## Build and run requirements

Build requirements:

- .NET SDK with `net10.0` support
- restore access for NuGet packages already referenced by the repo

Run requirements:

- local Vulkan-capable graphics environment
- native window/display environment
- Vulkan presentation support usable through `Silk.NET.Windowing`
- platform support sufficient for surface extension discovery, device setup, swapchain creation, and presentation

Requirement answers:

- Does it require native graphics/windowing packages? Yes, transitively through `Aurelian.Graphics`.
- Does it require local GPU/Vulkan support to run? Yes.
- Does it require shader compilation at build time, runtime, or both? Neither in the sample path itself; it loads precompiled checked-in shader artifacts at runtime.

## Validation results

Commands run for M13g:

```powershell
dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -- --frames 1 --no-hold
```

Observed sample results:

- `dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj`: passed
- `dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -- --frames 1 --no-hold`: failed in the current environment before visible presentation

Observed run failure:

```text
Vulkan presentation plant creation failed: AGV1011: Vulkan presentation was requested, but Silk.NET.Windowing could not report required surface extensions: InvalidOperationException: Vulkan functions can only be used after initialization (just before the Load callback is executed)
```

Interpretation:

- the sample is buildable in the current environment
- the sample is not safely runnable here as an automated proof gate
- the failure is environmental/runtime-path evidence, not a reason to rewrite the sample for M13g

## VD-MIR future insertion point

The future `VD-MIR` insertion point is upstream of the current checked-in shader artifact boundary and downstream of source-language frontend work:

```text
SDSL-V source
  -> SDSL-V frontend/lowering work
  -> future VD-MIR M0
  -> future HLSL backend
  -> existing DXC -> SPIR-V artifact export path
  -> existing shader.toml + .spv.hex runtime artifact boundary
  -> existing Aurelian.Assets loader
  -> existing visible triangle runtime/render path
```

Upstream of `VD-MIR`:

- SDSL-V source text, syntax, validation, and source-provenance logic
- future frontend/lowering that turns shader-language meaning into a backend-oriented MIR

Downstream of `VD-MIR`:

- HLSL emission
- DXC invocation
- SPIR-V artifact export
- checked-in/runtime artifact file policy
- `Aurelian.Assets` manifest loading
- `CompiledShaderProgram`
- Vulkan graphics pipeline creation
- swapchain acquire/present path

## Aurelian-owned boundaries

These should remain Aurelian-owned even after future `VD-MIR` work:

- window/device/swapchain policy and native realization
- renderer contracts
- `CompiledShaderProgram` runtime-facing export boundary
- shader/runtime artifact file policy (`assets.toml`, `shader.toml`, checked-in `.spv.hex`)
- visible triangle sample application
- Vulkan compositor and draw execution path
- render plan / prepared graphics ownership
- engine/runtime/frame-loop lifecycle integration

## Copeland / VD-MIR candidate boundaries

These may later become Copeland / `VD-MIR` owned if later milestones earn the move:

- SDSL-V frontend if migration is later justified
- source spans and diagnostics conventions that prove reusable
- `VD-MIR` module / entry point / type / value / stage-IO model
- HLSL backend from `VD-MIR`
- later Slang backend
- later PTX backend

These are candidate compiler boundaries only. M13g does not implement them.

## Risks

- the sample currently depends on real window/presentation timing and is not a headless proof target
- the sample's current proof value is strongest at the artifact/runtime boundary, not at the source-language boundary
- sample-local Vulkan setup and finite-frame glue are intentionally narrow, so future proof work must avoid absorbing renderer/runtime ownership into compiler code
- the checked-in artifact path proves runtime consumption today, but it does not by itself prove future `VD-MIR` insertion until `M14a`/`M14b`

## What changed

- `Aurelian.VisibleTriangle` was audited and documented
- `Aurelian.VisibleTriangle` was added back to `Aurelian.slnx`
- deterministic M13g manifest files were added under `artifacts/m13g`
- lightweight topology/boundary tests were added
- roadmap/doctrine docs were updated to state that M13g is audit/topology only

## What did not change

M13g did not:

- implement `VD-MIR`
- wire visible triangle to `VD-MIR`
- migrate SDSL-V into Copeland
- change HLSL/DXC emission behavior
- implement Slang
- implement PTX
- bridge Machina and Aurelian
- change Aurelian renderer architecture
- merge Aurelian into Copeland solutions
- rename the repository

After M14a, these non-changes are still true for the sample runtime:

- the sample is still not wired to `VD-MIR`
- the checked-in runtime artifact boundary is still preserved
- renderer/runtime behavior is still unchanged

## Deferred work

- `M14a`: implement `VD-MIR M0` for the smoke-triangle path
- `M14b`: prove visible triangle through `VD-MIR -> HLSL/DXC -> SPIR-V`
- later: revisit whether the sample can grow a deterministic headless proof seam without changing the Aurelian-owned presentation boundary
