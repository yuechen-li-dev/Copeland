# 0002 — A2 SDSL-V convergence audit

## 1. Files changed

- Created this docs-only audit: `docs/audits/0002-a2-sdslv-convergence-audit.md`.
- No production source, solution, project references, packages, tests, vendor code, or `CodeReferences` files were modified.

## 2. Task scope

A2 is an audit/design milestone. Its purpose is to determine what Aurelian must do to convert the carried-over Stri-V shader pipeline into a proper Aurelian SDSL-V implementation.

The audit treats:

- `src/StriV.ShaderPipeline/` as carried-over implementation seed code, not final Aurelian semantics.
- `CodeReferences/WyrmCoil/Engine/shader/sdslv/` as the semantic reference for SDSL-V.
- `CodeReferences/oct/` as a language-design reference for Oct-like compiler/interpreter organization.
- `CodeReferences/Stride/` and raw Stride SDSL as corpus/reference material only.

Explicit non-goals for A2:

- Do not rename projects.
- Do not modify `.slnx` or project references.
- Do not compile or port carried-over Stri-V projects.
- Do not modify production source.
- Do not modify `CodeReferences`.
- Do not add packages.

## 3. Executive recommendation

Aurelian should rename/convert `src/StriV.ShaderPipeline` soon, but **not by treating the current Stri-V pipeline as the language authority**. The next implementation milestone should be a namespace/project conversion into an `Aurelian.Shaders` module with the same limited behavior kept green, followed immediately by AST/parser convergence toward WyrmCoil SDSL-V.

The central decision is:

> **WyrmCoil Rust SDSL-V defines Aurelian SDSL-V semantics. Stri-V C# is an implementation seed and migration scaffold only.**

Aurelian SDSL-V M0 should avoid Stride mixins and old `.sdsl` compatibility. It should implement a small, explicit SDSL-V subset: tokenization, namespace/use parsing, records, streams, enums, shader declarations, stage methods, typed parameters/returns, simple statements, basic expressions, diagnostics, and minimal HLSL emission for one triangle/sprite/core fixture.

The first implementation milestone after this audit should therefore be:

1. Rename `StriV.ShaderPipeline` to `Aurelian.Shaders` with namespaces and project metadata updated.
2. Link it deliberately only when the solution is ready for shader work.
3. Keep semantic behavior unchanged during the rename.
4. Then replace the Stri-V-shaped AST/parser with WyrmCoil-shaped SDSL-V constructs.

## 4. Current Stri-V shader pipeline inventory

### Repository and solution shape

Current Aurelian production projects discovered under `src/`:

| Project | Role in current repository |
| --- | --- |
| `src/Aurelian.Core/Aurelian.Core.csproj` | Core Aurelian project. |
| `src/Aurelian.Actuation/Aurelian.Actuation.csproj` | Actuation-facing runtime module. |
| `src/Aurelian.Rendering.Contracts/Aurelian.Rendering.Contracts.csproj` | Rendering contracts, no shader compiler implementation yet. |
| `src/Aurelian.Runtime/Aurelian.Runtime.csproj` | Runtime smoke harness; already references vendored Dominatus from A1. |
| `src/Aurelian.World/Aurelian.World.csproj` | World/runtime support module. |

Carried-over Stri-V projects discovered under `src/`:

| Project | Status |
| --- | --- |
| `src/StriV.ShaderPipeline/StriV.ShaderPipeline.csproj` | Carried-over shader pipeline; not final Aurelian SDSL-V. |
| `src/StriV.AssetPipeline/StriV.AssetPipeline.csproj` | Carried-over asset pipeline. |
| `src/StriV.AssetTool/StriV.AssetTool.csproj` | Carried-over tool project. |

Reference/vendor locations:

| Location | Status |
| --- | --- |
| `vendor/Dominatus/src/Dominatus.Core/` | Actual vendored runtime dependency. |
| `vendor/Dominatus/src/Dominatus.OptFlow/` | Actual vendored runtime dependency. |
| `CodeReferences/WyrmCoil/Engine/shader/sdslv/` | SDSL-V semantic reference. |
| `CodeReferences/oct/` | GoOct language-design reference. |
| `CodeReferences/Stride/` | Stride/Stri-V reference-only source corpus. |
| `CodeReferences/Machina/` | Reference-only Machina/Copeland code. |

`Aurelian.slnx` currently includes only Aurelian production/test projects plus `vendor/Dominatus` projects. It does **not** link `src/StriV.ShaderPipeline`, `src/StriV.AssetPipeline`, or `src/StriV.AssetTool`.

### Stri-V shader project inventory

`src/StriV.ShaderPipeline` contains 15 C# files:

| Area | Files | Audit notes |
| --- | --- | --- |
| Project metadata | `StriV.ShaderPipeline.csproj` | Uses `Microsoft.NET.Sdk`, imports `../../build/StriV.Core.Profile.props`, targets `net10.0`, and sets Stri-V assembly/root namespace. |
| Lexing | `Lexing/ShaderLexer.cs`, `Lexing/Token.cs`, `Lexing/TokenKind.cs`, `Lexing/SourceSpan.cs` | Small generic lexer with broad token categories, keyword set, comments, strings, preprocessor directives, and spans. |
| Parsing | `Parsing/ShaderParser.cs`, `Parsing/ParseResult.cs`, `Parsing/BaseCallScanner.cs` | Regex/balanced-block parser for HLSL-ish functions, `shader`, `stage stream`, `stage` methods, base calls, and effect blocks. |
| AST | `Ast/ShaderAst.cs` | Compact Stri-V/Stride-shaped document records: `SdslDocument`, `SdslShader`, `SdslEffectBlock`, streams, stage methods, and base calls. |
| Diagnostics | `Diagnostics/Diagnostic.cs` | Minimal diagnostic record with code/message/line/column and helper creation. |
| Lowering | `Lowering/ShaderLowerer.cs`, `Lowering/StreamUsageAnalyzer.cs` | Emits separate vertex/pixel HLSL, maps stream usage, and bridges parsed Stri-V SDSL shapes to generated HLSL. |
| Artifacts | `Artifacts/ShaderArtifactEmitter.cs`, `Artifacts/ShaderArtifactJsonWriter.cs`, `Artifacts/ShaderArtifactManifest.cs`, `Artifacts/ShaderArtifactOptions.cs` | Writes generated HLSL, optional DXC SPIR-V outputs, logs, manifest JSON, IO records, specialization records, and diagnostics. |

No shader/SDSL/Stri-V tests were found under `tests` by the requested file-name grep. Existing tests are Aurelian core/runtime/world/rendering-contract tests only.

### Stri-V carried-over semantic shape

The current C# pipeline is useful, but narrow:

- It recognizes a limited keyword set: `struct`, `return`, `void`, vector scalar names, `shader`, `stage`, `stream`, `override`, and `streams`.
- Token kinds are broad categories rather than a complete SDSL-V token enum.
- The parser relies heavily on regular expressions and balanced block extraction rather than a token-driven recursive-descent grammar.
- AST nodes preserve old SDSL/Stride concepts including base shaders, `stage stream`, `stage override`, effect blocks, using parameters, and mixins.
- Generic parameter capture exists, but it is textual and Stri-V-shaped rather than WyrmCoil-shaped constraints/interfaces/compile declarations.
- Lowering/emission focuses on vertex and pixel HLSL generation, with stream IO extraction.
- DXC integration exists in the artifact emitter and emits SPIR-V if `dxc` is available, otherwise records a warning.
- Artifact output is already a useful seed because it has manifest/versioning/hashing/entry/stage/IO/diagnostic concepts.

## 5. WyrmCoil SDSL-V reference summary

WyrmCoil SDSL-V is located at:

```text
CodeReferences/WyrmCoil/Engine/shader/sdslv/
```

Representative files:

| File | Role |
| --- | --- |
| `mod.rs` | Module surface re-exporting artifact, AST, diagnostics, DXC, emitter, lexer, parser, runner, token, validation, and tests. |
| `token.rs` | Explicit SDSL-V token and span model. |
| `lexer.rs` | Token-driven lexer. |
| `ast.rs` | Semantic AST for modules, declarations, statements, expressions, flows, utility selection, fallibility, and shader constructs. |
| `parser.rs` | Recursive-descent parser over `SdslvTokenKind`. |
| `diagnostic.rs` | SDSL-V diagnostic model. |
| `validation.rs` | Semantic validation entry points for source/modules/tests. |
| `emitter.rs` | HLSL emitter for supported SDSL-V subset. |
| `artifact.rs` | Shader artifact model and entry-point collection. |
| `dxc.rs` | DXC discovery/command/compile wrapper. |
| `runner.rs` | Test runner/interpreter-style support for SDSL-V test modules. |
| `tests.rs` | Parser, lexer, validation, emission, flow, switch/match, and utility/fallibility tests. |

WyrmCoil's AST makes the intended SDSL-V semantic surface explicit:

- `SdslvModule` contains optional namespace, uses, and declarations.
- `SdslvDecl` supports type aliases, streams, records, interfaces, shaders, flows, compile declarations, and enums.
- `SdslvTypeRef` supports named paths and arrays.
- `SdslvShaderDecl` supports generic parameters, implemented interfaces, where constraints, material fields, regular methods, and stage methods.
- Function declarations include return types, optional error types, optional bodies, and stage/override metadata.
- Statements include let, assign, return, if, for, expression, and empty statements.
- Expressions include identifiers, literals, arrays, field access, index, calls, binary/unary, `with`, `switch`, `match`, `when utility`, `try` propagation, and `unwrap`.
- Flow declarations include parameters, return type, optional board fields, states, `when`, `goto`, return, and board assignment.
- Artifact generation validates, emits HLSL, collects entry points, maps stages to shader profiles, and supports compile declarations for generic shader aliases.

Most importantly, WyrmCoil SDSL-V does **not** model Stride mixins as the native composition mechanism. Its composition model is explicit language machinery: interfaces, generics, compile aliases, records/streams, flow/state machines, expressions, and validation.

## 6. GoOct/Oct reference lessons

GoOct is located at:

```text
CodeReferences/oct/
```

Representative files:

| File | Role |
| --- | --- |
| `lex.go` | Clear lexer/token model with keyword-specific token kinds, source positions, comment skipping, and lexing errors. |
| `parse.go` | Parser organized by top-level declarations, blocks/statements, expression precedence, postfix/prefix parsing, flow/state constructs, switch/match, and utility-when parsing. |
| `program.go` | AST model for files, records, enums, functions, flows, board/state declarations, statements, expressions, switch, match, fallibility, record updates, and enum values. |
| `interpret.go` | Runtime/interpreter organization for evaluating statements/expressions, switch/match, utility selection, flow instances, records, arrays, fallibility, and builtins. |

Lessons for Aurelian SDSL-V:

1. Keep lexer, parser, AST, validation, and runtime/evaluation/emission boundaries explicit.
2. Prefer small AST nodes with clear responsibilities over regex-captured source text.
3. Model flow/state constructs as first-class language elements rather than comments or lowering conventions.
4. Treat switch/match/utility/fallibility as syntax and semantic constructs, not emitter-only tricks.
5. Keep diagnostics close to source spans and parser/validator phases.
6. Avoid magical compiler constructs inherited from Stride SDSL mixins; require explicit declarations that can be parsed, validated, and tested.
7. Use interpreter/evaluator organization only as design inspiration; Aurelian does not need to port GoOct.

## 7. Capability comparison table

| Capability | Stri-V carried-over pipeline | WyrmCoil SDSL-V | Gap | Aurelian recommendation |
| ---------- | ---------------------------- | --------------- | --- | ----------------------- |
| Lexer/token model | Broad `TokenKind` categories and small keyword set; comments and preprocessor directives are retained as tokens. | Explicit `SdslvTokenKind` for namespace/use/type/stream/record/interface/shader/material/stage/fn/implements/where/compile/flow/board/state/when/utility/switch/match/fallibility and operators. | Stri-V tokens are too generic and incomplete for SDSL-V. | Replace with Aurelian token enum modeled after WyrmCoil; keep `SourceSpan` concept. |
| Parser architecture | Regex plus balanced-block scanning. | Token-driven recursive descent. | Regex parsing cannot scale to SDSL-V semantics. | Rewrite parser around token stream and precedence parsing. |
| AST module/declaration shape | `SdslDocument` with shaders/effect blocks; no module namespace/use declaration model. | `SdslvModule` with namespace, uses, and typed declarations. | Missing top-level language structure. | Introduce `Module`, `UseDecl`, and declaration union/classes early. |
| Type references | Mostly raw strings for return/stream/generic text. | `SdslvTypeRef` named paths and arrays. | Missing semantic type references. | Port `TypeRef` shape before deep parser work. |
| Records | Not present as SDSL-V `record`; HLSL `struct` recognized textually. | First-class `RecordDecl`. | Large semantic gap. | M0 must parse record declarations. |
| Streams | `stage stream Type Name : Semantic;` records individual stage streams. | First-class stream declaration with fields. | Different syntax and shape. | Supersede Stri-V `stage stream` with WyrmCoil stream declarations. |
| Shader declarations | `shader Name<T> : Base { ... }`, base shaders, stage methods. | Shader declarations with generics, implements, constraints, material fields, methods, stage methods. | Stri-V preserves inheritance/base shader model. | Rewrite shader AST toward WyrmCoil; do not preserve base-shader semantics. |
| Interfaces | Not present. | First-class interface declarations and shader implements. | Missing. | M1 feature; parse stubs can exist earlier if cheap. |
| Generics | Textual generic parameter capture. | Generic parameters plus where constraints and compile aliases. | Stri-V generics lack semantic constraints. | M1; do not reuse text-only representation as final. |
| Compile declarations | Not present as SDSL-V construct. | `compile` declarations create concrete aliases for generic shaders and artifact entry points. | Missing. | M1 after generic shader AST exists. |
| Enums | Not present. | First-class enum declarations and match support. | Missing. | M0 parse enum declarations; M1 match semantics. |
| Flow declarations/state machines | Not present. | First-class flows, board fields, states, when/goto/return/board assignment. | Missing. | M1+; align with WyrmCoil and Oct flow patterns. |
| Board fields | Not present. | Flow board fields are explicit. | Missing. | M1 flow work, not shader M0. |
| Statements/expressions | Method bodies are mostly raw body strings plus base-call scan. | Structured statements and expressions. | Major gap. | M0 needs let/assign/return/expression and basic expression AST. |
| Switch | Not parsed structurally. | Switch expression represented in AST and parser. | Missing. | M1. |
| Match | Not present. | Match expression represented in AST and parser. | Missing. | M1, after enums. |
| Utility selection | Not present. | `WhenUtility` expression and policy support. | Missing. | M1/M2, borrow Oct organization. |
| Fallibility / try / unwrap | Not present. | Optional function error type plus try-propagate/unwrap expressions. | Missing. | M1/M2; design before emitting. |
| Diagnostics | Minimal diagnostic record and code inference for artifact phases. | Dedicated SDSL-V diagnostics across lex/parse/validation/emission. | Stri-V diagnostics are too shallow. | Keep simple C# record idea but expand phase/severity/span model. |
| Validation | Minimal implicit checks in parse/lower/artifact path. | Dedicated validation module for source/module/test modules. | Missing semantic validation pass. | Add validation before HLSL emission. |
| HLSL emission | Existing vertex/pixel HLSL generation seed. | HLSL emitter for WyrmCoil AST subset. | Stri-V emitter targets old AST. | Reuse only isolated emission ideas; rewrite over Aurelian AST. |
| SPIR-V / DXC runner | Artifact emitter can find `dxc`, run `-spirv`, and record warnings/errors. | Dedicated DXC options/request/result/error model. | Stri-V runner is coupled to artifact emitter. | Extract into `Dxc/` module modeled after WyrmCoil. |
| Artifact output | Manifest JSON with format, source hash, stages, specializations, IO, mixins/effects, diagnostics. | Artifact model with HLSL, entry points, stage/profile mapping, compile aliases. | Stri-V manifest includes obsolete mixin/effect fields. | Keep hashing/stage/IO ideas; remove mixin/effect semantics. |
| Test fixture strategy | No current tests under `tests`; no linked solution project. | Rust unit tests exercise lexer/parser/records/generics/switch/flow/emitter/artifacts. | Aurelian lacks shader fixtures. | Add C# fixture tests during A3/A4; first triangle/sprite shader. |
| Old SDSL mixins/effect model | Effect blocks and mixin lists are represented and serialized. | No mixins; explicit SDSL-V machinery instead. | Incompatible semantic direction. | Treat mixins/effects as reference/corpus-only; do not support in SDSL-V M0. |

## 8. Semantic authority decision

| Question | Decision |
| --- | --- |
| Which implementation defines Aurelian SDSL-V semantics? | WyrmCoil Rust SDSL-V. |
| How much of Stri-V C# code should survive? | The project structure can seed a C# module, and isolated utilities such as spans, diagnostics basics, HLSL/artifact concepts, hashing, and DXC discovery can influence implementation. The parser/AST semantics should mostly be rewritten. |
| Should Aurelian attempt compatibility with Stride SDSL? | No native compatibility target for M0. Stride SDSL is corpus/reference material. |
| Should mixins be supported? | No. SDSL-V M0 should not support Stride mixins. Later compatibility tooling may translate selected mixin families into explicit SDSL-V, but mixins should not become a native feature. |
| Should old `.sdsl` be accepted directly? | No. Old `.sdsl` should be ported family-by-family into `.sdslv` with design notes. |
| What is the relationship between SDSL, SDSL-V, HLSL, and Aurelian shader assets? | SDSL/Stride `.sdsl` is historical source corpus. SDSL-V is Aurelian's authoring language. HLSL is an emission target and behavioral reference, not the source language. Aurelian shader assets should package `.sdslv`, generated HLSL, compiler outputs, reflection/IO data, diagnostics, and manifests. |

## 9. Rename/port/discard map

Recommended first layout:

```text
src/Aurelian.Shaders/
  Aurelian.Shaders.csproj
  Language/
    Tokens/
    Lexing/
    Parsing/
    Ast/
    Diagnostics/
    Validation/
  Lowering/
  Emission/
    Hlsl/
  Artifacts/
  Dxc/
```

A simpler A3 rename can temporarily keep the current folder grouping while changing the project and namespaces, then A4 can split `Language/` and `Emission/` cleanly.

| Current Stri-V file/project | Aurelian destination | Action | Reason |
| --------------------------- | -------------------- | ------ | ------ |
| `src/StriV.ShaderPipeline/StriV.ShaderPipeline.csproj` | `src/Aurelian.Shaders/Aurelian.Shaders.csproj` | Rename/move directly, then edit metadata | This is the module seed, but project identity must become Aurelian. |
| `Lexing/SourceSpan.cs` | `Language/Tokens/SourceSpan.cs` or `Language/Source/SourceSpan.cs` | Rename/move directly with minor edits | Span model is useful. |
| `Lexing/Token.cs` | `Language/Tokens/SdslvToken.cs` | Port with rewrite | Shape is useful, but token kind must become SDSL-V-specific. |
| `Lexing/TokenKind.cs` | `Language/Tokens/SdslvTokenKind.cs` | Superseded by WyrmCoil | Current categories are too broad. |
| `Lexing/ShaderLexer.cs` | `Language/Lexing/SdslvLexer.cs` | Port with rewrite | Keep scanner mechanics where helpful; expand tokens/keywords/operators. |
| `Parsing/ParseResult.cs` | `Language/Parsing/ParseResult.cs` | Rename/move directly or replace | Result pattern is useful if diagnostics remain list-based. |
| `Parsing/BaseCallScanner.cs` | none, or `Legacy/` during transition only | Delete/discard after rename | Base calls are tied to old inheritance/mixin style. |
| `Parsing/ShaderParser.cs` | `Language/Parsing/SdslvParser.cs` | Port with rewrite | Regex parser should be replaced by recursive-descent parser. |
| `Ast/ShaderAst.cs` | `Language/Ast/*.cs` | Superseded by WyrmCoil | Current AST encodes old SDSL/effect/mixin concepts. Split into module/decl/type/stmt/expr files. |
| `Diagnostics/Diagnostic.cs` | `Language/Diagnostics/SdslvDiagnostic.cs` | Port with rewrite | Keep simple record idea; expand severity/phase/span. |
| `Lowering/ShaderLowerer.cs` | `Lowering/` or `Emission/Hlsl/` | Port with rewrite | Must target WyrmCoil-shaped AST; old lowering can inform HLSL formatting only. |
| `Lowering/StreamUsageAnalyzer.cs` | `Emission/Hlsl/StreamLayoutAnalyzer.cs` | Port with rewrite | Useful concept, but streams should be first-class SDSL-V declarations. |
| `Artifacts/ShaderArtifactOptions.cs` | `Artifacts/ShaderArtifactOptions.cs` | Rename/move directly with field review | Artifact options remain useful. |
| `Artifacts/ShaderArtifactManifest.cs` | `Artifacts/SdslvShaderArtifactManifest.cs` | Port with rewrite | Keep source hash/stages/IO/diagnostics; remove effect/mixin fields. |
| `Artifacts/ShaderArtifactJsonWriter.cs` | `Artifacts/ShaderArtifactJsonWriter.cs` | Rename/move directly, then update schema | JSON writing can survive if schema changes. |
| `Artifacts/ShaderArtifactEmitter.cs` | `Artifacts/` + `Dxc/` + `Emission/Hlsl/` | Split/rewrite | Currently combines parsing/lowering/HLSL/DXC/manifest concerns. |
| `src/StriV.AssetPipeline/` | future `src/Aurelian.Assets/` | Defer | Out of A2 scope. |
| `src/StriV.AssetTool/` | future Aurelian tool/CLI project | Defer | Out of A2 scope. |

## 10. Aurelian SDSL-V MVP scope

### M0 — first useful SDSL-V compiler slice

M0 should include:

- Tokenization with WyrmCoil-aligned token kinds for the subset.
- `namespace` and `use` parsing.
- Top-level declaration parsing for:
  - `record`;
  - `stream`;
  - `enum`;
  - `shader`.
- Shader body parsing for:
  - material fields if needed for the first fixture;
  - regular functions;
  - `stage` functions for vertex/pixel first.
- Function signatures:
  - parameters;
  - return types;
  - named/path type references;
  - arrays only if needed by fixture, otherwise defer.
- Statements:
  - `let`;
  - assignment;
  - `return`;
  - expression statement.
- Expressions:
  - identifiers;
  - integer/float/string/bool literals;
  - field access;
  - function calls;
  - binary/unary operators;
  - parenthesized expressions.
- Diagnostics:
  - lex/parse diagnostic codes;
  - line/column/span;
  - phase/severity.
- Validation:
  - duplicate declarations;
  - missing shader/stage entry points;
  - unknown obvious type names for the core fixture;
  - unsupported features produce clear diagnostics.
- Minimal HLSL emission:
  - one triangle/sprite/core fixture;
  - vertex and pixel stages;
  - generated HLSL kept inspectable.
- Test fixture strategy:
  - lexer tests;
  - parser golden-shape tests;
  - one emission snapshot or normalized text assertion;
  - artifact manifest smoke if cheap.

### M1 — convergence features after M0

M1 should include:

- Generic shader parameters and where constraints.
- Interfaces and shader `implements`.
- `compile` declarations and generic specialization aliases.
- `switch` expressions.
- `match` expressions over enums.
- `with`/record update if needed by shader ergonomics.
- Utility-style expressions inspired by WyrmCoil and GoOct.
- Flow declarations, board fields, states, `when`, `goto`, and flow returns.
- Fallibility constructs: function error types, try propagation, unwrap.
- Dedicated DXC/SPIR-V validation path.
- Artifact manifest schema stabilized enough for fixtures.

## 11. Conversion milestone plan

### A2 output

Audit only. This document is the output.

### A3 — identity conversion, no semantic expansion

- Rename `src/StriV.ShaderPipeline` to `src/Aurelian.Shaders`.
- Rename project file and assembly/root namespace to `Aurelian.Shaders`.
- Update namespaces from `StriV.ShaderPipeline` to `Aurelian.Shaders`.
- Decide whether to link `Aurelian.Shaders` into `Aurelian.slnx` in the same milestone; if linked, add minimal tests at the same time.
- Preserve behavior while renaming.
- Do not attempt SDSL-V convergence in the rename commit.

### A4 — AST convergence

- Replace `ShaderAst.cs` with WyrmCoil-shaped module/declaration/type/statement/expression files.
- Add records, streams, enums, shaders, stage functions, and type references.
- Remove native effect/mixin/base-shader concepts from the final AST.
- Preserve any old Stri-V examples only as legacy/reference fixtures if needed.

### A5 — parser convergence

- Replace regex parser with token-driven parser.
- Implement declaration parsing and precedence-based expressions for M0.
- Produce diagnostics instead of throwing for ordinary syntax errors.

### A6 — diagnostics and validation convergence

- Add lex/parse/validation diagnostic phases.
- Add duplicate declaration, unknown type, entry-point, and unsupported-feature validation.
- Keep error messages stable enough for tests.

### A7 — HLSL emission M0

- Implement HLSL emission over the new AST.
- Generate vertex/pixel entry points for M0 fixture.
- Keep generated HLSL inspectable and deterministic.

### A8 — first shader fixture/artifact

- Add first triangle/sprite/core `.sdslv` fixture.
- Emit HLSL and, where tool availability permits, compile with DXC to SPIR-V.
- Write manifest with source hash, entry points, IO records, diagnostics, and compiler metadata.

## 12. Shader corpus port strategy

This audit should connect to the Stride shader corpus strategy from M26 as follows:

- Raw `.sdsl` is a reference/intent source, not native Aurelian input.
- Generated or inspected HLSL is the behavior source when validating ported shader families.
- Aurelian SDSL-V is the new authoring language.
- Codex should port shader families manually, family-by-family, with design notes explaining each semantic translation.
- The first family should be minimal triangle/sprite/core shaders, not full PBR.
- Full PBR/material graph work should wait until SDSL-V records/streams/stage functions/HLSL emission and artifact manifests are stable.
- Old Stride mixins should be translated into explicit records, interfaces, generic constraints, compile declarations, utility expressions, or ordinary functions when appropriate.

## 13. Risks and blockers

| Risk | Cause | Mitigation |
| ---- | ----- | ---------- |
| Stri-V pipeline diverges from WyrmCoil semantics | Current AST/parser preserves old SDSL/effect/mixin/base-shader concepts. | Treat Stri-V as seed only; make WyrmCoil AST the convergence target. |
| WyrmCoil Rust code may not map directly to C# idioms | Rust enums/results/modules differ from C# records/classes/discriminated unions. | Port semantics, not syntax; use C# records, abstract base records, or sealed union-like types consistently. |
| Old SDSL mixins tempt compatibility trap | Stride shader corpus contains mixins and effect composition. | Explicitly exclude mixins from SDSL-V M0; port families manually. |
| HLSL emission before IR may become brittle | Direct AST-to-HLSL can couple parser choices to backend formatting. | Keep M0 emitter small; introduce lowering/IR once features expand. |
| DXC/SPIR-V tool availability | CI/dev machines may not have `dxc`. | Make DXC optional in early tests; separate command construction from execution; record unavailable diagnostics. |
| Shader corpus is large | Stride corpus includes broad rendering/material scenarios. | Start with triangle/sprite/core; defer PBR and material graph families. |
| Tests may be tied to old Stri-V semantics | Future tests copied from Stri-V could assert mixins/effects/base shaders. | Label legacy tests clearly and write new SDSL-V tests from WyrmCoil semantics. |
| Premature package/API freeze | Artifact/schema/API may appear stable too early. | Keep schemas versioned and mark A3-A8 shader APIs experimental. |
| Parser rewrite scope creep | WyrmCoil supports many features beyond M0. | Define parser feature gates and unsupported-feature diagnostics. |
| Flow/utility/fallibility complexity | These constructs are language-level and backend-sensitive. | Defer to M1/M2 after records/streams/shaders and basic emission are proven. |

## 14. Validation / command log

Commands run for this audit:

```bash
git status --short
find . -maxdepth 4 -type d | sort
find . -maxdepth 5 \( -name '*.csproj' -o -name '*.slnx' -o -name '*.sln' -o -name 'Directory.Build.props' -o -name 'Directory.Packages.props' -o -name 'global.json' \) | sort
sed -n '1,240p' Aurelian.slnx
find src/StriV.ShaderPipeline -type f | sort
find src/StriV.ShaderPipeline -type f -name '*.cs' | wc -l
find tests -maxdepth 4 -type f | grep -Ei 'Shader|SDSL|Pipeline|StriV' | sort || true
rg -n "namespace|class|record|struct|enum|interface|Shader|SDSL|SDSL-V|sdslv|Lexer|Token|Parser|Ast|Lower|Emit|HLSL|DXC|SPIR|Diagnostic|Mixin|Generic|Interface|Stream|Record|Shader|Compile|Effect|Stage|Vertex|Pixel|Fragment|Compute|Flow|Switch|Match|Utility|Try|Unwrap|Fallible" src/StriV.ShaderPipeline tests docs -g '*.cs' -g '*.md' -g '*.toml' -g '*.json' > /tmp/aurelian-a2-striv-shaderpipeline-search.txt || true
wc -l /tmp/aurelian-a2-striv-shaderpipeline-search.txt
head -n 1400 /tmp/aurelian-a2-striv-shaderpipeline-search.txt
sed -n '1,220p' src/StriV.ShaderPipeline/StriV.ShaderPipeline.csproj
find src/StriV.ShaderPipeline -type f \( -name '*Lexer*.cs' -o -name '*Parser*.cs' -o -name '*Ast*.cs' -o -name '*Lower*.cs' -o -name '*Emitter*.cs' -o -name '*Diagnostic*.cs' \) | sort
sed -n '1,260p' src/StriV.ShaderPipeline/Lexing/TokenKind.cs
sed -n '1,260p' src/StriV.ShaderPipeline/Lexing/ShaderLexer.cs
sed -n '1,260p' src/StriV.ShaderPipeline/Ast/ShaderAst.cs
sed -n '1,260p' src/StriV.ShaderPipeline/Parsing/ShaderParser.cs
sed -n '1,260p' src/StriV.ShaderPipeline/Diagnostics/Diagnostic.cs
sed -n '1,260p' src/StriV.ShaderPipeline/Lowering/ShaderLowerer.cs
sed -n '1,260p' src/StriV.ShaderPipeline/Artifacts/ShaderArtifactEmitter.cs
sed -n '1,260p' src/StriV.ShaderPipeline/Artifacts/ShaderArtifactManifest.cs
find CodeReferences -type d | grep -Ei 'WyrmCoil|sdslv|shader' | sort | head -n 200
find CodeReferences -type f | grep -Ei 'WyrmCoil.*sdslv|shader/sdslv|sdslv' | sort
rg -n "Sdslv|sdslv|TypeRef|Module|Decl|Enum|Flow|Board|State|When|Goto|Return|Switch|Match|WhenUtility|TryPropagate|Unwrap|Generic|Interface|Shader|Compile|Record|Stream|Parser|Lexer|Emitter|Validate|Diagnostic|Hlsl|Dxc|Artifact" CodeReferences/WyrmCoil -g '*.rs' -g '*.md' > /tmp/aurelian-a2-wyrmcoil-sdslv-search.txt || true
wc -l /tmp/aurelian-a2-wyrmcoil-sdslv-search.txt
head -n 1800 /tmp/aurelian-a2-wyrmcoil-sdslv-search.txt
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/mod.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/ast.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/token.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/lexer.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/parser.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/diagnostic.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/validation.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/emitter.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/dxc.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/artifact.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/runner.rs
sed -n '1,220p' CodeReferences/WyrmCoil/Engine/shader/sdslv/tests.rs
find CodeReferences -type f | grep -Ei 'oct|interpret.go|lex.go|parse.go|program.go' | sort
sed -n '1,360p' CodeReferences/oct/lex.go || true
sed -n '1,460p' CodeReferences/oct/parse.go || true
sed -n '1,460p' CodeReferences/oct/program.go || true
sed -n '1,460p' CodeReferences/oct/interpret.go || true
rg -n "Token|Lexer|Parser|Ast|Expr|Statement|Program|Interpret|Function|Type|Generic|Interface|Switch|Match|Error|Diagnostic|Result|Flow|State|Let|Assign|Return|Call|Array|Record" CodeReferences/oct -g '*.go' > /tmp/aurelian-a2-gooct-search.txt || true
wc -l /tmp/aurelian-a2-gooct-search.txt
head -n 1200 /tmp/aurelian-a2-gooct-search.txt
```

Observed command results:

| Command/check | Result |
| --- | --- |
| Initial `git status --short` | Clean before the audit file was created. |
| Repository directory/project inventory | Confirmed Aurelian projects, carried-over Stri-V projects, vendored Dominatus, and reference-only code. |
| `Aurelian.slnx` inspection | Confirmed no Stri-V projects are linked into the solution. |
| `find src/StriV.ShaderPipeline -type f -name '*.cs' | wc -l` | `15`. |
| Test file-name grep for shader/SDSL/pipeline/Stri-V | No matching test files found. |
| Stri-V search output count | `351` lines in `/tmp/aurelian-a2-striv-shaderpipeline-search.txt`. |
| WyrmCoil SDSL-V location | `CodeReferences/WyrmCoil/Engine/shader/sdslv/`. |
| WyrmCoil SDSL-V search output count | `3298` lines in `/tmp/aurelian-a2-wyrmcoil-sdslv-search.txt`. |
| GoOct location | `CodeReferences/oct/`. |
| GoOct search output count | `2497` lines in `/tmp/aurelian-a2-gooct-search.txt`; search confirmed lexer/parser/AST/interpreter coverage for flow, switch, match, utility, records, arrays, and fallibility concepts. |

Final docs-only validation commands:

```bash
test -f docs/audits/0002-a2-sdslv-convergence-audit.md
git status --short
```

## 15. Next recommendation

Aurelian should rename/convert `StriV.ShaderPipeline` now only as an identity cleanup milestone, not as a semantic endorsement of the old pipeline.

Recommended next milestone:

> **A3: create `src/Aurelian.Shaders` from `src/StriV.ShaderPipeline`, update namespaces/project metadata, optionally link with minimal tests, and keep behavior unchanged.**

Recommended SDSL-V M0:

- Tokenization.
- Namespace/use parsing.
- Records, streams, enums, shader declarations.
- Stage methods and typed function signatures.
- Let/assign/return/expression statements.
- Identifier/literal/field/call/binary/unary expressions.
- Diagnostics and minimal validation.
- Minimal HLSL emission for one triangle/sprite/core fixture.

The milestone after A3 should be AST convergence toward WyrmCoil SDSL-V. That is where Aurelian should stop carrying Stri-V's old effect/mixin/base-shader language semantics forward and become the “restorer of world”: explicit, modern SDSL-V instead of historical Stride SDSL compatibility.
