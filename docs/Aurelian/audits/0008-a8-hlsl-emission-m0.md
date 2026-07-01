# 0008 — A8 HLSL emission M0

## 1. Files changed

- `src/Aurelian.Shaders/Language/Diagnostics/SdslvDiagnosticPhase.cs`
- `src/Aurelian.Shaders/Language/Emission/Hlsl/HlslEmitter.cs`
- `src/Aurelian.Shaders/Language/Emission/Hlsl/HlslEmissionDiagnosticCodes.cs`
- `src/Aurelian.Shaders/Language/Emission/Hlsl/HlslEmissionOptions.cs`
- `src/Aurelian.Shaders/Language/Emission/Hlsl/HlslEmissionResult.cs`
- `tests/Aurelian.Shaders.Tests/Fixtures/Sdslv/smoke_triangle.sdslv`
- `tests/Aurelian.Shaders.Tests/HlslEmissionM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/sdslv-compatibility-matrix.md`
- `docs/audits/0008-a8-hlsl-emission-m0.md`

## 2. Task scope

A8 adds minimal deterministic HLSL emission over the new Aurelian SDSL-V C# parser/AST/validator path. Tests explicitly:

1. read a checked-in `.sdslv` fixture;
2. parse it with `SdslvParser.ParseModule(...)`;
3. validate it with `SdslvValidator.ValidateModule(...)`;
4. emit HLSL with `HlslEmitter.EmitModule(...)`;
5. assert that expected structs and stage functions appear in the generated HLSL.

A8 does not use or rewrite the legacy Stri-V lowerer/emitter path. It does not add DXC, SPIR-V, graphics/windowing, renderer/HAL work, asset manifests, old Stride `.sdsl` compatibility, mixins, effect/base-shader inheritance, generic specialization, full type checking, `.sdslvtest`, or an Oct CPU evaluator.

## 3. Smoke `.sdslv` fixture

The fixture is checked in at:

```text
tests/Aurelian.Shaders.Tests/Fixtures/Sdslv/smoke_triangle.sdslv
```

It contains a small readable SDSL-V module with:

- namespace `Aurelian.Smoke`;
- `VertexInput` and `VertexOutput` records;
- `SmokeTriangle` shader;
- `VSMain` stage returning `VertexOutput`;
- `PSMain` stage returning `float4`.

No syntax compromise was needed for the suggested fixture shape. The current parser accepts `stage VSMain(input: VertexInput): VertexOutput { ... }` and `stage PSMain(input: VertexOutput): float4 { ... }` directly.

## 4. HLSL emitter API

A8 adds namespace:

```csharp
Aurelian.Shaders.Language.Emission.Hlsl
```

The public API is:

```csharp
public static class HlslEmitter
{
    public static HlslEmissionResult EmitModule(SdslvModule module, HlslEmissionOptions? options = null);
}
```

`HlslEmissionResult` contains generated HLSL text and `IReadOnlyList<SdslvDiagnostic>`. Its `Success` property follows the parser/validator convention and returns `false` when any diagnostic is an error.

`HlslEmissionOptions` is intentionally empty for M0.

## 5. Emission feature set

HLSL M0 supports only the small structural surface needed by the smoke fixture plus simple nearby shapes:

- built-in type mapping for `void`, `bool`, `int`, `uint`, `float`, `float2`, `float3`, `float4`, and `float4x4`;
- user named types emitted by their final path segment;
- records and streams emitted as HLSL `struct` declarations;
- type aliases emitted as `typedef` declarations;
- enums emitted as `static const int` values;
- shader methods and stage methods emitted as top-level HLSL functions;
- material fields emitted as simple `static` declarations;
- `let`, assignment, return, expression, empty, `if`, and bounded `for` statements;
- identifier, integer, float, string, bool, array literal, field access, index, call, binary, and unary expressions.

Stage semantics such as `SV_Position` are intentionally not invented in M0 because they are not represented in the current AST fixture shape.

## 6. Unsupported constructs/diagnostics

A8 adds emission-phase diagnostics using existing `SdslvDiagnostic`:

| Code | Name | Severity | Meaning |
| ---- | ---- | -------- | ------- |
| `SV3001` | `UnsupportedDeclarationForHlslM0` | Error | Declaration shape is outside HLSL M0. |
| `SV3002` | `UnsupportedStatementForHlslM0` | Error | Statement shape is outside HLSL M0. |
| `SV3003` | `UnsupportedExpressionForHlslM0` | Error | Expression shape is outside HLSL M0. |
| `SV3004` | `UnsupportedTypeForHlslM0` | Error | Type-reference shape is outside HLSL M0. |

The emitter reports diagnostics rather than throwing for unsupported known constructs such as flow declarations, compile declarations, interfaces, with/switch/match/utility/fallibility expressions, and unrecognized future AST nodes.

## 7. Tests added

`tests/Aurelian.Shaders.Tests/HlslEmissionM0Tests.cs` adds coverage for:

- `HlslEmitter_EmitSmokeTriangleFixture_ProducesHlsl`;
- `HlslEmitter_EmitSmokeTriangleFixture_HasNoDiagnostics`;
- `HlslEmitter_EmitSmokeTriangleFixture_ContainsRecordStructs`;
- `HlslEmitter_EmitSmokeTriangleFixture_ContainsStageFunctions`;
- `HlslEmitter_UnsupportedFlowDeclaration_ReportsDiagnostic`.

The fixture is read from `tests/Aurelian.Shaders.Tests/Fixtures/Sdslv/smoke_triangle.sdslv` and is not generated inside test code.

## 8. Boundary checks

Boundary checks run for A8:

```bash
test -f tests/Aurelian.Shaders.Tests/Fixtures/Sdslv/smoke_triangle.sdslv
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.Shaders tests/Aurelian.Shaders.Tests -g '*.cs' -g '*.csproj' || true
rg -n "Mixin|EffectBlock|BaseShader|BaseCall|Compose|StageStream" src/Aurelian.Shaders/Language tests/Aurelian.Shaders.Tests -g '*.cs' || true
git status --short
```

The implementation keeps `CodeReferences/*` and `vendor/Dominatus/*` unmodified, does not couple production/test C# to WyrmCoil or old Stride concepts, and leaves legacy paths untouched.

## 9. Validation results

A8 validation commands:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
```

Both build and test pass. The full solution test run includes the new HLSL emission tests in `Aurelian.Shaders.Tests`.

## 10. Deferred features

Deferred after A8:

- DXC validation;
- SPIR-V output or execution;
- renderer/HAL integration;
- shader asset pipeline integration;
- artifact manifests for new SDSL-V outputs;
- full expression/type checking;
- interface satisfaction checking;
- generic specialization;
- flow/utility/fallibility HLSL lowering;
- `.sdslvtest`;
- Oct interpreter / CPU shader evaluator;
- old Stride `.sdsl` compatibility;
- mixins and effect/base-shader inheritance.

## 11. Next recommendation

Recommended next milestone:

```text
A9 — SDSL-V artifact manifest M0
```

Reason: HLSL text now exists, but Aurelian still needs a small manifest boundary that records source fixture/source path, parse/validation/emission diagnostics, generated HLSL text identity, and future optional tool outputs. That should come before DXC so external compiler validation has a stable place to report artifacts without entangling the emitter with renderer or asset-pipeline work.
