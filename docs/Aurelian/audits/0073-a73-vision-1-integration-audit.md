# A73 — Vision 1 integration audit

## 1. Files changed

- `docs/architecture/visions-1.md`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/architecture/aurelian-checkpoint-a72.md`
- `docs/audits/0073-a73-vision-1-integration-audit.md`

## 2. Task scope

A73 is a docs-only integration pass for `docs/architecture/visions-1.md` after the A72 visible-sample + shader-asset-bridge checkpoint.

The pass reviewed the human-authored vision, compared it to the checkpoint and current architecture doctrine, added only status/boundary clarification, linked Vision 1 from public roadmap/checkpoint entry points, and recorded this audit.

A73 deliberately does not implement features, add projects, add packages, modify production source, modify vendor/Dominatus, modify CodeReferences, create renderer adapters, import Machina UI, implement materials, integrate Margaret, implement ray marching, implement LLM/DM systems, implement multi-GPU, or change the dependency graph.

## 3. Vision document reviewed

Reviewed `docs/architecture/visions-1.md` as the post-A72 north-star document.

The document preserves the A72 thesis:

```text
Aurelian is not an engine with AI bolted on.

Aurelian is an engine whose core orchestration model is Dominatus-shaped.
```

The document also preserves the stronger doctrine that Aurelian owns explicit orchestration and world truth while graphics, UI, external renderers, reference renderers, tools, and future LLM systems operate through typed contracts, policy/mechanism seams, facts, acts, and results.

## 4. Consistency checks

### Current implementation vs future ideas

Vision 1 accurately separates the current executable proof from future work. The current path is the visible triangle sample using sample-owned Vulkan setup, asset-manifest shader loading, checked-in shader artifact TOML plus `.spv.hex` files, neutral `CompiledShaderProgram` contracts, Core frame-loop orchestration, a Dominatus-backed Runtime session, Runtime compositor policy, Core compositor bridge, Vulkan compositor mechanism, and sample-local presentation.

A status note was added near the top to make explicit that external renderers, Machina UI, Margaret, ray marching, multi-plant/multi-GPU, and LLM/DM gameplay are directional goals unless explicitly marked otherwise.

### Dumb renderer strategy

The external renderer section is consistent with current boundaries because it describes Unreal, Unity, O3DE, and Stride as future dumb renderer plants, not as current integrations. The strategy keeps Aurelian as world/policy owner and treats external engines as pixel mechanisms that would consume snapshots, command plans, material/mesh/light/camera data, UI surfaces, or debug overlays through explicit contracts.

No adapter exists today and none was added in A73.

### Wyrmcoil material TOML / MaterialX direction

The material section is aspirational and consistent with current repo reality. Aurelian currently has shader artifacts and asset-manifest shader references, but not a material system. Vision 1 correctly frames TOML materials, flattened MaterialX-shaped semantics, lowered artifacts, parameter/binding data, texture references, validation, and renderer compatibility hints as future design direction.

No material project, runtime material loader, MaterialX dependency, or material asset system was added in A73.

### Machina UI / Avalonia direction

The UI section is consistent as a future audit direction. It treats Machina UI as a separate project, recommends practical Avalonia use, and argues for strict UI/3D separation through surfaces and compositor boundaries.

No Machina code, Avalonia package, UI project, CodeReferences import, or renderer/UI coupling was added in A73.

### Margaret reference renderer direction

The Margaret section is consistent as a long-term plant concept. It describes Margaret as a future unbiased/reference renderer for lighting/material/camera validation, cinematic/still output, approximation calibration, and differential-rendering confidence rather than as the normal real-time renderer.

No Margaret integration, project reference, adapter contract, or reference-renderer plant was added in A73.

### Ray marching direction

The renderable-taxonomy section is consistent as future design pressure. The repo has no ray marching implementation today. Vision 1 correctly says mesh rendering should not be derailed, while avoiding a hard-coded assumption that every future renderable must be a mesh.

No ray marching asset, SDF runtime, shader path, renderer support, or taxonomy implementation was added in A73.

### LLM/DM future direction

The LLM/DM section is consistent with the Dominatus-shaped thesis because it keeps LLM behavior behind policy decisions, bounded acts, validation, and typed actuators. It does not claim that current Runtime implements a dramaturge, scene generator, dialogue system, quest system, persistence layer, or gameplay loop.

No LLM package, prompt system, DM policy, act contracts, validators, or gameplay integration was added in A73.

### Multi-plant/multi-GPU future direction

The multi-plant section is consistent with current seams: `PlantId`, `PlantContext`, `PlantOutputRef`, required-output sets, compositor policy kinds, compositor diagnostics, Dominatus policy, and graphics mechanism boundaries exist to preserve future optionality. Vision 1 correctly treats real multi-GPU as later work and explicitly defers device groups, external memory, cross-device image transfer, multi-adapter synchronization, and heterogeneous scheduling.

No multi-GPU implementation, device-group work, external-memory bridge, or dependency change was added in A73.

## 5. Clarifications added

Added a `Status note` block to `docs/architecture/visions-1.md` clarifying that the document is a vision document rather than an assertion that every described subsystem exists today.

The note points current implemented reality back to the A72 checkpoint, milestone audits, and MVP roadmap, and calls out external renderers, Machina UI, Margaret, ray marching, multi-plant/multi-GPU, and LLM/DM gameplay as directional goals unless explicitly marked otherwise.

## 6. Documentation links added

Added or updated Vision 1 references in:

- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/dependency-policy.md`
- `docs/architecture/aurelian-checkpoint-a72.md`

The roadmap now records A72 as the checkpoint and A73 as docs-only Vision 1 alignment. It also states that implementation pauses after A73 until human review selects A74+.

## 7. Boundary checks

A73 changed documentation only.

No source files, project files, package references, samples, vendor files, CodeReferences, renderer adapters, material systems, UI imports, Margaret integration, ray marching implementation, LLM/DM systems, multi-GPU systems, or dependency graph edges were changed.

Dependency doctrine remains:

```text
Runtime must not reference Graphics.
Graphics must not reference Runtime or Dominatus.
Rendering.Contracts must remain neutral.
Core may integrate subsystems because Core is the engine spine.
Samples/integration tests may compose everything.
```

## 8. Validation results

Planned validation for this docs-only pass:

```bash
test -f docs/architecture/visions-1.md
test -f docs/audits/0073-a73-vision-1-integration-audit.md
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
dotnet build samples/Aurelian.VisibleTriangle/Aurelian.VisibleTriangle.csproj -c Debug
git diff --check
git status --short
rg -n "visions-1.md|Aurelian Vision 1|Vision 1" README.md docs/architecture docs/audits -g '*.md' || true
rg -n "Aurelian.Host|ServiceLocator|Singleton|Vortice|VMASharp|Vma|CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" docs/architecture/visions-1.md docs/audits/0073-a73-vision-1-integration-audit.md || true
```

The validation commands are intentionally source-build checks plus docs/link/boundary checks because A73 is documentation-only. The required file checks, solution build, solution tests, visible sample build, and `git diff --check` passed. `git status --short` showed only the expected docs changes before commit. The link grep reported the expected Vision 1 references. The boundary grep reported only expected future-direction mentions in the vision/audit text, not source or dependency changes.

## 9. Deferred features

Deferred beyond A73:

- Dumb renderer adapter contracts and feasibility audits.
- Unreal/Unity/O3DE/Stride adapter implementation.
- Machina UI carryover or Avalonia integration.
- Material TOML / flattened MaterialX design and implementation.
- Margaret/reference renderer plant integration.
- Ray marching, SDF, or broader renderable taxonomy implementation.
- LLM/DM gameplay acts, validators, persistence, or scene/dialogue/quest systems.
- Multi-plant/multi-GPU device groups, external memory, cross-device transfer, multi-adapter synchronization, or heterogeneous scheduling.
- Production host/application lifecycle, full renderer, asset manager/cache/hot reload, resize handling, descriptor/uniform path, and material/mesh/texture asset pipeline.

## 10. Next recommendation

Human review before A74.

Candidate A74 directions:

- Dumb renderer adapter audit.
- Machina UI carryover audit.
- Material TOML / flattened MaterialX design audit.
- Margaret/reference renderer plant audit.
- Renderable taxonomy audit.
