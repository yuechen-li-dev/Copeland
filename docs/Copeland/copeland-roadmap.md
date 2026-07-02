# Copeland Roadmap

## Purpose

This roadmap tracks the current Copeland direction as the compiler workshop for Visionary. It is intentionally architecture-first through M13g, adds the tiny compiler-only `VD-MIR M0` smoke-triangle slice in M14a, and then uses M14b to prove the first visible triangle through the Presenter/Silk.NET runtime path without implying immediate package extraction or migration work.

## Current state

- `Copeland.Markdown` is the active document lane in the repo.
- the original script/compiler direction remains an important legacy lane: TypeScript-like source -> AST/MIR -> C# backend
- Aurelian currently hosts the active SDSL-V shader lane outside Copeland
- `VD-MIR M0` now exists only as a minimal implementation slice inside `src/Aurelian.Shaders/Language/VdMir`

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

M14a:
  VD-MIR M0 implementation for smoke triangle

M14b:
  Presenter/Silk.NET golden triangle path with visible runtime proof

M14+:
  later implementation extraction, bridge proof, or backend expansion
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

M13e and M13f add the current GPU-lane doctrine:

- replace the temporary phrase `GPU MIR` with `VD-MIR` / `Visual Direct MIR`
- start from one common `VD-MIR` assumption
- do not split Shader MIR / Kernel MIR until proven necessary
- treat HLSL/DXC, Slang, and PTX as backends from `VD-MIR`, not semantic centers
- do not implement `Copeland.Mir.Vd`, `Copeland.Mir.VdMir`, `Copeland.Backends.Slang`, or `Copeland.Backends.Ptx` during doctrine work

M13g adds the proof-target boundary audit:

- treat `samples/Aurelian.VisibleTriangle` as the next concrete visible proof target
- document the current `assets.toml` -> shader artifact -> `CompiledShaderProgram` -> Vulkan pipeline -> present path before any MIR insertion
- keep the sample Aurelian-owned and separate from `Copeland.slnx` / `Copeland.Slow.slnx`
- do not wire the sample to `VD-MIR` until `M14a`/`M14b`
- after M14a and M14b, keep the default direct AST-to-HLSL path and keep `VD-MIR` opt-in rather than the sample default compiler path
