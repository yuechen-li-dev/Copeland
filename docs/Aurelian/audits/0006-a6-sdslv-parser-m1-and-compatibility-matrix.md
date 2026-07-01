# 0006 — A6 SDSL-V parser M1 and compatibility matrix

## 1. Files changed

- `src/Aurelian.Shaders/Language/Tokens/SdslvTokenKind.cs`
- `src/Aurelian.Shaders/Language/Lexing/SdslvLexer.cs`
- `src/Aurelian.Shaders/Language/Ast/SdslvExpression.cs`
- `src/Aurelian.Shaders/Language/Parsing/SdslvParser.cs`
- `tests/Aurelian.Shaders.Tests/SdslvParserM1ExpressionTests.cs`
- `tests/Aurelian.Shaders.Tests/SdslvParserM1StatementTests.cs`
- `docs/architecture/sdslv-compatibility-matrix.md`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/vendor-strategy.md`
- `docs/audits/0006-a6-sdslv-parser-m1-and-compatibility-matrix.md`

## 2. Task scope

A6 advances the new token-driven `Aurelian.Shaders.Language` parser from M0 declaration/body shells to M1 statement and expression syntax. It keeps the legacy parser/lowerer/emitter path intact and does not add HLSL emission over the new AST.

A6 also records WyrmCoil ↔ Aurelian SDSL-V compatibility decisions in an explicit architecture matrix. WyrmCoil remains reference-only and inspirational; Aurelian SDSL-V remains the production C# implementation.

## 3. Parser M1 feature set

Parser M1 now supports parse shapes for:

- arithmetic, comparison, equality, logical, and modulo binary operators;
- unary negate and logical-not operators;
- field access, call, index, and fallibility postfix chaining;
- array literals, including empty arrays;
- with expressions;
- switch expressions with optional subject, `case`, `else`, and `=>`/`->` arms;
- match expressions with enum variant arms, fallible `ok`/`err` arms, and Aurelian `else` arms;
- prefix `try` and `unwrap` fallibility expressions;
- empty, let, assignment, return, if/else, bounded for, and expression statements.

## 4. Expression parsing changes

The expression parser remains recursive-descent/precedence based, but M1 broadens the precedence table to:

1. `||`
2. `&&`
3. `==`, `!=`
4. `<`, `<=`, `>`, `>=`
5. `+`, `-`
6. `*`, `/`, `%`
7. unary `!`, unary `-`
8. postfix field access, calls, indexing, `with`, `?`, and `!`
9. literals, identifiers, array literals, switch, match, and parenthesized expressions

M1 adds array literal parsing, with-expression update parsing, switch expression arm parsing, match expression arm parsing, prefix fallibility parsing, and compatibility postfix fallibility parsing.

## 5. Statement parsing changes

The statement parser now recognizes:

- `;` empty statements;
- `let name: Type = initializer;` local declarations;
- `target = value;` assignments;
- `return value;` returns;
- `if condition { ... } else { ... }` statements;
- `for i in start..end step value { ... }` bounded for statements;
- expression statements.

Statement blocks are parsed for function/stage bodies and nested if/for bodies. This is parse-shape support only; semantic validation remains deferred.

## 6. Lexer/token changes

A6 adds tokens and lexer keyword mappings for:

- `case`;
- `score`;
- `with`;
- `step`;
- `ok`;
- `err`;
- `..` ranges.

Existing M0 tokens for `else`, `if`, `=>`, `.`, brackets, comparisons, logical operators, `try`, and `unwrap` are used by M1 parser support.

## 7. Tests added

A6 adds expression tests for:

- binary precedence;
- logical nested binary shape;
- unary shape;
- field/call/index chaining;
- array literals;
- with expressions;
- switch expressions;
- match expressions;
- try/unwrap expressions;
- invalid expression diagnostics.

A6 adds statement tests for:

- if/else statements;
- bounded for statements;
- empty, let, assignment, and expression statements.

No HLSL output tests were added.

## 8. Compatibility matrix summary

`docs/architecture/sdslv-compatibility-matrix.md` now tracks feature-by-feature WyrmCoil SDSL-V versus Aurelian SDSL-V status using:

- Compatible;
- Intentionally different;
- Deferred;
- Not applicable;
- Unknown.

The matrix covers module structure, declarations, type refs, arrays, shader members, statements, expressions, switch/match/utility/fallibility, flow features, diagnostics, validation, emission, artifacts, test runner differences, and old Stride concepts.

## 9. WyrmCoil/Aurelian divergence notes

Recorded Aurelian decisions include:

- WyrmCoil is reference/inspiration, not a production dependency.
- Aurelian SDSL-V is a C# production implementation with C# records/result objects.
- Aurelian with expressions prefer `Field = value` updates while accepting `Field: value` for compatibility.
- Aurelian switch expressions prefer `=>` and accept `->` as a compatibility parse shape.
- Aurelian match expressions include an explicit `else` arm parse shape.
- Aurelian documents prefix `try expression` and `unwrap expression`; postfix `?` and `!` are parsed as compatibility shapes.
- Utility expressions remain deferred to avoid overbuilding parser M1.

## 10. Legacy pipeline preservation

A6 does not rewrite or delete the carried-over legacy shader parser, lowerer, emitter, or artifact path. The new work remains under `src/Aurelian.Shaders/Language` and tests under `tests/Aurelian.Shaders.Tests`.

## 11. Boundary checks

Boundary checks were run against the new language path, shader tests, and compatibility matrix to verify:

- no native legacy mixin/effect/base-shader tokens or concepts leaked into the new parser/test path;
- no `CodeReferences`, Stride, Machina, WyrmCoil, or Copeland coupling was added to production source or tests;
- WyrmCoil mentions remain in documentation only.

## 12. Validation results

Validation completed successfully:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Mixin|EffectBlock|BaseShader|BaseCall|Compose|StageStream" src/Aurelian.Shaders/Language tests/Aurelian.Shaders.Tests docs/architecture/sdslv-compatibility-matrix.md -g '*.cs' -g '*.md' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
git status --short
```

## 13. Deferred features

Deferred beyond A6:

- utility expression parser support;
- interface declarations;
- compile declarations;
- flow declarations, board fields, states, and when/goto parsing;
- semantic validation over the new AST;
- HLSL/lowering/emission over the new AST;
- artifact schema over the new AST;
- production shader fixtures.

## 14. Next recommendation

A7 — SDSL-V validation M0.

Parser M1 now covers enough statement/expression shape to make validation the highest-value next step. Validation should establish type, control-flow, declaration-reference, switch/match arm, and statement legality rules before new-AST HLSL emission begins.
