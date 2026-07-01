# SDSL-V Compatibility Matrix

## 1. Purpose

WyrmCoil SDSL-V remains the reference and semantic inspiration for Aurelian SDSL-V, especially for the Oct-shaped module/declaration/statement/expression/validation direction established during A2 through A10.

Aurelian SDSL-V is now its own production C# implementation under `Aurelian.Shaders.Language`. The matrix below tracks where Aurelian currently matches WyrmCoil, where it intentionally differs, where features are deferred, and where WyrmCoil concepts are not applicable to Aurelian.

This document exists to avoid accidental semantic drift while also avoiding blind copying of prototype constraints that do not fit Aurelian production needs.

## 2. Status legend

- **Compatible** — Aurelian has the same practical syntax or AST intent as WyrmCoil for the current milestone.
- **Intentionally different** — Aurelian has made a deliberate syntax, API, implementation, or scope choice.
- **Deferred** — The feature is acknowledged but not implemented in the production Aurelian path yet.
- **Not applicable** — The feature is outside Aurelian's native SDSL-V model.
- **Unknown** — Compatibility needs more investigation before a decision is recorded.

## 3. Feature matrix

| Feature | WyrmCoil SDSL-V | Aurelian SDSL-V | Status | Notes |
| ------- | --------------- | --------------- | ------ | ----- |
| File/module structure | Rust reference parser produces a module with namespace, uses, and declarations. | C# parser produces `SdslvModule` with namespace, uses, and declarations. | Compatible | Production implementation is independent C# code. |
| Namespace | `namespace Path;`. | `namespace Path;`. | Compatible | Parsed in M0. |
| Use imports | `use Path;`. | `use Path;`. | Compatible | Parsed in M0. |
| Path/type refs | Dotted paths and array type refs. | Dotted paths and `array<T, N>`. | Compatible | Parsed in M0. |
| Arrays | Array type refs and array literals. | Array type refs and M1 array literals. | Compatible | Literal syntax is `[a, b]` and `[]`. |
| Records | Record field blocks. | Record field blocks. | Compatible | Parsed in M0. |
| Streams | Stream field blocks. | Stream field blocks. | Compatible | Parsed in M0. |
| Enums | Enum declarations and variant match arms. | Enum declarations and variant match arms. | Compatible | A7 validation reports duplicate variants. |
| Interfaces | Supported in WyrmCoil reference. | AST exists, parser emits unsupported diagnostics for declarations. | Deferred | Parser M2 or validation phase should decide scope. |
| Shaders | Shader declarations. | Shader declarations. | Compatible | Parsed in M0 shell form. |
| Material fields | Shader material fields. | `material Name: Type;` and `material { ... }`. | Compatible | Parsed in M0. |
| Shader generics | Generic parameters. | Generic parameters. | Compatible | Parsed in M0. |
| Implements | Implements list. | Implements list. | Compatible | Parsed in M0. |
| Where constraints | Generic constraints. | Generic constraints. | Compatible | Parsed in M0. |
| Compile declarations | Supported in WyrmCoil reference. | Unsupported diagnostic in parser M1. | Deferred | No artifact generation over new AST yet. |
| Functions | Function declarations with bodies. | Function declarations with bodies. | Compatible | Parser shell M0, statement/expression M1. |
| Stage functions | Stage methods. | Stage methods. | Compatible | Parsed in M0 with M1 body parsing. |
| Statements | Let, assignment, return, if/else, for, expression, empty statements. | Let, assignment, return, if/else, bounded `for i in start..end step n`, expression, empty statements. | Compatible | M1 parse shape only; validation is deferred. |
| Expressions | Reference precedence/postfix/body subset plus advanced forms. | M1 precedence, postfix, arrays, with, switch, match, try/unwrap parse shapes. | Compatible | Utility expressions remain deferred. |
| Field access | `foo.bar`. | `foo.bar`. | Compatible | Chaining supported in M1. |
| Indexing | `array[index]`. | `array[index]`. | Compatible | Chaining supported in M1. |
| Calls | `callee(args)`. | `callee(args)`. | Compatible | Chaining supported in M1. |
| Binary/unary operators | Arithmetic/comparison subset in reference, with additional operators tracked over time. | `||`, `&&`, `==`, `!=`, `<`, `<=`, `>`, `>=`, `+`, `-`, `*`, `/`, `%`, unary `!`, unary `-`. | Intentionally different | Aurelian M1 hardens logical operators and modulo in the C# parser now. |
| Array literals | `[a, b]`, `[]`. | `[a, b]`, `[]`. | Compatible | Parsed in M1. |
| With expressions | Postfix `base with { Field: value }` in WyrmCoil. | Postfix `base with { Field = value; }`; colon is also accepted. | Intentionally different | Aurelian documents assignment-style updates as preferred C# production syntax. |
| Switch | `switch { case cond => value else => value }` and subject switch. | `switch x { case cond => value; else => value; }`; `->` also accepted. | Compatible | Semicolon/comma separators are accepted in Aurelian M1. |
| Match | Enum variant arms plus fallible `ok`/`err` arms in reference. | Enum variant arms, `else`, and fallible `ok`/`err` arm parse shapes. | Intentionally different | Aurelian adds an explicit else-arm AST shape for parse completeness. |
| Utility expressions | `when utility { case value when guard score score else value }` with options in reference. | AST exists; parser M1 defers utility expression parsing. | Deferred | Parser M2 should revisit syntax and options. |
| Flow declarations | Supported in WyrmCoil reference. | AST exists; parser emits unsupported diagnostics. | Deferred | Kept out of parser M1. |
| Board fields | Flow board model in reference. | AST exists only. | Deferred | No parser/validation support yet. |
| States | Flow state model in reference. | AST exists only. | Deferred | No parser/validation support yet. |
| When/goto | Flow control in reference. | AST support exists for flow statements only. | Deferred | No parser support in M1. |
| Fallibility/try/unwrap | Reference supports postfix `?`/`!` and fallible match arms. | M1 supports prefix `try expr`, prefix `unwrap expr`, and postfix `?`/`!`. | Intentionally different | Prefix forms are Aurelian's documented M1 syntax; postfix forms are retained as compatibility parse shapes. |
| Diagnostics | Reference parser/validator/emitter diagnostics. | C# lex/parse/validation/emission diagnostics returned in result objects and combined in artifact M0. | Compatible | A7 adds stable validation codes `SV1001`, `SV1002`, `SV1101`, `SV1201`, `SV1301`, `SV1302`, `SV1401`, `SV1402`, `SV1501`, and `SV1901`; A8 adds HLSL M0 emission codes `SV3001` through `SV3004` for unsupported declarations/statements/expressions/types. |
| Validation | Reference includes validation. | A7 structural validation M0 over the new AST is implemented. | Compatible | Covers duplicates, basic type-reference validity, positive array lengths, and same-scope duplicate locals. It intentionally defers full type checking, interface satisfaction, flow checking, and expression typing. |
| HLSL emission | Reference includes emission path. | New AST HLSL M0 emitter exists under `Aurelian.Shaders.Language.Emission.Hlsl`; legacy emitter remains unchanged. | Compatible | A8 emits records/streams as structs, shader methods/stages as functions, and basic let/assignment/return/expression/empty/if/for statements plus basic expressions. Unsupported constructs produce diagnostics. A10 can optionally pass emitted HLSL to DXC for external validation when DXC is available. |
| DXC/SPIR-V runner | Reference has runner/integration concepts. | Optional HLSL-only DXC validation M0 exists under `Aurelian.Shaders.Language.External.Dxc`; SPIR-V output is not implemented. | Partial | A10 discovers DXC via `AURELIAN_DXC`, `dxc`, then `dxc.exe` on `PATH`; normal tests skip cleanly if missing. The validator checks syntax/profile/entry point combinations only and does not require Vulkan SDK or persistent compiled artifacts. |
| Artifact manifests | Reference has artifact concepts. | New AST artifact manifest M0 exists under `Aurelian.Shaders.Language.Artifacts`; legacy artifact path remains unchanged. | Compatible | A9 packages source identity, SHA-256 source hash, HLSL, parse/validation/emission diagnostics, success state, and heuristic stage/profile entry point metadata. JSON includes HLSL for M0 and may split files later. Optional DXC HLSL validation exists in A10; no SPIR-V/Vulkan or asset/TOML bridge yet. |
| Test modules/runner | Reference includes rich tests and prototype `.sdslvtest` support. | xUnit parser/AST/validation/emission/artifact tests in Aurelian, including the checked-in `tests/Aurelian.Shaders.Tests/Fixtures/Sdslv/smoke_triangle.sdslv` source fixture. | Intentionally different | A9 still intentionally uses ordinary xUnit tests only; `.sdslvtest` and CPU evaluator work remain deferred. |
| mixins | Not a native WyrmCoil SDSL-V production target for Aurelian. | Not native Aurelian SDSL-V. | Not applicable | Old Stride mixins are intentionally excluded. |
| Old Stride `.sdsl` compatibility | Historical reference only. | Not supported as native Aurelian SDSL-V. | Not applicable | No old Stride effect/base-shader inheritance model. |

## 4. Aurelian syntax decisions

- **Switch syntax:** Aurelian accepts `switch { case condition => value; else => value; }` and `switch subject { case value => result; else => result; }`. The `->` arrow is accepted as a compatibility parse shape, but `=>` is the preferred expression-arm spelling.
- **Match syntax:** Aurelian accepts `match subject { Path.Variant => value; else => value; }` plus fallible `ok(binding)` and `err(binding)` arms. The explicit `else` arm is an Aurelian parse-shape addition.
- **With syntax:** Aurelian prefers assignment-style updates: `base with { Field = value; Other = value; }`. Colon updates are also accepted for WyrmCoil-shaped compatibility.
- **Utility syntax:** Utility expressions are deferred in parser M1. The existing AST records preserve a future shape for ranked cases, guards, scores, and options.
- **Fallibility syntax:** Aurelian M1 documents prefix `try expression` and `unwrap expression`; postfix `expression?` and `expression!` are parsed as compatibility shapes.
- **Validation M0:** Aurelian A7 validates structural AST invariants and type-reference names only. It is not a full shader type checker and does not evaluate expression behavior.
- **HLSL emission M0:** Aurelian A8 emits deterministic HLSL from the new AST after callers explicitly parse and validate. It covers the smoke fixture shape and reports diagnostics for unsupported constructs. A10 may optionally invoke DXC after emission for external HLSL validation only; the emitter itself still does not invoke DXC/SPIR-V or a renderer.
- **Artifact manifest M0:** Aurelian A9 packages `source -> parse -> validate -> HLSL emit -> artifact/manifest` results into deterministic in-memory artifacts and JSON. The manifest includes HLSL for M0 simplicity, while later asset work may split generated outputs into separate files. Stage/profile inference is heuristic: entry point names or parsed stage labels map to vertex/pixel/compute only by simple naming rules.
- **Optional DXC validation M0:** Aurelian A10 treats DXC as an optional external validator for generated HLSL. `AURELIAN_DXC` may point to an executable; otherwise discovery probes `dxc` and `dxc.exe` on `PATH`. Missing DXC, empty HLSL, or missing entry points return skipped validation statuses rather than failing normal tests. No SPIR-V, Vulkan, renderer, or asset/TOML path is implied.

## 5. Known intentional divergences

- Aurelian does not support old Stride mixins as a native SDSL-V feature.
- Aurelian does not support old Stride effect/base-shader inheritance as a native SDSL-V model.
- Aurelian uses C# records/classes and result objects rather than Rust enum and parser result shapes.
- Aurelian may phase parser, validation, emission, artifact, and fixture work differently from WyrmCoil. A9 validates artifact manifests through ordinary xUnit tests and a checked-in `.sdslv` fixture rather than adding `.sdslvtest`.
- Aurelian may rename or reshape APIs where C# production needs differ, while preserving compatibility notes here.

## 6. Future `.sdslvtest` direction

Aurelian may support `.sdslvtest` files analogous to `.octest` by porting the Oct interpreter architecture to C# and using a CPU evaluator for deterministic shader behavior tests. This is intentionally deferred until parser/validation/emission/artifact contracts are stable. WyrmCoil remains reference-only for this direction and is not linked into production code.

## 7. Next compatibility work

- Shader asset manifest bridge M0 after optional DXC validation boundaries are explicit.
- Future artifact manifest file output M1 for splitting generated HLSL from JSON when needed.
- Parser/validation M2 for flow, utility, and any remaining fallibility details.
- Future artifact schema revisions that may split HLSL out of JSON.
- Shader fixtures that exercise accepted Aurelian syntax and documented WyrmCoil compatibility cases.


## A40 shader compiler toolchain note

A40 does not change SDSL-V syntax or semantic compatibility. It records the compiler-facing artifact direction: Aurelian SDSL-V should lower through HLSL or Slang as MIR, then invoke DXC as a build/tool subprocess to produce SPIR-V artifacts. Direct SDSL-V -> SPIR-V generation is not planned.

`Microsoft.Direct3D.DXC` is intentionally scoped to `Aurelian.Shaders` tooling; `Aurelian.Graphics` remains DXC-free and consumes only SPIR-V bytes plus stage/entry/hash/reflection metadata. `Vortice.Dxc` remains deferred unless subprocess DXC proves insufficient.


## A41 shader artifact note

A41 does not change SDSL-V syntax, parsing, validation, or HLSL emission compatibility. It adds an HLSL-facing SPIR-V artifact layer under `Aurelian.Shaders.Language.Artifacts.Spirv`: checked-in HLSL stage fixtures can compile through the A40 DXC subprocess wrapper into typed stage artifacts, raw SPIR-V bytes, lowercase SHA-256 source/SPIR-V hashes, and deterministic JSON manifests that include `spirvBase64`.

This is intentionally not SDSL-V integration yet. The SDSL-V path remains `SDSL-V -> HLSL -> SPIR-V artifact` for future work; direct SDSL-V -> SPIR-V remains out of scope, and graphics pipeline consumption remains deferred.


## A42 SDSL-V -> HLSL -> SPIR-V artifact note

A42 connects the existing SDSL-V parser, validator, and HLSL emitter to the A41 SPIR-V shader artifact layer under `Aurelian.Shaders.Language.Artifacts.SdslvSpirv`. The artifact path records source name, lowercase UTF-8 SHA-256 source hash, generated HLSL, optional nested HLSL/SPIR-V artifact data, and diagnostics. JSON output is deterministic and retains HLSL inline for M0 traceability.

The M0 stage extractor is deliberately simple: it requires generated HLSL entry points named `VSMain` and `PSMain`, emits `vs_6_0` and `ps_6_0` stage sources, and then delegates to DXC through the A41 artifact layer. The smoke path uses temporary HLSL semantic conventions (`VertexInput.Position -> POSITION`, `VertexOutput.Position -> SV_Position`, `Color -> COLOR0`, and `PSMain` `float4` returns -> `SV_Target0`) until SDSL-V has explicit semantic metadata. Direct SDSL-V -> SPIR-V and graphics/runtime integration remain out of scope.

## A43 compiled shader contract note

A43 does not change SDSL-V syntax, parsing, validation, HLSL emission, or WyrmCoil compatibility. It adds a downstream contract bridge: successful A42 `SdslvSpirvShaderArtifact` values can be exported by `Aurelian.Shaders` into neutral `Aurelian.Rendering.Contracts.Shaders` compiled shader programs, and `Aurelian.Graphics` can map those neutral programs to Vulkan stage descriptors without referencing `Aurelian.Shaders`.

The SDSL-V compiler-facing path remains `SDSL-V -> HLSL -> DXC -> SPIR-V artifact -> neutral compiled shader contract -> graphics pipeline descriptor`. Runtime shader compilation, asset/TOML integration, and direct SDSL-V-to-Vulkan pipeline creation remain deferred.

## A44 graphics pipeline consumption note

A44 does not change SDSL-V syntax, parsing, validation, HLSL emission, artifact production, or WyrmCoil compatibility. It extends only the downstream graphics consumption side: a neutral `CompiledShaderProgram` can now become a Vulkan graphics pipeline descriptor, and optionally a native Vulkan pipeline, inside `Aurelian.Graphics`.

The SDSL-V path remains compiler-side and indirect:

```text
SDSL-V -> HLSL -> DXC -> SPIR-V artifact -> neutral compiled shader contract -> Vulkan graphics pipeline descriptor/native helper
```

There is still no runtime DXC invocation from graphics, no direct `Aurelian.Graphics`/`Aurelian.Shaders` project reference, no direct SDSL-V-to-Vulkan path, no asset/TOML bridge, and no draw or pipeline bind commands.
