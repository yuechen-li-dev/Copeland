# Copeland Docs

Copeland is the compiler workshop for Visionary. It hosts compiler lanes, shared compiler primitives, and architecture doctrine without requiring one universal IR or one top-level shader-only identity.

## Current docs

- [Copeland Compiler Workshop Architecture M13d](history/copeland-compiler-workshop-architecture-m13d.md)
- [Copeland Compiler Lane Taxonomy M13d](history/copeland-compiler-lane-taxonomy-m13d.md)
- [VD-MIR Architecture Doctrine M13f](history/vd-mir-architecture-doctrine-m13f.md)
- [Copeland Roadmap](architecture/copeland-roadmap.md)
- [Copeland Markdown Frontend M12a](history/copeland-markdown-frontend-m12a.md)
- [Copeland TypeScript Support](architecture/copeland-typescript-support.md)
- [Language Profile](architecture/language-profile.md)
- [Windows Test Triage M5i](history/copeland-windows-test-triage-m5i.md)

## Current lane snapshots

- Markdown/document lane: Markdown source -> Markdown AST -> `DocumentMir`
- original script lane: TypeScript-like source -> AST -> MIR -> C# backend
- current Aurelian SDSL-V lane: SDSL-V -> Aurelian shader frontend/lowering -> HLSL -> DXC -> SPIR-V
- future GPU-oriented MIR doctrine: SDSL-V -> VD-MIR -> backend -> tool boundary -> artifacts

## Doctrine

- Copeland is a workshop for multiple explicit compiler lanes.
- Shared primitives are promoted only after repeated concrete use.
- `DocumentMir` is a document/body MIR, not a universal MIR.
- `Copeland.Shaders` is too narrow to name the whole architecture.
- `VD-MIR` is the current name for the future common GPU-oriented MIR candidate.

## Related docs

- [Current JTF-M0 topology and ownership](../architecture/jtf-m0-topology-and-ownership.md)
- [Visionary Monorepo Architecture M13a](../architecture/visionary-monorepo-architecture-m13a.md)
- [Machina.UI documentation](../Machina.UI/README.md)
