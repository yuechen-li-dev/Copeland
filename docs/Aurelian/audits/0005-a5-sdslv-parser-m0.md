# 0005 — A5 SDSL-V parser M0

## 1. Files changed

- Added new SDSL-V token contracts under `src/Aurelian.Shaders/Language/Tokens/`.
- Added new SDSL-V lexer and lex result under `src/Aurelian.Shaders/Language/Lexing/`.
- Added new SDSL-V diagnostics contracts under `src/Aurelian.Shaders/Language/Diagnostics/`.
- Added parser M0 and parse result under `src/Aurelian.Shaders/Language/Parsing/`.
- Added lexer/parser tests under `tests/Aurelian.Shaders.Tests/`.
- Updated `docs/architecture/mvp-roadmap.md` and `README.md`.
- Created this audit report: `docs/audits/0005-a5-sdslv-parser-m0.md`.

## 2. Task scope

A5 adds the first token-driven SDSL-V parser path for the new `Aurelian.Shaders.Language.Ast` model. The new path is parallel to the carried-over legacy shader pipeline.

Explicit non-goals preserved:

- No deletion or rewrite of the legacy parser.
- No deletion or rewrite of the legacy lowerer/emitter.
- No artifact emission changes.
- No HLSL emission over the new AST.
- No full SDSL-V implementation.
- No renderer, windowing, asset, mixin, Stride compatibility, or vendor/reference-folder work.

## 3. Parser M0 feature set

Parser M0 supports:

- optional `namespace` declarations;
- zero or more `use` declarations;
- named path type references;
- `array<T, N>` type references;
- `type` aliases;
- `record` field blocks;
- `stream` field blocks;
- `enum` declarations with semicolon or comma separated variants;
- `shader` shells with optional generic parameter lists and optional `implements` lists;
- material fields in `material Name: Type;` and `material { Name: Type; }` forms;
- small function/stage shells with parameter lists, return types, optional error type markers, and optional bodies;
- simple statement/body support for empty statements, `let`, assignment, `return`, expression statements, calls, field/index access, unary negate, and simple binary expressions.

Unsupported declarations such as flow/interface/compile currently produce parser diagnostics instead of being accepted.

## 4. Token/lexer model

The new token model is intentionally separate from the legacy `Aurelian.Shaders.Lexing` model. It includes SDSL-V-specific keyword tokens, literals, punctuation, operators, unknown tokens, and EOF.

The lexer:

- skips whitespace;
- skips `//` line comments;
- skips `/* ... */` block comments;
- emits identifiers, keywords, integer literals, float literals, string literals, boolean literals, punctuation, and operators;
- always emits EOF;
- reports diagnostics for unknown characters and unterminated strings/comments.

## 5. Diagnostics model

A5 adds a minimal language diagnostics model with severity, phase, code, message, and `SdslvSpan`. Lexing and parsing diagnostics are collected and returned through the lex/parse result objects rather than thrown for ordinary invalid source.

## 6. Parser implementation

The parser is a small recursive-descent parser over the new token stream. It constructs the A4 AST contracts directly and accumulates diagnostics. Recovery is intentionally conservative: it skips to common declaration/member boundaries after unsupported or malformed constructs so later declarations can still be parsed when possible.

## 7. Tests added

Lexer tests cover:

- namespace/use/record tokenization;
- operators and punctuation;
- unknown-character diagnostics;
- unterminated-string diagnostics.

Parser tests cover:

- namespace/use/record module parsing;
- stream parsing;
- enum parsing;
- shader shell parsing;
- named and array type references;
- invalid syntax diagnostics without throwing;
- rejection of unsupported native constructs;
- stage function shell parsing;
- return statements and simple binary expressions.

## 8. Legacy pipeline preservation

The carried-over legacy files under `src/Aurelian.Shaders/Ast/`, `src/Aurelian.Shaders/Lexing/`, `src/Aurelian.Shaders/Parsing/`, `src/Aurelian.Shaders/Lowering/`, and `src/Aurelian.Shaders/Artifacts/` were not modified. Artifact emission remains on the legacy path.

## 9. Boundary checks

Boundary checks were run to verify the new language path does not reference `CodeReferences`, Stride, Machina, WyrmCoil, or Copeland from Aurelian-owned source/tests, and to inspect legacy construct names in the new language/test files.

## 10. Validation results

Validation commands completed successfully:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Mixin|EffectBlock|BaseShader|BaseCall|Compose|StageStream" src/Aurelian.Shaders/Language tests/Aurelian.Shaders.Tests -g '*.cs' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
rg -n "namespace Aurelian\.Shaders\.Language" src/Aurelian.Shaders/Language -g '*.cs'
git status --short
```

## 11. Deferred features

Deferred beyond A5:

- full statement parsing;
- full expression parsing;
- flow parsing;
- switch/match/utility/fallibility parsing;
- interface and compile declaration parsing;
- validation passes over the new AST;
- HLSL/lowering/emission over the new AST;
- migration/removal decisions for the legacy shader pipeline.

## 12. Next recommendation

A6 — SDSL-V parser M1 statements/expressions.

Parser M0 is stable enough to produce real modules, declarations, shader shells, and small bodies. The next highest-value step is broadening statements/expressions and recovery before building validation or HLSL emission on top of the new AST.
