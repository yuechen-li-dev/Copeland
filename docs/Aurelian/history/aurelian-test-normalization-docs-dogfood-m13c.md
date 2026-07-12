# Aurelian Test Normalization and Docs Dogfood M13c

## Purpose

M13c finishes two narrow follow-through tasks after the Aurelian import and build-topology cleanup:

- fix the remaining Windows line-ending test failure in `Aurelian.Shaders.Tests`
- add a curated Aurelian doc slice to the existing Copeland Markdown and Oblivion docs dogfood path

This milestone is normalization and docs dogfood only. It does not integrate Aurelian runtime/rendering with Machina, does not move SDSL-V into Copeland, and does not add Vulkan presenter integration.

## Shader test normalization

The remaining Aurelian test failure after M13b was:

- `tests/Aurelian/Aurelian.Shaders.Tests/ShaderArtifactFileWriterM0Tests.cs`
- `ShaderArtifactFileWriter_CanWriteHexEncodedSpirvFiles`

The failure was a Windows CRLF versus LF mismatch at the assertion boundary for `.spv.hex` text output:

- writer output on Windows contained `\r\n`
- the test regex only accepted `\n`

M13c fixes that at the test boundary by normalizing line endings before comparison. The writer semantics are unchanged. No shader compiler behavior, hash behavior, or SPIR-V artifact semantics changed.

## Aurelian test status

After the normalization fix:

- `dotnet restore Aurelian.slnx` passes
- `dotnet build Aurelian.slnx --no-restore` passes
- `dotnet test Aurelian.slnx --no-build` passes

The cross-platform regression coverage now includes a focused line-ending normalization test so the Windows CRLF case stays explicit.

## Aurelian docs dogfood

The existing `Oblivion -> Docs` dogfood page now includes a curated Aurelian slice.

Each selected file:

- is loaded from its real repo-relative source path
- compiles through `Copeland.Markdown`
- preserves per-doc diagnostics
- remains one generated card, not a whole page

The index card now summarizes:

- total docs loaded
- Aurelian docs loaded
- total diagnostics
- Aurelian diagnostics
- unsupported syntax count

It also states clearly that Aurelian docs are dogfood inputs, not runtime or presenter integration behavior.

## Curated docs list

M13c keeps the list deterministic and intentionally small:

- `docs/Aurelian/history/aurelian-monorepo-import-audit-m13a.md`
- `docs/Aurelian/history/aurelian-build-topology-m13b.md`
- `docs/Aurelian/architecture/aurelian-charter.md`
- `docs/Aurelian/architecture/dependency-policy.md`
- `docs/Aurelian/architecture/compositor-policy-mechanism-split.md`
- `docs/Aurelian/architecture/graphics-memory-allocation.md`
- `docs/Aurelian/architecture/mvp-roadmap.md`
- `docs/Aurelian/architecture/world-model-doctrine.md`

These flow alongside the existing Machina/Copeland doc dogfood cards.

## Diagnostics summary

M13c does not special-case Aurelian Markdown semantics.

The selected Aurelian docs compile through the same `Copeland.Markdown` frontend as the earlier docs dogfood set:

- document MIR is produced per doc
- unsupported syntax remains diagnostic-driven rather than fatal
- diagnostics stay attached per generated card
- source paths remain visible in the inspector

## What changed

- normalized shader hex-output test comparison line endings in `Aurelian.Shaders.Tests`
- added a regression test for CRLF/LF normalization behavior
- extended the curated `Oblivion -> Docs` dogfood list with selected Aurelian docs
- added Aurelian-specific tags and Aurelian counts to the docs dogfood index and manifest shape
- added M13c manifest writing for test/docs dogfood closeout reporting

## What did not change

- no shader compiler semantic change beyond test-boundary normalization
- no SDSL-V migration into Copeland
- no `Copeland.Shaders` implementation
- no `Machina.Aurelian` bridge
- no Machina production dependency on Aurelian runtime or Vulkan
- no Vulkan presenter integration
- no repo rename
- no merge of `Aurelian.slnx` into `Copeland.slnx`

## Deferred work

- broader Aurelian docs ingestion beyond the curated slice
- M13e SDSL-V lane audit and GPU MIR target analysis
- M13f VD-MIR architecture doctrine
- `Machina.Aurelian` bridge contracts
- Vulkan/presenter integration after subsystem boundaries are intentionally tightened
