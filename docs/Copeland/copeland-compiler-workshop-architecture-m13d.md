# Copeland Compiler Workshop Architecture M13d

## Purpose

M13d defines Copeland as the compiler workshop for Visionary. This milestone is doctrine, taxonomy, and roadmap work only. It does not move Aurelian shader code, create new Copeland packages, wire Machina to Vulkan, or change runtime behavior.

## Why Copeland is changing

The older repository story centered Copeland mainly around a TypeScript-like source language and a TypeScript-to-MIR-to-C# direction. That history still matters, but it is now too narrow for the broader Visionary ecosystem.

Visionary already has more than one compiler-shaped lane:

- `Copeland.Markdown` compiles a strict Markdown subset into `DocumentMir`.
- the original Copeland script lane explored TypeScript-like source into MIR and C#.
- `Aurelian.Shaders` currently hosts an SDSL-V shader lane that emits HLSL and then SPIR-V through DXC.

The next phase should not pretend those are all one language problem, and it should not pretend shaders are the only future reason Copeland exists.

## Compiler workshop doctrine

Copeland is the compiler workshop for Visionary.

It provides shared compiler primitives and conventions:

- source text
- source spans
- tokens
- diagnostics
- parser/lowering conventions
- pipeline stage contracts
- artifact manifests
- dumps/debug output
- corpus tests

It does not require every frontend to lower into one universal IR.

Instead, Copeland hosts explicit compiler lanes:

- document lanes
- script lanes
- shader lanes
- GPU kernel lanes
- numeric/scientific lanes
- future domain lanes

Shared abstractions are promoted only after two or more lanes prove the same shape.

Anti-doctrine:

- Do not invent a universal IR because a future language might need it.
- Do not require every language to share one AST/HIR/MIR.
- Do not turn Copeland into MLIR-lite.
- Do not name the whole architecture after shaders.

## Why not a universal IR

Copeland does not need one universal IR to be useful. Different lanes preserve different domain semantics:

- Markdown/document work preserves document/body structure and presentation-oriented diagnostics.
- script work preserves program semantics and runtime/lowering concerns.
- shader and GPU kernel work preserve stage restrictions, resource bindings, and execution-model constraints.
- numeric/scientific work may need array semantics, units, plotting, and interpreter-friendly representations.

Forcing those domains into one predeclared IR too early would produce a vague abstraction that serves none of them well. Copeland should earn shared layers from repeated concrete use, not from anticipation.

## Why not only Copeland.Shaders

`Copeland.Shaders` may become a useful package name for a particular lane split someday, but it is too narrow to describe the architecture as a whole.

It would understate:

- the existing Markdown/document lane
- the original script/compiler lane
- possible GPU TypeScript-like frontends
- possible PTX-oriented compute backends
- possible Oct-in-C# numeric/scientific work

The architectural question is not "where do shaders go?" The larger question is "how does Visionary host multiple explicit compiler lanes without pretending they are one language?"

## Core compiler primitives

Copeland should converge on shared primitives only where multiple lanes prove the same shape:

- source text models
- source locations and spans
- token/value conventions where appropriate
- diagnostic identifiers, severities, and formatting rules
- parser and lowering result shapes
- stable dump/debug artifact conventions
- deterministic corpus-test conventions
- manifest/report conventions for milestone and compiler outputs

These are workshop primitives, not a demand that every lane reuse the same node hierarchy.

## Compiler lane model

A compiler lane is the end-to-end path for one source domain through its own frontend, semantic models, backends, diagnostics, artifacts, and tests.

General lane anatomy:

```text
Source language / syntax
  -> frontend
  -> AST
  -> HIR when domain semantics need preservation
  -> MIR when execution/backend details need explicit representation
  -> backend
  -> artifacts
  -> diagnostics/manifests/corpus tests
```

Stages are optional, not ceremonial. A Markdown lane may not need the same middle forms as a shader lane. A GPU lane may earn a MIR only when backend pressure proves it necessary. A numeric lane may start as an interpreter plus CPU backend before any GPU lowering exists.

## Frontends

A frontend is the language-specific entry path from source text into structured compiler state. It may include lexing, parsing, syntax validation, name resolution, or other early semantic steps.

Frontend doctrine:

- frontends are explicit and domain-owned
- frontends may share source/span/diagnostic conventions
- frontends should not be forced into one master syntax stack
- a frontend may target AST directly, or AST plus HIR, depending on the lane

SDSL-V is one future Copeland frontend candidate, not the whole workshop.

## AST

AST means abstract syntax tree: a syntax-oriented representation that preserves the source language shape closely enough for parsing diagnostics, syntax-directed transforms, and early semantic analysis.

AST doctrine:

- ASTs are lane-specific by default
- AST shape should match the source language, not an imagined global language
- AST sharing is not a goal by itself

Markdown AST, script AST, shader AST, and future Oct AST should remain separate unless repeated use proves a clean shared subset.

## HIR

HIR means high-level intermediate representation: a domain-semantic representation that preserves the concepts a lane still cares about after raw syntax stops being the right shape.

HIR is appropriate when a lane needs to preserve semantics such as:

- document/body structure
- shader stage intent
- kernel resource semantics
- numeric/scientific array and unit behavior
- script-level semantic binding results

HIR is not mandatory for every lane, but it is often the right place to keep domain meaning before backend-oriented lowering begins.

## MIR

MIR means mid-level or machine-oriented intermediate representation: a representation used when execution strategy, control-flow shape, storage rules, or backend constraints need to be made more explicit.

MIR doctrine:

- MIR exists when concrete lowering pressure earns it
- MIR is not universal by default
- one lane's MIR does not define another lane's MIR

`DocumentMir` is body/document MIR for Markdown and document rendering flows. It is not a universal program MIR. M13f later names the future GPU-oriented MIR target `VD-MIR`, with likely code spellings such as `VdMir`, but it still appears only if shader/kernel lanes actually need that shared shape.

## Backends

A backend consumes a lane-specific AST, HIR, MIR, or contract shape and emits targeted output artifacts.

Possible backend categories in the Visionary ecosystem include:

- C# backend
- CPU-oriented backend
- HLSL backend
- SPIR-V backend
- PTX backend
- interpreter-oriented execution backend

Backends are obligations only when a concrete lane needs them. PTX, SPIR-V, HLSL, C#, and CPU targets are candidate emitters, not universal promises.

## Artifacts

An artifact is any deterministic compiler output or report generated by a lane. Examples include:

- emitted HLSL
- emitted SPIR-V
- generated C#
- text or JSON dumps
- diagnostic reports
- corpus exports
- build or milestone manifests

Artifacts should remain lane-aware and explicit. They are the evidence of a pipeline stage, not proof that all lanes share one representation.

## Diagnostics

Diagnostics are the stable way a lane reports errors, warnings, notes, and milestone facts.

Copeland should converge on shared diagnostic conventions such as:

- deterministic codes
- source-span attachment where applicable
- severity categories
- stable text and structured output

The exact diagnostic vocabulary may differ across lanes, but the conventions for how diagnostics behave should feel consistent.

## Pipeline stages

A pipeline is the ordered set of stages a lane uses from input to outputs.

Pipeline doctrine:

- stages should be explicit
- stages may be skipped when they add no value
- stage boundaries should be testable
- dumps/manifests should describe the real pipeline, not an aspirational one

Copeland should prefer honest, concrete stage contracts over broad universal abstractions.

## Naming and package guidance

M13d does not create packages, but it records the preferred naming direction.

Preferred future shape:

```text
src/Copeland.Core
src/Copeland.Frontends.Markdown
src/Copeland.Frontends.TypeScript
src/Copeland.Frontends.Sdslv
src/Copeland.Frontends.GpuTs
src/Copeland.Frontends.Oct

src/Copeland.Hir.Document
src/Copeland.Hir.Script
src/Copeland.Hir.Shader
src/Copeland.Hir.Kernel
src/Copeland.Hir.Numeric

src/Copeland.Mir.CSharp
src/Copeland.Mir.Vd
src/Copeland.Mir.VdMir
src/Copeland.Mir.Numeric

src/Copeland.Backends.CSharp
src/Copeland.Backends.Hlsl
src/Copeland.Backends.Spirv
src/Copeland.Backends.Ptx
src/Copeland.Backends.Cpu

src/Copeland.Tooling
```

Guidance:

- choose names by lane responsibility, not by imagined universal reuse
- keep frontends, HIRs, MIRs, and backends explicit
- create packages only when implementation milestones need them
- avoid `Copeland.Shaders` as the umbrella architectural name
- avoid names like `UniversalCopelandMir`, `OneTrueIr`, or `GpuEverythingCore`

Acceptable future package names include:

- `Copeland.Frontends.Sdslv`
- `Copeland.Hir.Shader`
- `Copeland.Backends.Hlsl`
- `Copeland.Backends.Ptx`
- `Copeland.Mir.Vd`
- `Copeland.Mir.VdMir`

## Existing lanes

Current lanes and lane directions should be documented narrowly and honestly.

Markdown/document lane:

```text
Copeland.Markdown:
  Markdown source
    -> Markdown lexer/parser
    -> Markdown AST
    -> DocumentMir
    -> Machina/Oblivion rendering/dogfood
```

Important doctrine:

- Markdown is a text-card body language, not the whole Oblivion page model.
- `DocumentMir` is body/document MIR, not universal program MIR.
- the strict Markdown subset is intentional.

Original TypeScript/script lane:

```text
TypeScript-like source
  -> AST
  -> MIR
  -> C# backend
```

This should be described as the original or legacy Copeland direction where implementation is partial or historical, not as a fully current universal compiler stack.

Aurelian SDSL-V lane today:

```text
SDSL-V
  -> Aurelian.Shaders lexer/parser/AST/lowering
  -> HLSL
  -> DXC
  -> SPIR-V
```

Future Copeland-shaped target candidates may include:

```text
Copeland.Frontends.Sdslv
Copeland.Hir.Shader
Copeland.Backends.Hlsl
```

M13d does not perform that move.

## Future lanes

M13d documents possibilities, not commitments.

Possible GPU TypeScript-like lane:

```text
GPU TypeScript-ish source
  -> restricted TS-shaped AST
  -> Kernel/Shader HIR
  -> GPU MIR if earned
  -> HLSL / SPIR-V / PTX / maybe MSL/WGSL
```

Doctrine:

- TypeScript syntax and tooling reuse may be valuable.
- This is not JavaScript runtime semantics.
- There is no goal to inherit prototype/object/dynamic JS behavior.
- GPU execution semantics must stay explicit.
- Tooling reuse is a surface advantage, not a semantic prison.

Possible PTX backend:

```text
Kernel HIR / GPU MIR
  -> PTX backend
```

Doctrine:

- PTX implies GPU compute, not only graphics shaders.
- PTX should not be designed before a concrete lane needs it.
- CUDA C/C++ pain is motivation, not architecture by itself.

Possible Oct-in-C# numeric/scientific lane:

```text
Oct source
  -> Oct AST
  -> Numeric HIR
  -> CPU MIR / interpreter / C# backend
  -> optional GPU kernel lowering later
```

Doctrine:

- a C# reimplementation of Oct could reuse Copeland compiler primitives
- arrays, units, numeric operators, plots, and interpreter flow belong to numeric/scientific lane design
- Oct should not be forced into shader abstractions

UI/layout-related languages may also emerge later, but M13d does not define them as committed lanes.

## Dependency direction

Preferred direction:

```text
Copeland.Core:
  lowest-level compiler primitives

Copeland.Frontends.*:
  may depend on Copeland.Core and domain HIR contracts

Copeland.Hir.*:
  domain semantic models

Copeland.Mir.*:
  execution/target-oriented models

Copeland.Backends.*:
  consume specific HIR/MIR/contracts and emit artifacts

Aurelian:
  may consume shader/render artifacts and contracts,
  may eventually depend on Copeland shader frontend/backend packages,
  should retain renderer semantic ownership

Machina:
  consumes document/UI outputs,
  should not depend on shader/Vulkan backends directly

Dominatus:
  orchestrates effectful compiler/render actions,
  should not own compiler semantics
```

Avoid:

```text
Copeland.Core -> Aurelian.Runtime
Copeland.Core -> Machina.Presenter.Sample
Machina.Core -> Aurelian.Graphics/Vulkan
Aurelian.Core -> Machina.Presenter.Sample
Backends -> random sample projects
```

Relationship notes:

- `Aurelian.Shaders` remains where it is for now.
- the future audit/migration target is not `Copeland.Shaders` as a monolith
- the likely future target is split across frontend/HIR/backend packages if and when the move is earned
- Aurelian still owns renderer-facing semantics such as render contracts, shader program contracts, asset manifests, backend policy, and Vulkan/native realization
- Copeland Markdown currently feeds Oblivion docs dogfood
- Machina should present compiler artifacts and diagnostics, not own compiler semantics
- Oblivion cards may host compiler inputs and outputs later, but executable notebook/compiler cards remain deferred
- Leviathan may eventually consume Copeland, Machina, and Dominatus concepts in web or networked applications, but M13d does not integrate Leviathan

## What changed

M13d adds architecture doctrine only:

- Copeland is now documented as the compiler workshop for Visionary
- compiler-lane terminology is defined
- non-universal-IR doctrine is explicit
- naming/package guidance is recorded
- existing and future lane examples are documented
- dependency direction and promotion rules are documented
- a deterministic M13d manifest records that no migration or implementation happened

## What did not change

M13d does not:

- move `Aurelian.Shaders`
- create `Copeland.Shaders`
- create `Copeland.Frontends.Sdslv`
- create `Copeland.Backends.Ptx`
- create `Copeland.Frontends.GpuTs`
- migrate SDSL-V into Copeland
- wire Machina to Aurelian or Vulkan
- rename the repository
- change existing compiler, renderer, or presenter runtime behavior

## Deferred work

Recommended next sequence:

- `M13d`: Copeland compiler workshop architecture
- `M13e`: Aurelian SDSL-V lane audit and GPU MIR target analysis
- `M13f`: VD-MIR architecture doctrine
- `M13g`: Aurelian.VisibleTriangle sample topology and proof-boundary audit
- `M14a`: VD-MIR M0 implementation for smoke triangle
- `M14b`: visible triangle proof through VD-MIR -> HLSL/DXC -> SPIR-V
- `M14+`: first implementation extraction or bridge proof when the architecture has earned it

Shared abstraction promotion rule for all follow-up work:

```text
A helper stays lane-local until at least two lanes prove the same shape.

A type becomes shared only after:
  - two concrete users exist,
  - dependency direction is clean,
  - diagnostics/source-span behavior matches,
  - tests prove it is not forcing unrelated domains into the same mold.
```
