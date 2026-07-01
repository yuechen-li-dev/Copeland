# Copeland Roadmap

## Purpose

This roadmap tracks the current Copeland direction as the compiler workshop for Visionary. It is intentionally architecture-first in M13d and does not imply immediate package extraction or migration work.

## Current state

- `Copeland.Markdown` is the active document lane in the repo.
- the original script/compiler direction remains an important legacy lane: TypeScript-like source -> AST/MIR -> C# backend
- Aurelian currently hosts the active SDSL-V shader lane outside Copeland

## Doctrine

- Copeland should host explicit compiler lanes rather than one universal IR cathedral.
- shared abstractions should be promoted only after two or more lanes prove the same shape.
- frontends, HIRs, MIRs, and backends should be split by responsibility when implementation pressure earns them.

## Recommended sequence

```text
M13d:
  Copeland compiler workshop architecture

M13e:
  Aurelian SDSL-V lane audit against Copeland workshop doctrine

M13f:
  Copeland shader/kernel lane target architecture

M13g:
  Aurelian render model/null renderer boundary proof planning

M14+:
  first implementation extraction or bridge proof
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
- `Copeland.Mir.Gpu`
- `Copeland.Mir.Numeric`
- `Copeland.Backends.CSharp`
- `Copeland.Backends.Hlsl`
- `Copeland.Backends.Spirv`
- `Copeland.Backends.Ptx`
- `Copeland.Backends.Cpu`

These are target taxonomy names, not M13d implementation work.
