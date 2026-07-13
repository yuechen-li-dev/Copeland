# JTF compiler SDK graduation doctrine

## Decision

Copeland is a compiler workshop: small, semantics-free libraries that independent compiler lanes may adopt. It is not a universal IR, inheritance framework, or a pipeline controller.

```text
Copeland TS (implemented proof path)
  TypeScript-shaped text -> SyntaxTree -> BoundCompilation -> Cope MIR -> C# proof backend -> C# text

Copeland TS (intended product direction)
  TypeScript 7-shaped source -> Copeland TS frontend -> Cope MIR
      -> JavaScript backend
      -> C#/.NET backend

Aurelian shader
  SDSL-V -> SdslvLexer/Parser/Module -> Validator -> HLSL -> DXC -> SPIR-V
       \-> (M14a proof) VD-MIR M0 -> HLSL -> existing DXC/SPIR-V path

Machina-facing Markdown document lane
  Markdown -> MarkdownSourceText/Lexer/Parser/MarkdownDocument -> DocumentMir
           -> CLI dumps and Oblivion presenter-sample UI lowering
```

The document lane is implemented in `src/Copeland/Copeland.Markdown`; its current Machina consumer is `samples/Integrations/Machina.Presenter.Sample`. This describes destination, not a project-ownership move. `MirProgram`, `VdMirModule`, and `DocumentMir` remain independently owned representations. No lane lowers through another lane's IR.

## Current boundaries

The only current Copeland backend is the C# proof backend. `CopelandCompiler` exposes frontend-to-MIR compilation; `Copeland.Cli --emit mir|csharp` composes the selected output path. The intended JavaScript-first product direction is a pivot, not an implemented path. The backend boundary is `Cope MIR -> lane-owned backend`; no universal backend abstraction exists.

## M6d closeout

JTF-M6d closes this compiler-SDK audit. No universal MIR, shared parser framework, shared source/span abstraction, universal backend interface, or universal pass interface graduated. Cope MIR, `DocumentMir`, and VD-MIR remain subsystem-owned and do not reference one another. Existing mechanics may remain recorded as candidates, but candidate status creates neither a package nor a dependency.

The compiler SDK is a policy and a place for proven reusable equipment, not a mandate to generalize every compiler-shaped subsystem. Future graduation still requires at least two real consumers with compatible semantics and invariants, followed by direct evidence and tests for both consumers.

### Copeland TS direction (not current capability)

Copeland TS is intended to be a TypeScript-shaped, closed-world language that preserves TypeScript’s authoring ergonomics without inheriting JavaScript’s dynamic runtime semantics. TypeScript 7 is the syntax and ecosystem reference point, not a claim of full TypeScript 7 or npm compatibility. Copeland TS intends stricter closed-world semantics; removing JavaScript coercion and dynamic-runtime footguns, null-less semantics, and payload enums for explicit optionality and failure are future language work.

The JavaScript backend is the planned first usable distribution/reference backend. The C# emitter is currently a proof backend and may later mature into the C#/.NET backend. Ordinary managed execution would use RyuJIT; supported native deployments would use NativeAOT; browser/Wasm deployment would use .NET WebAssembly AOT. Browser WebAssembly is not ordinary NativeAOT. No performance superiority is claimed before it is measured and tested.

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
| Source text | M6b measured common UTF-16 offsets, but Markdown alone has an immutable indexed source holder; Script has raw text only and active SDSL-V is LF-only for line advancement. | Rejected in M6b; retain local. See `docs/Copeland/architecture/compiler-source-contract-jtf-m6b.md`. |
| Spans/locations | M6b found Markdown source-bound spans, unconstrained Script position/length values, and unconstrained SDSL-V start/end/unknown spans. | Local; no two-consumer structural/source-bound contract. |
| Line mapping | Markdown cached CR/LF/CRLF map; SDSL-V scanner counters advance only on LF; Script exposes offsets only. | Rejected in M6b; no policy flags. |
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

## Proposed topology (not implemented; M6b rejected)

| Project | Responsibility/dependencies | Prohibited vocabulary | First consumers/API | Excluded |
| --- | --- | --- | --- | --- |
| `Copeland.Compiler.Source` | Rejected in M6b: no two-consumer source/line/span contract exists yet. | All domain/IR/backend vocabulary. | None; no project or API was created. | Tokens, diagnostics, file I/O. |
| `Copeland.Compiler.Diagnostics` (defer) | Neutral record/list after two severity/order contracts. Depends on Source. | Domain/tool terms. | None in M6b. | Rendering and collection policy. |
| `Copeland.Compiler.Artifacts` (defer) | Byte hashing/writing only after compatible failure/encoding contracts. | Stage, DXC, SPIR-V, Markdown, C#. | None in M6b. | Manifests/schemas. |
| `Copeland.Compiler.Testing` (defer) | Test-only normalization helpers after two direct suite contracts. | Production/domain vocabulary. | None in M6b. | Discovery/subprocess orchestration. |

## M6b outcome

M6b ran the source-contract spike against Markdown, Script, and active SDSL-V using test-local adapters. It found shared UTF-16 offsets but not two complete compatible source contracts: Script has no mapping/span contract, and SDSL-V treats bare CR differently from Markdown. `Copeland.Compiler.Source` was not extracted, no production consumer migrated, and no dependency edge was added. Do not migrate diagnostics, parsers, tokens, artifacts, or IRs on this evidence. See `docs/Copeland/architecture/compiler-source-contract-jtf-m6b.md` and `docs/migrations/jtf-m6b-compiler-source-contract.md`.
