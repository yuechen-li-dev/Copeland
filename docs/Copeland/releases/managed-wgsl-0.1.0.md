# Managed Visual TypeScript / WGSL compiler 0.1.0

The managed graphics compiler turns admitted Visual TypeScript programs into
WGSL with entry points, binding layout, diagnostics, source mappings and semantic
identity. It requires .NET 10 and does not load the Aurelian renderer, a GPU driver
or an OS-native shader compiler.

Install `Copeland.TS.Backend.Wgsl` version `0.1.0`. Its required managed packages
are Copeland.TS, Copeland.TS.Mir, Copeland.TS.Workspace, Copeland.Markdown,
Copeland.Profile and Copeland.SpanAllocation, also at `0.1.0`.

Use `WgslGraphicsBackend.Compile(GpuCompilationRequest)` and check `Success`
before consuming `Program.Code`. Keep diagnostics on failure. A changed program
has a changed semantic identity; renderers must include shader identity and binding
layout in their pipeline cache keys. The complete independently restored example is
under `tests/fixtures/managed-wgsl-consumer/`.

This release packages the Aetheris-qualified compiler revision
`70b6cbe742ecfb2947c9c6a31cfa8e1d3a89067b`. It does not advertise subsequent
Copeland source changes or all Aurelian compute/runtime capabilities. The package
family retains the repository's GPL-3.0-only license.

`tools/Build-ManagedWgslRelease.ps1` tests the compiler, packs its transitive
project graph, verifies package closure and runs a consumer with an isolated
NuGet cache. The existing publication workflow additionally executes the exact
Windows-built package bytes on Linux before publishing them. Qualification and
SHA-256 receipts accompany the workflow artifact.
