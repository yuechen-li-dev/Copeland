# Copeland Compiler Lane Taxonomy M13d

## Purpose

This document defines the lane taxonomy for Copeland as the compiler workshop for Visionary. It is intentionally narrower than a universal compiler theory. The goal is to describe the kinds of lanes Copeland can host without forcing them into one AST, one HIR, one MIR, or one backend strategy.

## Lane anatomy

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

Stages are optional:

- Markdown may stop at AST plus `DocumentMir`.
- SDSL-V may keep shader-specific semantic forms.
- GPU TypeScript-like work may earn a GPU MIR only after real pressure.
- Oct may begin with interpreter or CPU backend work before any GPU lowering exists.

## Document lane

Document lanes compile text or markup into structured document/body models.

Current example:

```text
Markdown source
  -> Markdown lexer/parser
  -> Markdown AST
  -> DocumentMir
  -> Machina/Oblivion document rendering and dogfood
```

Doctrine:

- document lanes preserve body structure and document diagnostics
- document lanes are not universal program lanes
- `DocumentMir` is document/body MIR, not universal MIR

## Script lane

Script lanes compile program-oriented sources with control flow, values, and backend lowering needs.

Original Copeland direction:

```text
TypeScript-like source
  -> AST
  -> MIR
  -> C# backend
```

Doctrine:

- script lanes may share source, span, and diagnostic conventions with other lanes
- script lanes should not be treated as the only shape Copeland supports
- legacy or partial implementation should be described honestly

## Shader lane

Shader lanes compile stage-oriented GPU shading languages.

Current Aurelian-hosted example:

```text
SDSL-V
  -> Aurelian.Shaders lexer/parser/AST/lowering
  -> HLSL
  -> DXC
  -> SPIR-V
```

Current M13e audit finding:

- the active lane has no explicit MIR today
- one common GPU MIR is the starting assumption for future target analysis
- Shader MIR / Kernel MIR split is deferred until proven necessary

Possible future Copeland target split:

```text
Copeland.Frontends.Sdslv
  -> Copeland.Hir.Shader
  -> Copeland.Backends.Hlsl
  -> DXC / SPIR-V tooling path
```

Doctrine:

- shader lanes are one category inside the workshop
- shader lanes do not define the whole architecture
- renderer-facing ownership stays with Aurelian where appropriate

## GPU kernel lane

GPU kernel lanes target compute-oriented GPU execution rather than only graphics shaders.

Possible example:

```text
GPU TypeScript-ish source
  -> restricted TS-shaped AST
  -> Kernel HIR
  -> GPU MIR if earned
  -> PTX / SPIR-V / HLSL / later targets
```

Doctrine:

- GPU tooling reuse from TypeScript is allowed
- JavaScript runtime semantics are not the goal
- execution model, bindings, memory spaces, and launch constraints must stay explicit
- PTX should appear only when a real frontend or kernel lane needs it

## Numeric/scientific lane

Numeric/scientific lanes target array-heavy, interpreter-friendly, or analysis-oriented languages.

Possible Oct-in-C# example:

```text
Oct source
  -> Oct AST
  -> Numeric HIR
  -> CPU MIR / interpreter / C# backend
  -> optional GPU kernel lowering later
```

Doctrine:

- arrays, units, numerical operators, plots, and scientific workflows are lane-local concerns
- numeric lanes should not be forced through shader abstractions
- CPU-first is a valid starting point

## UI/layout lane candidates

M13d does not commit to a UI/layout compiler lane, but it leaves room for future DSLs that compile into UI, layout, or document presentation contracts.

Possible future shapes could include:

- structured layout DSLs
- notebook cell description dialects
- presenter/workbench artifact authoring languages

Doctrine:

- these are only candidates
- they should be defined by concrete need
- they should not be invented just to fill a taxonomy grid

## Backend taxonomy

Backends are targeted emitters, not universal obligations.

Representative backend categories:

- `CSharp`
- `Cpu`
- `Hlsl`
- `Spirv`
- `Ptx`
- interpreter-oriented execution backends

Doctrine:

- a backend exists because a lane needs it
- one backend does not imply all lanes must use it
- PTX, SPIR-V, HLSL, C#, and CPU targets are options, not mandates

## Shared primitives

Copeland workshop primitives may become shared when repeated use proves the same shape:

- source text
- source spans
- token conventions where appropriate
- diagnostic ids and severities
- parser and lowering result conventions
- artifact manifest/report conventions
- dump and debug-output conventions
- corpus-test conventions

These are workshop utilities, not a universal semantic model.

## Promotion rule for shared abstractions

Required rule:

```text
A helper stays lane-local until at least two lanes prove the same shape.

A type becomes shared only after:
  - two concrete users exist,
  - dependency direction is clean,
  - diagnostics/source-span behavior matches,
  - tests prove it is not forcing unrelated domains into the same mold.
```

This rule exists to stop premature universalization.

## Example pipeline sketches

Markdown/document:

```text
Markdown source
  -> frontend
  -> Markdown AST
  -> DocumentMir
  -> document artifacts and diagnostics
```

Original TypeScript/script:

```text
TypeScript-like source
  -> frontend
  -> AST
  -> MIR
  -> C# backend
  -> generated C# and test/runtime proof artifacts
```

Current SDSL-V:

```text
SDSL-V
  -> frontend
  -> shader AST
  -> lane-local lowering
  -> HLSL backend
  -> DXC
  -> SPIR-V artifacts
```

Possible GPU TypeScript-like lane:

```text
GPU TypeScript-ish source
  -> frontend
  -> restricted TS-shaped AST
  -> Kernel/Shader HIR
  -> GPU MIR if earned
  -> HLSL / SPIR-V / PTX
```

Possible Oct lane:

```text
Oct source
  -> frontend
  -> Oct AST
  -> Numeric HIR
  -> CPU MIR / interpreter / C# backend
```

## Non-goals

- no universal IR mandate
- no requirement that every lane share one AST/HIR/MIR
- no `Copeland.Shaders` monolith as the top-level architectural concept
- no M13d package creation or migration
- no Aurelian shader move yet
- no Machina/Aurelian/Vulkan wiring
