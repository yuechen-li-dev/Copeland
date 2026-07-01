# Copeland Docs

Copeland is the compiler workshop for Visionary. It hosts compiler lanes, shared compiler primitives, and architecture doctrine without requiring one universal IR or one top-level shader-only identity.

## Current docs

- [Copeland Compiler Workshop Architecture M13d](copeland-compiler-workshop-architecture-m13d.md)
- [Copeland Compiler Lane Taxonomy M13d](copeland-compiler-lane-taxonomy-m13d.md)
- [Copeland Roadmap](copeland-roadmap.md)
- [Copeland Markdown Frontend M12a](copeland-markdown-frontend-m12a.md)
- [Copeland TypeScript Support](copeland-typescript-support.md)
- [Language Profile](language-profile.md)
- [Windows Test Triage M5i](copeland-windows-test-triage-m5i.md)

## Current lane snapshots

- Markdown/document lane: Markdown source -> Markdown AST -> `DocumentMir`
- original script lane: TypeScript-like source -> AST -> MIR -> C# backend
- current Aurelian SDSL-V lane: SDSL-V -> Aurelian shader frontend/lowering -> HLSL -> DXC -> SPIR-V

## Doctrine

- Copeland is a workshop for multiple explicit compiler lanes.
- Shared primitives are promoted only after repeated concrete use.
- `DocumentMir` is a document/body MIR, not a universal MIR.
- `Copeland.Shaders` is too narrow to name the whole architecture.

## Related docs

- [Visionary Monorepo Architecture M13a](../visionary-monorepo-architecture-m13a.md)
- [Machina Support Roadmap](../Machina/machina-support-roadmap.md)
