# JTF-M6a — three compiler infrastructure audit

## Result

M6a is documentation-only reconnaissance. It maps the three lanes and their incomplete/legacy paths, establishes graduation evidence, and recommends a bounded source-contract M6b. No production source, public API, project/package graph, solution membership, IR ownership, or compiler behavior changed.

## Project and dependency topology

Historical pre-M6c state: `Copeland.slnx` contained the proof-era TS project, `Copeland.Markdown`, `Copeland.Cli`, and their tests. M6c renamed and split that TS project; see `jtf-m6c-copeland-ts-mise-en-place.md`.

## Inspected evidence

### Copeland TS / Cope MIR

Inspected the then-combined TS compiler, CLI, stage tests, root-level corpus, and the then-proposed test-dialect note.

The current accepted source is a restricted TypeScript-shaped string language: typed functions, explicit variables, primitives/arrays, control flow, calls, fallibility/propagation, enums, and matches. It is not broad TypeScript or JavaScript runtime semantics. The parser does not select a language by extension; the CLI reads any source file as text.

`MirProgram` is canonical in-memory Cope MIR. `MirTextWriter` owns deterministic printing. There is no textual MIR parser, verifier, parse/print or parse/verify round trip, nor CLI path consuming `.cope`. Binding owns profile/source validation; lowering carries bound diagnostics. The current production/proof pipeline is TypeScript-shaped source -> AST -> bound model -> MIR -> C# text. The intended product pivot is TypeScript -> Cope MIR -> JavaScript first -> NativeAOT-compatible C# later; JavaScript and NativeAOT are not implemented.

This historical audit found three former `.cope` meanings. M6c resolved that ambiguity: `.cope` means only Cope MIR text; TSPack owns executable `*.xtest.tsx` declarations. It is not a current interchange parser format.

The `.ts` inputs plus `.tokens.txt`, `.diagnostics.txt`, `.tree.txt`, `.cope`, and `.g.cs` files are canonical stage corpus artifacts. `m0-*` names are historical, not proof of debris. `Runtime/M0hRuntimeTests` is the unique generated-code execution proof; CLI subprocess tests uniquely prove host/file/exit behavior. No test or fixture cleanup was safe: no audited expensive proof was assertion-free or demonstrably duplicated by a focused contract.

### Aurelian shaders / VD-MIR

Inspected `src/Aurelian/Aurelian.Shaders/Language/{Lexing,Tokens,Parsing,Ast,Diagnostics,Validation,Emission/Hlsl,External/Dxc,Artifacts,VdMir}/*`, top-level shader `Lexing/Parsing/Ast/Lowering/Artifacts/*`, `src/Aurelian/Aurelian.Assets/AssetPipeline.cs`, shader tests/fixtures, and M13e/M13f/M14a records.

The active tested SDSL-V route is SDSL-V -> `SdslvLexer`/`SdslvParser`/`SdslvModule` -> `SdslvValidator` -> direct HLSL or M14a `VdMirM0Lowerer` -> HLSL -> DXC -> SPIR-V artifact -> compiled shader export/file set. Stages, resources/bindings, built-ins, shader validation, VD-MIR, HLSL, DXC, SPIR-V, manifests, compiled shader contracts, and Vulkan mapping are Aurelian-owned. DXC availability is an explicit external-tool boundary.

VD-MIR M0 is real but intentionally smoke-triangle-sized: vertex/pixel entry points, limited types/statements/expressions, semantics, spans, and diagnostics. It excludes general SSA/CFG, resource/binding model, compute, address spaces, barriers, Slang/PTX, and runtime integration. The direct route remains live.

`Aurelian.Assets.AssetPipelineRunner` still invokes the top-level regex-oriented `ShaderParser`/`ShaderArtifactEmitter`, writing legacy HLSL/DXC/SPIR-V manifests. It is an actual production asset host, not removable because `Language.*` has stronger tests. Its spans, diagnostics, stage policy, and hashing differ; convergence is deferred.

### Machina Markdown / DocumentMir

Inspected `src/Copeland/Copeland.Markdown/*`, `tests/Copeland/Copeland.Markdown.Tests/MarkdownPipelineTests.cs`, CLI Markdown commands, `samples/Integrations/Machina.Presenter.Sample/{OblivionMarkdownBody,OblivionMarkdownRenderer,OblivionWorkspaceLoader,OblivionDocsDogfoodCatalog}.cs`, and M12a history.

`MarkdownSourceText` caches line starts and supports CR/LF/CRLF one-based locations. The lexer produces line-aware lightweight tokens; the block parser is line-oriented and the inline parser has independent delimiter/lookahead behavior. Diagnostics are deterministic span-bearing warning/error values with recovery. This is evidence against forcing Markdown through a programming-language parser framework.

`DocumentMir` is immutable blocks/inlines plus diagnostics. `MarkdownDumpWriter` and `MarkdownCorpusExporter` provide deterministic text/JSON/corpus artifacts. Current Machina lowering is presenter-sample code, not a production Machina.UI compiler project. Tests cover focused parse/lowering/recovery/determinism plus selected repository-document dogfood.

## Changed files

- `docs/architecture/jtf-compiler-sdk-graduation-doctrine.md`
- `docs/migrations/jtf-m6a-three-compiler-infrastructure-audit.md`
- `docs/architecture/jtf-target-semantic-boundaries.md`

## Deferred work

- M6b source-contract spike and extraction only if paired consumer contracts agree.
- Cope text parsing/verification only if a real MIR consumer requires it.
- Aurelian legacy/new shader-host convergence.
- `.g.ts`, JavaScript backend, NativeAOT-compatible C# backend, and all language features.

## Validation

For this documentation-only milestone: run documentation/path checks, dependency-boundary validation, `git diff --check`, and verify no production/project/solution files changed. Compiler tests are not required because no source or fixtures changed.
