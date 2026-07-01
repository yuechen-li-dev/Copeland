# 0004 — A4 SDSL-V AST convergence

## 1. Files changed

- Added new Aurelian SDSL-V AST contract files under `src/Aurelian.Shaders/Language/Ast/`.
- Added AST contract tests in `tests/Aurelian.Shaders.Tests/SdslvAstContractTests.cs`.
- Updated architecture/status documentation in `docs/architecture/mvp-roadmap.md`, `docs/architecture/vendor-strategy.md`, and `README.md`.
- Created this audit report: `docs/audits/0004-a4-sdslv-ast-convergence.md`.

## 2. Task scope

A4 is an AST-contract milestone only. It adds WyrmCoil-shaped Aurelian SDSL-V data contracts beside the carried-over legacy shader pipeline.

Explicit non-goals preserved:

- No parser rewrite.
- No lexer rewrite.
- No HLSL lowerer or artifact emitter rewrite.
- No deletion of legacy AST/parser/lowerer files.
- No removal of legacy mixin/effect/base-shader handling from the legacy path.
- No raw `.sdsl` shader porting.
- No `CodeReferences` or vendor modifications.

## 3. WyrmCoil AST reference summary

The WyrmCoil SDSL-V reference models a module as an optional namespace, use declarations, and typed declarations. Declarations include type aliases, records, streams, enums, interfaces, shaders, flow declarations, and compile aliases. The expression and statement model includes typed let/assign/return/if/for forms, expression statements, literals, calls, field/index access, binary/unary expressions, record-with updates, switch, match, utility decisions, and fallibility helpers.

Aurelian copied this shape conceptually into C# records without referencing or compiling WyrmCoil code.

## 4. New AST namespace/folder

The new AST lives under:

```text
src/Aurelian.Shaders/Language/Ast/
```

The namespace is:

```csharp
Aurelian.Shaders.Language.Ast
```

This intentionally separates the native Aurelian SDSL-V model from the carried-over legacy `Aurelian.Shaders.Ast` namespace.

## 5. Types added

A4 added:

- Basic source model: `SdslvPath`, `SdslvSpan`, `SdslvModule`, `SdslvUseDecl`, `SdslvDecl`, `SdslvTypeRef`.
- Type references: `SdslvNamedTypeRef`, `SdslvArrayTypeRef`.
- Declarations: `SdslvTypeAliasDecl`, `SdslvRecordDecl`, `SdslvFieldDecl`, `SdslvStreamDecl`, `SdslvEnumDecl`, `SdslvEnumVariant`, `SdslvInterfaceDecl`, `SdslvShaderDecl`, `SdslvWhereConstraint`, `SdslvCompileDecl`.
- Functions: `SdslvFunctionDecl`, `SdslvFunctionParameter`, `SdslvBody`.
- Statements: let, assign, return, if, for, expression, and empty statement records derived from `SdslvStatement`.
- Expressions: identifier, integer, float, string, bool, array literal, field access, index, call, binary, unary, with, switch, match, when-utility, try-propagate, and unwrap expression records derived from `SdslvExpression`.
- Flow model: `SdslvFlowDecl`, `SdslvFlowBoard`, `SdslvFlowBoardField`, `SdslvFlowState`, `SdslvFlowStatement`, `SdslvFlowWhen`, `SdslvFlowCase`, and `SdslvFlowAction` variants.
- Helpers/operators: `SdslvBinaryOperator`, `SdslvUnaryOperator`, `SdslvSwitchCase`, `SdslvMatchArm`, `SdslvMatchArmKind`, `SdslvUtilityOptions`, `SdslvUtilityCase`, and `SdslvWithUpdate`.

## 6. Legacy concepts intentionally excluded

The new `Language/Ast` model intentionally has no native declarations or expression forms for old carried-over concepts such as old mixin/effect/base-shader constructs, base calls, compose forms, or the old stage-stream model. Those remain only in the legacy path until later migration steps decide their fate.

## 7. Tests added

`tests/Aurelian.Shaders.Tests/SdslvAstContractTests.cs` covers:

- path display;
- named and array type display;
- module namespace/use/declaration storage;
- record, stream, enum, shader, and flow shapes;
- switch, match, utility, try-propagate, and unwrap expression representability.

The tests assert AST construction, containment, equality/reference preservation, and display helper behavior only. They do not test parser or emitter behavior.

## 8. Behavior preservation statement

A4 adds a parallel AST contract and does not alter the carried-over parser, lexer, lowerer, artifact emitter, or legacy AST. Existing shader pipeline smoke behavior remains preserved.

## 9. Boundary checks

Boundary checks were run to verify the new AST and tests do not introduce native legacy shader concepts or external project/code-reference dependencies.

## 10. Validation results

Validation commands completed successfully:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "Mixin|EffectBlock|BaseShader|BaseCall|Compose|StageStream" src/Aurelian.Shaders/Language/Ast tests/Aurelian.Shaders.Tests -g '*.cs' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
git status --short
```

## 11. Next recommendation

A5 — SDSL-V parser convergence M0

A5 should:

- add a token-driven parser path for the new AST;
- initially parse namespace/use/type refs/records/streams/enums/shader shells;
- keep legacy parser/lowerer intact;
- add parser tests against the new AST;
- avoid HLSL emission changes.
