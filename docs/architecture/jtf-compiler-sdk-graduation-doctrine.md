# JTF compiler SDK graduation doctrine

## Decision

Copeland is a compiler workshop: small, semantics-free libraries that independent compiler lanes may adopt. It is not a universal IR, inheritance framework, or a pipeline controller.

```text
Copeland TS (implemented proof path)
  TypeScript-shaped text -> SyntaxTree -> BoundCompilation -> MirProgram -> CSharpBackend -> C# text

Copeland TS (intended product direction)
  TypeScript -> Cope MIR -> JavaScript first -> NativeAOT-compatible C# later

Aurelian shader
  SDSL-V -> SdslvLexer/Parser/Module -> Validator -> HLSL -> DXC -> SPIR-V
       \-> (M14a proof) VD-MIR M0 -> HLSL -> existing DXC/SPIR-V path

Machina-facing Markdown document lane
  Markdown -> MarkdownSourceText/Lexer/Parser/MarkdownDocument -> DocumentMir
           -> CLI dumps and Oblivion presenter-sample UI lowering
```

The document lane is implemented in `src/Copeland/Copeland.Markdown`; its current Machina consumer is `samples/Integrations/Machina.Presenter.Sample`. This describes destination, not a project-ownership move. `MirProgram`, `VdMirModule`, and `DocumentMir` remain independently owned representations. No lane lowers through another lane's IR.

## Current boundaries

The only current Copeland backend is `CSharpBackend`, exposed by `CopelandCompiler` and `Copeland.Cli --emit mir|csharp`. The intended JavaScript-first product direction is a pivot, not an implemented path. A future multi-backend boundary is `Cope MIR -> lane-owned backend`; M6a implements neither backend.

The future GPU TypeScript-shaped path remains direct:

```text
.g.ts -> shared TypeScript syntax only when proven -> Aurelian shader semantics -> VD-MIR
```

A future shared TypeScript syntax package may own lexing, parsing, syntax nodes, trivia/comments, spans, and syntax diagnostics. It may not own Copeland runtime semantics, shader semantics, VD-MIR lowering, or backend selection.

## Graduation model

**Local** is the default. A mechanism remains with its lane whenever semantics, recovery, lifetime, or performance assumptions are lane-specific.

**Candidate** requires two real lanes with the same invariants. Its record compares semantics, failure behavior, deterministic ordering, provenance, mutability/lifetime, performance, public API, differences, and migration cost. Names and similarly shaped records are not evidence.

**Graduated** infrastructure requires two current/immediate consumers, concrete implementation/test evidence, no TypeScript/Markdown/shader/GPU/Vulkan/UI/backend vocabulary, lower total complexity, consumer-owned policy, direct tests for both consumers, no Copeland dependency on Aurelian/Machina, no IR translation, and a contract that survives either consumer disappearing. Proposals get a short evidence record and paired contract tests; review approves, adopts one consumer at a time, or rejects. Rejection leaves code local without technical-debt stigma.

## Candidate matrix

| Candidate | Evidence and material difference | Status, owner, milestone |
| --- | --- | --- |
| Source identity | Script accepts strings; Markdown has no identity; SDSL-V has display name and legacy assets have paths. | Local; no common identity contract. |
| Source text | `SyntaxTree.Text`, `MarkdownSourceText`, SDSL-V strings. Markdown alone has a reusable indexed map. | Candidate, `Copeland.Compiler.Source`, M6b spike. |
| Spans/locations | Script offset/length; Markdown start/end locations; SDSL-V start/end/line/column. | Local until range/unknown semantics align. |
| Line mapping | Markdown cached binary-search map; shader scanners update counters; script exposes offsets only. | Candidate, M6b only after compatible tests. |
| Provenance | Markdown nodes/MIR and VD-MIR retain spans; Cope MIR drops source provenance. | Local. |
| Diagnostic record | Script `Diagnostic`, Markdown diagnostic records, SDSL-V diagnostics/tool diagnostics. | Candidate only after compatible neutral fields are proven. |
| Severity/codes | Script observable error-only behavior; Markdown warning/error; SDSL-V severity/phase. | Local. |
| Diagnostic ordering | All append in traversal; no common sort comparator, and tool order is external. | Candidate only after two consumers select one comparator. |
| Diagnostic phase | SDSL-V explicit phase; script compilation stage; Markdown none. | Local. |
| Lexer cursor | Script stateful peek; SDSL-V scanners; Markdown line scanner. | Local. |
| Parser cursor/lookahead | Script and SDSL-V own indexed token cursors; Markdown uses blocks plus independent inline scanning. | Candidate for a private indexed-token cursor after comparison; M6c earliest. |
| Token containers | Script tokens, SDSL-V tokens, Markdown line+flattened token source. | Local. |
| Parse/validation result | SyntaxTree, MarkdownCompilation, SDSL-V parse/validation results retain different partial state. | Local. |
| Phase reporting | Script stage enum, SDSL-V phase, Markdown CLI dump choice describe distinct hosts. | Local; reject coordinator. |
| Artifact hashing | SDSL-V SHA-256 and legacy newline-normalized hashing; other lanes do not share contract. | Candidate for byte helper only when two callers agree. |
| Artifact manifests | Shader manifests encode stages/profiles/tool state; Markdown corpus and script golden files do not. | Local. |
| Deterministic writing | Shader file writer, CLI text writer, Markdown exporter have different overwrite/encoding/path policies. | Candidate only after contract convergence. |
| Corpus/golden utilities | Script expected corpus files; Markdown selected-doc dogfood; shaders direct fixtures/assertions. | Local. |
| Fixture discovery | Each lane's explicit curated discovery is meaningful policy. | Local. |
| Snapshot normalization | Script normalizes corpus text; Markdown has dump writers; shader assertions are targeted. | Candidate test helper only if two suites choose identical rules. |
| Subprocess/tool harness | Script test-only Roslyn runtime proof; shader DXC policy; Markdown none. | Local; reject universal tool harness. |

## Rejected abstractions

M6a rejects a universal MIR, instruction/value/type hierarchy, syntax-node base, symbol table, module resolver, compiler pass/backend interface, metadata bags, master pipeline coordinator, universal token stream, and abstractions for imaginary users. VD-MIR and DocumentMir must not route through Cope MIR.

## Proposed topology (not implemented)

| Project | Responsibility/dependencies | Prohibited vocabulary | First consumers/API | Excluded |
| --- | --- | --- | --- | --- |
| `Copeland.Compiler.Source` | BCL-only source text, half-open spans, line mapping. | All domain/IR/backend vocabulary. | Script and SDSL-V; `SourceText`, `SourceLocation`, `SourceSpan`. | Tokens, diagnostics, file I/O. |
| `Copeland.Compiler.Diagnostics` (defer) | Neutral record/list after two severity/order contracts. Depends on Source. | Domain/tool terms. | None in M6b. | Rendering and collection policy. |
| `Copeland.Compiler.Artifacts` (defer) | Byte hashing/writing only after compatible failure/encoding contracts. | Stage, DXC, SPIR-V, Markdown, C#. | None in M6b. | Manifests/schemas. |
| `Copeland.Compiler.Testing` (defer) | Test-only normalization helpers after two direct suite contracts. | Production/domain vocabulary. | None in M6b. | Discovery/subprocess orchestration. |

## Recommended M6b

Run a source-contract spike first: compare `MarkdownSourceText` CR/LF behavior against non-public script and SDSL-V adapters; specify half-open offsets, one-based location basis, empty spans, EOF, and unknown-source behavior; add paired consumer tests; extract only `Copeland.Compiler.Source` if the adapters are identical. Do not migrate diagnostics, parsers, tokens, artifacts, or IRs in that milestone.
