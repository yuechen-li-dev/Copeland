# Copeland Docs

Copeland is the compiler workshop for Visionary. It hosts compiler lanes, shared compiler primitives, and architecture doctrine without requiring one universal IR or one top-level shader-only identity.

## Current docs

- [Copeland TS Language Profile](language/copeland-ts-language-profile.md)
- [CTS-TSON-M0a Native Typed Data Design](language/copeland-ts-tson-design-cts-tson-m0a.md)
- [CTS-TSON-M0a Repository Audit](../migrations/cts-tson-m0a-native-typed-data-audit.md)
- [CTS-M6b Typed Result `try`/`except` implementation](architecture/copeland-ts-typed-try-except-cts-m6b.md)
- [CTS-M0a Copeland TS Language Doctrine Audit](../migrations/cts-m0a-copeland-ts-language-doctrine-audit.md)
- [Copeland Compiler Workshop Architecture M13d](history/copeland-compiler-workshop-architecture-m13d.md)
- [Copeland Compiler Lane Taxonomy M13d](history/copeland-compiler-lane-taxonomy-m13d.md)
- [VD-MIR Architecture Doctrine M13f](history/vd-mir-architecture-doctrine-m13f.md)
- [Copeland Roadmap](architecture/copeland-roadmap.md)
- [Copeland Markdown Frontend M12a](history/copeland-markdown-frontend-m12a.md)
- [Copeland TypeScript Support](architecture/copeland-typescript-support.md)
- [Copeland TS Compiler Topology JTF-M6c](architecture/copeland-ts-compiler-topology-jtf-m6c.md)
- [Copeland TS JavaScript Backend CTS-M1](architecture/copeland-ts-javascript-backend-cts-m1.md)
- [CTS-M1 Minimal JavaScript Backend](../migrations/cts-m1-minimal-javascript-backend.md)
- [Copeland TS Mise en Place JTF-M6c](../migrations/jtf-m6c-copeland-ts-mise-en-place.md)
- [Compiler SDK and JTF Closeout JTF-M6d](../migrations/jtf-m6d-compiler-sdk-and-jtf-closeout.md)
- [Historical M1 Language Profile](architecture/language-profile.md)
- [Windows Test Triage M5i](history/copeland-windows-test-triage-m5i.md)

## Current lane snapshots

- Markdown/document lane: Markdown source -> Markdown AST -> `DocumentMir`
- Copeland TS lane: TypeScript-shaped source -> AST -> Cope MIR -> C# proof backend or CTS-M1 JavaScript backend
- current Aurelian SDSL-V lane: SDSL-V -> Aurelian shader frontend/lowering -> HLSL -> DXC -> SPIR-V
- future GPU-oriented MIR doctrine: SDSL-V -> VD-MIR -> backend -> tool boundary -> artifacts

## Doctrine

- Copeland is a workshop for multiple explicit compiler lanes.
- Shared primitives are promoted only after repeated concrete use.
- `DocumentMir` is a document/body MIR, not a universal MIR.
- `Copeland.Shaders` is too narrow to name the whole architecture.
- `VD-MIR` is the current name for the future common GPU-oriented MIR candidate.
- Cope MIR is owned only by the Copeland TS lane; it is not a universal MIR.

## Related docs

- [Current JTF-M0 topology and ownership](../architecture/jtf-m0-topology-and-ownership.md)
- [Compiler SDK graduation doctrine](../architecture/jtf-compiler-sdk-graduation-doctrine.md)
- [Visionary Monorepo Architecture M13a](../architecture/visionary-monorepo-architecture-m13a.md)
- [Machina.UI documentation](../Machina.UI/README.md)
