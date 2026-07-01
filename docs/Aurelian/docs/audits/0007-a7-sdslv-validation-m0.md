# 0007 — A7 SDSL-V validation M0

## 1. Files changed

- `src/Aurelian.Shaders/Language/Validation/SdslvValidator.cs`
- `src/Aurelian.Shaders/Language/Validation/SdslvValidationResult.cs`
- `src/Aurelian.Shaders/Language/Validation/SdslvValidationOptions.cs`
- `src/Aurelian.Shaders/Language/Validation/SdslvBuiltinTypes.cs`
- `tests/Aurelian.Shaders.Tests/SdslvValidationM0Tests.cs`
- `docs/architecture/sdslv-compatibility-matrix.md`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/audits/0007-a7-sdslv-validation-m0.md`

## 2. Task scope

A7 adds SDSL-V validation M0 for the new `Aurelian.Shaders.Language` parser/AST path. The pipeline covered by this milestone is:

1. parse SDSL-V source with `SdslvParser`;
2. validate the resulting `SdslvModule` with `SdslvValidator`;
3. assert validation diagnostics through ordinary xUnit tests.

This milestone does not implement `.sdslvtest`, an Oct interpreter port, CPU shader behavior evaluation, HLSL emission, DXC/SPIR-V, renderer work, asset work, Stride compatibility, native mixins, or legacy pipeline changes.

## 3. Validation M0 feature set

Validation M0 covers structural semantic checks that are cheap and stable over the existing AST:

- duplicate top-level declaration names;
- duplicate `use` paths;
- duplicate record fields;
- duplicate stream fields;
- duplicate enum variants;
- duplicate shader generic parameter names;
- duplicate shader material fields;
- duplicate shader method/stage method names within a shader;
- duplicate shader implements entries;
- duplicate interface method names for AST-created interfaces;
- duplicate flow board fields for AST-created flows;
- basic type-reference validation for built-in, user-defined, generic-parameter, and array type references;
- positive array lengths;
- duplicate `let` local names in the same function statement block;
- structurally invalid for-loop iterator names if an AST contains an empty/error iterator.

Built-in type references accepted by M0 are:

- `void`
- `bool`
- `int`
- `uint`
- `float`
- `float2`
- `float3`
- `float4`
- `float4x4`
- `string`

User-defined type references are accepted from records, streams, enums, interfaces, shaders, type aliases, flows, and compile aliases already present in the module AST.

## 4. Validator API

A7 adds the validation namespace:

```csharp
Aurelian.Shaders.Language.Validation
```

The primary API is:

```csharp
public static SdslvValidationResult ValidateModule(
    SdslvModule module,
    SdslvValidationOptions? options = null);
```

A small convenience API is also present for tests and future callers that want parse diagnostics and validation diagnostics in a single result:

```csharp
public static SdslvValidationResult ValidateSource(
    string source,
    SdslvValidationOptions? options = null);
```

`SdslvValidationResult.Success` returns `false` when any diagnostic has error severity.

## 5. Diagnostic codes

A7 adds stable validation diagnostic codes:

| Code | Name | Severity | Meaning |
| ---- | ---- | -------- | ------- |
| `SV1001` | DuplicateDeclaration | Error | Duplicate top-level declaration name. |
| `SV1002` | DuplicateUse | Error | Duplicate module `use` path. |
| `SV1101` | DuplicateField | Error | Duplicate field/material/board field name in one owner. |
| `SV1201` | DuplicateEnumVariant | Error | Duplicate enum variant name in one enum. |
| `SV1301` | DuplicateGenericParameter | Error | Duplicate shader generic parameter name. |
| `SV1302` | DuplicateShaderMember | Error | Duplicate shader method/stage method/implements entry or duplicate interface method. |
| `SV1401` | UnknownType | Error | Unknown named type reference. |
| `SV1402` | InvalidArrayLength | Error | Array type reference length is zero or negative. |
| `SV1501` | DuplicateLocal | Error | Duplicate `let` local in one function statement block. |
| `SV1901` | UnsupportedValidationFeature | Warning/Error depending on context | Validation M0 encountered an unsupported or structurally invalid AST feature. |

All validation diagnostics use `SdslvDiagnosticPhase.Validation` and best-available spans. Where the current AST has no source span, validation uses `SdslvSpan.Unknown`.

## 6. Tests added

A7 adds `tests/Aurelian.Shaders.Tests/SdslvValidationM0Tests.cs` with ordinary xUnit tests for:

- valid record/stream/enum/shader module success;
- duplicate top-level declarations;
- duplicate record fields;
- duplicate stream fields;
- duplicate enum variants;
- duplicate shader generics;
- duplicate shader material fields;
- duplicate shader methods;
- unknown type references;
- invalid array lengths;
- duplicate locals in the same function scope.

Tests parse source first, assert parser success, then validate the AST directly with `SdslvValidator.ValidateModule`.

## 7. `.sdslvtest` / Oct interpreter deferral

A7 intentionally does not add `.sdslvtest` fixture files, a `.sdslvtest` runner, an Oct interpreter port, or CPU shader behavior simulation.

Future `.sdslvtest` direction:
Aurelian may support `.sdslvtest` files analogous to `.octest` by porting the Oct interpreter architecture to C# and using a CPU evaluator for deterministic shader behavior tests. This is intentionally deferred until parser/validation/emission contracts are stable.

## 8. Compatibility matrix/docs updates

Documentation now records:

- A7 validation M0 support;
- the validation diagnostic code set;
- validation M0 as structural, not full type checking;
- WyrmCoil as reference-only;
- `.sdslvtest`, `.octest`-style testing, Oct interpreter porting, and CPU shader behavior simulation as future work;
- HLSL emission over the new AST as the likely next shader milestone.

## 9. Boundary checks

Boundary checks were run against production shader source and shader tests:

```bash
rg -n "sdslvtest|octest|Oct interpreter|CpuInterpreter|Evaluator" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
git status --short
```

The source/test boundary searches returned no matches. Documentation mentions `.sdslvtest`, `.octest`, Oct, and WyrmCoil only as explicit future/reference notes. No `CodeReferences` coupling was added to production or test code.

## 10. Validation results

Validation completed successfully:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
```

Both commands passed after the validation implementation and tests were added.

## 11. Deferred features

Deferred beyond A7:

- full expression type checking;
- function return-path analysis;
- parameter duplicate-name validation;
- full generic type checking;
- full interface satisfaction;
- flow declaration parsing and complete flow validation;
- utility expression parsing and validation;
- `.sdslvtest` files and runners;
- Oct interpreter port and deterministic CPU shader behavior evaluation;
- HLSL emission over the new AST;
- DXC/SPIR-V integration;
- artifact generation over the new AST;
- renderer/assets/windowing work.

## 12. Next recommendation

A8 — HLSL emission M0 over new AST.

Parser M1 and validation M0 now establish enough AST and diagnostics shape to begin a small emission contract over the new AST. That work should remain separate from `.sdslvtest` and CPU shader behavior simulation until parser, validation, and emission contracts are stable.
