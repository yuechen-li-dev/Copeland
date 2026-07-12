# Copeland Roadmap

## Purpose

This roadmap tracks the current Copeland direction as the compiler workshop for Visionary. M13d through M14e establish the architecture, topology, proof boundaries, and handoff language around Copeland, `VD-MIR`, and Aurelian without requiring immediate package extraction or migration work.

## Current state

- `Copeland.Markdown` is the active document lane in the repo.
- the original script/compiler direction remains an important legacy lane: TypeScript-like source -> AST/MIR -> C# backend
- Aurelian currently hosts the active SDSL-V shader lane outside Copeland
- `VD-MIR` implementation as an active Copeland lane remains deferred
- a historical exploratory `src/Aurelian/Aurelian.Shaders/Language/VdMir` slice remains in-tree, but the visible-triangle golden path does not depend on it

## Doctrine

- Copeland should host explicit compiler lanes rather than one universal IR cathedral.
- shared abstractions should be promoted only after two or more lanes prove the same shape.
- frontends, HIRs, MIRs, and backends should be split by responsibility when implementation pressure earns them.

## Recommended sequence

```text
M13d:
  Copeland compiler workshop architecture

M13e:
  Aurelian SDSL-V lane audit and GPU MIR target analysis

M13f:
  VD-MIR architecture doctrine

M13g:
  Aurelian.VisibleTriangle sample topology and proof-boundary audit

M14d:
  visible triangle routed through PresenterScreenStack as semantic world screen

M14e:
  Aurelian migration closeout and subsystem handoff

M14+:
  future reviewer-owned VD-MIR implementation, extraction, bridge proof, or backend expansion
```

## Deferred implementation targets

Possible future packages:

- `Copeland.Core`
- `Copeland.Frontends.Markdown`
- `Copeland.Frontends.TypeScript`
- `Copeland.Frontends.Sdslv`
- `Copeland.Frontends.GpuTs`
- `Copeland.Frontends.Oct`
- `Copeland.Hir.Document`
- `Copeland.Hir.Script`
- `Copeland.Hir.Shader`
- `Copeland.Hir.Kernel`
- `Copeland.Hir.Numeric`
- `Copeland.Mir.CSharp`
- `Copeland.Mir.Vd`
- `Copeland.Mir.VdMir`
- `Copeland.Mir.Numeric`
- `Copeland.Backends.CSharp`
- `Copeland.Backends.Hlsl`
- `Copeland.Backends.Spirv`
- `Copeland.Backends.Ptx`
- `Copeland.Backends.Cpu`

These are target taxonomy names, not M13d implementation work.

M13d through M14e establish the current GPU-lane doctrine and handoff:

- replace the temporary phrase `GPU MIR` with `VD-MIR` / `Visual Direct MIR`
- start from one common `VD-MIR` assumption
- do not split Shader MIR / Kernel MIR until proven necessary
- treat HLSL/DXC, Slang, and PTX as backends from `VD-MIR`, not semantic centers
- do not implement `Copeland.Mir.Vd`, `Copeland.Mir.VdMir`, `Copeland.Backends.Slang`, or `Copeland.Backends.Ptx` during doctrine work
- keep the active visible-triangle route on the existing checked-in artifact/runtime path
- treat future `VD-MIR` continuation as a separate reviewer lane

M13g through M14e add the proof-target boundary and closeout:

- treat `samples/Aurelian/Aurelian.VisibleTriangle` as the next concrete visible proof target
- document the current `assets.toml` -> shader artifact -> `CompiledShaderProgram` -> Vulkan pipeline -> present path before any MIR insertion
- keep the sample Aurelian-owned and separate from `Copeland.slnx` / `Machina.UI.Slow.slnx`
- route the sample through `PresenterScreenStack` on the semantic `world` layer in M14d
- close the current migration arc in M14e without changing the sample to a `VD-MIR` path

## M14e closeout note

M14e records that:

- M13d-M14e established the Copeland/`VD-MIR`/Aurelian architecture and handoff boundaries
- active `VD-MIR` implementation remains deferred as a future reviewer lane
- future Copeland / `VD-MIR` work can resume from the `M14a` / `M14b` plan space when explicitly reactivated
