# A72 — Checkpoint after visible sample + shader asset bridge

## 1. Files changed

- Added `docs/architecture/aurelian-checkpoint-a72.md` as the checkpoint architecture review after the A0-A71 run.
- Added this A72 audit report.
- Updated `README.md` to record that A72 exists and that implementation should pause for review before A73.
- Updated `docs/architecture/mvp-roadmap.md` to add the A72 checkpoint/review milestone.
- Updated `docs/architecture/dependency-policy.md` to record the A72 dependency-boundary status and review pause.

No production source, project, package, vendor, or CodeReferences changes were made.

## 2. Task scope

A72 is docs/checkpoint only. It summarizes the current Aurelian state after:

- visible triangle sample;
- Core frame loop;
- Dominatus-backed runtime session;
- compositor policy/mechanism split;
- Vulkan graphics path;
- shader artifact and asset manifest bridge.

A72 does not implement new production code, add projects, add packages, modify `vendor/Dominatus`, modify CodeReferences, add features, or change architecture beyond docs/status recommendations.

The convergence state is success: the intended checkpoint documents exist, they summarize the real current path and deferred work, and build/test validation still passes.

## 3. Current demo path

The current demo path is:

```bash
dotnet run --project samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
```

The sample is human/local-run oriented and should not be run automatically in headless CI. It requires Vulkan presentation support and a windowing environment.

What it proves:

- `Assets/assets.toml` is loaded by the sample;
- shader id `smoke_triangle` is resolved through `Aurelian.Assets`;
- TOML + `.spv.hex` shader artifacts are decoded/hash-checked to real SPIR-V bytes;
- a neutral `CompiledShaderProgram` reaches Vulkan pipeline setup;
- Core frame loop and frame pump run finite frames;
- a Dominatus-backed runtime session is started and ticked;
- runtime compositor policy and Core bridge dispatch to the Vulkan compositor mechanism;
- per-frame swapchain acquire/present happens;
- sample-local event pumping observes close requests and can stop cleanly.

What it does not prove:

- production host/window lifecycle;
- production input system;
- infinite game loop;
- material/mesh/texture asset systems;
- runtime shader compilation;
- world-driven rendering;
- descriptor/uniform/camera/depth paths;
- resize/swapchain recreation;
- multi-GPU or differential compositor behavior;
- LLM/DM/emergent gameplay behavior.

## 4. Project/dependency state

Observed project-reference shape:

- Core -> Runtime, Rendering.Contracts, Graphics.
- Runtime -> World, Rendering.Contracts, vendored Dominatus.Core.
- Graphics -> Rendering.Contracts and Silk.NET Vulkan/windowing packages.
- Rendering.Contracts -> no Aurelian project references.
- Assets -> Rendering.Contracts, Shaders, Tomlyn.
- Shaders -> Rendering.Contracts and the DXC tool package.
- Visible sample -> Assets, Core, Graphics, Runtime, Rendering.Contracts.
- Integration tests -> Core, Runtime, Graphics, Rendering.Contracts, plus linked SPIR-V fixture source from graphics tests.

Risk notes:

- Core's Graphics reference is currently intentional for the Vulkan compositor adapter, but should remain narrow.
- Assets -> Shaders is acceptable for the current artifact-loader bridge, but must not become runtime compiler ownership.
- The sample's direct Graphics dependency is acceptable while the sample is the composition executable and no production host exists.

## 5. Real vs scaffold

Real implementation:

- world units/data stores and actuator request/result contracts;
- rendering/shader/compositor contracts;
- Dominatus-backed runtime session and runtime tick path;
- Core frame loop/frame pump/runtime-step/presentation-mechanism integration;
- Runtime compositor policy and Core/Graphics mechanism bridge;
- Vulkan device/resource/pipeline/submit/presentation/compositor M0 path;
- shader artifact TOML + `.spv.hex` loading and hash validation;
- asset manifest shader references and visible sample consumption.

Scaffold/proof-only:

- visible sample owns setup and windowing;
- finite frame loop is sample-oriented, not a production game loop;
- static triangle output is reused for planned frame IDs;
- current compositor is passthrough/copy proof, not differential composition;
- no full world -> render -> Vulkan execution path;
- checked-in sample artifacts are not yet produced by a production content build pipeline.

## 6. Boundary health

Boundary scan classifications:

- Runtime for graphics/Vulkan/window terms: clean.
- Graphics for Runtime/Dominatus/policy terms: clean.
- Rendering.Contracts for Runtime/Graphics/Vulkan/Dominatus terms: clean.
- sample/Graphics for shader compiler/runtime compiler terms: clean.
- Assets for graphics/Vulkan terms: expected false positive in an assets test assertion that checks Graphics is not referenced.
- broad risky terms: acceptable intentional exceptions inside Vulkan code (`StructureType`, `ImageType`, `ViewType`), allocator enum option `Vma`, tests asserting Vortice absence, boundary/property reflection in tests, and diagnostic `ex.GetType().Name` formatting.

No needs-follow-up boundary issue required an A72 source change.

## 7. Build/test validation

Validation commands run during A72:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
test -f docs/architecture/aurelian-checkpoint-a72.md
test -f docs/audits/0072-a72-checkpoint-visible-sample-shader-asset-bridge.md
git diff --check
git status --short
```

The visible sample was not run automatically because this environment is not assumed to have Vulkan presentation/windowing support.

## 8. Key architecture conclusions

- Aurelian's integration spine is now real enough to review: assets, shaders, Core, Runtime, Graphics, and the visible sample all connect through typed seams.
- Aurelian is not an engine with AI bolted on. Aurelian is an engine whose core orchestration model is Dominatus-shaped.
- Stride's engine core was effectively an implicit, reflection-heavy, less-local version of what Aurelian is making explicit through Dominatus, typed contracts, and actuator boundaries.
- The dependency boundaries that mattered most held: Runtime did not absorb Graphics, Graphics did not absorb Runtime/Dominatus, Rendering.Contracts stayed neutral, and the visible sample did not add runtime shader compiler coupling.
- The engine is still pre-MVP and should not mistake the visible triangle for production usability.

## 9. Deferred future goals

Deferred future goals include:

- production host/window/input lifecycle;
- material/mesh/texture assets;
- asset cache/hot reload;
- runtime shader compilation;
- world-driven visible rendering;
- depth/descriptors/uniforms/cameras/resize handling;
- differential compositor;
- explicit multi-GPU/device-group/external-memory work;
- LLM/DM/emergent gameplay.

Multi-plant/multi-GPU position:

> plant-shaped from day one, multi-GPU later

The existing seams (`PlantId`, `PlantOutputRef`, `RequiredPlantOutputSet`, compositor policy/mechanism split, Dominatus policy, and graphics mechanism boundaries) preserve future options without making explicit multi-GPU an immediate priority.

LLM/DM/emergent gameplay position:

LLM DM/emergent gameplay should wait until core usability, explicit world facts, validated world actuation, proposal contracts, runtime policy integration, and safety/consistency validators exist. When it arrives, it should be bounded Dominatus/actuator-driven policy, not direct world mutation.

## 10. Recommended next options

### Option A — Basic engine usability

- material/mesh assets;
- render command plan -> Vulkan;
- simple world -> visible render.

### Option B — Runtime/world gameplay spine

- world tick facts;
- Dominatus world policies;
- simple NPC/scene acts;
- validators for policy-driven actuation.

### Option C — Graphics completeness

- depth buffer;
- resize/swapchain recreation;
- descriptor sets/uniform buffers;
- camera matrices.

### Option D — Authoring/tooling

- SDSL-V semantic annotations;
- `.sdslvtest`;
- asset tool builds shader artifacts;
- content-pipeline docs.

### Option E — Sample/productization

- improve visible sample;
- package sample assets;
- documentation and onboarding;
- local-run/visual-smoke checklist.

## 11. Suggested checkpoint questions for human review

- Should A73 prioritize basic usability, graphics completeness, runtime/world gameplay, authoring/tooling, or sample/productization?
- Is Core's current direct Graphics dependency still acceptable for the next few milestones, or should host/application composition be introduced soon?
- Should the next visible proof be world-driven rendering or a stronger graphics sample?
- Should the asset pipeline grow toward material/mesh first or shader-build tooling first?
- How much sample-local window/Vulkan setup should remain before introducing an `Aurelian.Host` shape?
- Which technical debt must be paid before any LLM/DM/emergent gameplay work?
- What minimum graphics usability should exist before revisiting explicit multi-GPU?

## 12. Next recommendation

Review with human before selecting A73.
