# Copeland TS language profile

**Status:** canonical product-language profile (CTS-M0a, with CTS-M0b executable fixture evidence). This document supersedes the current-tense language claims in the historical M1 profile and support matrix; it does not change compiler behavior.

## Position

> Copeland TS is intended to be a TypeScript-shaped, closed-world language that preserves TypeScript’s authoring ergonomics without inheriting JavaScript’s dynamic runtime semantics.

“TypeScript-shaped” means that TypeScript 7 is the intended syntax and ecosystem reference point, not a promise of full TypeScript 7, JavaScript, npm, DOM, or arbitrary JavaScript-interop compatibility. Copeland TS owns its runtime meaning. JavaScript is the planned first usable distribution/reference backend; the C# backend is currently a proof backend and the later .NET direction is RyuJIT for ordinary managed execution, NativeAOT for supported native deployment, and .NET WebAssembly AOT for browser/Wasm.

## Reading this profile

The status vocabulary is deliberate: **Normative direction**, **Implemented**, **Partially implemented**, **Intended, unimplemented**, **Historical experiment**, **Rejected**, and **Unresolved**. “Implemented” requires current production code and tests; an artifact emitted by the C# proof backend does not establish a language law. CTS-M0b adds curated source-level fixture evidence under `tests/Copeland/Copeland.TS.Tests/Language`; those fixtures establish frontend-to-MIR acceptance or normal validation rejection without loading a backend.

## Language-law checklist

| Rule | Area | Language law | Status | Current evidence | Executable fixture evidence | JS-backend obligation |
| --- | --- | --- | --- | --- | --- | --- |
| CL-FLOW-001 | conditions | Conditions require `boolean`; JavaScript truthiness is not a Copeland TS condition model. | Implemented | `Binder.EnsureBoolean`; `if` expression check; runtime branch tests; historical M1 profile. | Accepts [boolean-condition](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/conditions/boolean-condition.cl-valid.ts); rejects [number-truthiness](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/conditions/number-truthiness.cl-invalid.ts). | Emit a boolean condition only; reject before emission otherwise. |
| CL-TYPE-001 | declarations | Variables, parameters, and returns use declared types in the current subset; `let` and `const` are supported and `var` is excluded. | Partially implemented | binder type annotation and const checks; corpus; `COPE-PROFILE-0001`. `var` is tokenized but is not parsed as a variable declaration, so its current rejection is parser recovery rather than a normal validation diagnostic. | Accepts [typed-let-const](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/declarations/typed-let-const.cl-valid.ts); rejects [missing-annotation](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/declarations/missing-annotation.cl-invalid.ts). No `var` fixture until parsing reaches the binder normally. | Direct lexical bindings only after validation. |
| CL-TYPE-002 | coercion | No implicit numeric/string/boolean coercion is intended. | Partially implemented | binder admits same-kind arithmetic/string concatenation and boolean logical operators, rejecting incompatible operands; no conversion model. | Accepts [same-kind-operators](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/declarations/same-kind-operators.cl-valid.ts); rejects [mixed-plus](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/coercions/mixed-plus.cl-invalid.ts) and [number-truthiness](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/conditions/number-truthiness.cl-invalid.ts). | Do not rely on JavaScript coercion; generate checks only if a future explicit conversion is defined. |
| CL-TYPE-003 | dynamic types | `any` is excluded from the product profile. | Rejected | historical M1 profile and support matrix list `any` as banned; current parser treats it as an unknown identifier type rather than giving a dedicated profile diagnostic. | Rejects [any-annotation](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/dynamic-types/any-annotation.cl-invalid.ts) through the current unknown-type diagnostic; it does not yet prove a dedicated `any` profile diagnostic. | Reject; never erase to JavaScript `any`. |
| CL-TYPE-004 | dynamic types | The role, if any, of TypeScript `unknown` is undecided. | Unresolved | no `unknown` type symbol, rule, or historical doctrine found. | — | Do not introduce an `unknown` lowering by implication. |
| CL-NULL-001 | absence | `null` is excluded. | Implemented | lexer/parser recognize it; binder emits `COPE-PROFILE-0005`; facade/runtime gate tests. | Rejects [null-literal](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/absence/null-literal.cl-invalid.ts). | Reject; do not emit JavaScript `null` as an ordinary value. |
| CL-NULL-002 | absence | `undefined` is excluded from the product profile. | Partially implemented | historical M1 profile bans it; it is presently an ordinary unknown identifier rather than a dedicated token/diagnostic. | Rejects [undefined-value](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/absence/undefined-value.cl-invalid.ts) through the current unknown-name diagnostic; it does not yet prove a dedicated `undefined` diagnostic. | Reject; never use `undefined` as absence representation. |
| CL-NULL-003 | optionality | Optionality and failure should be explicit values, with payload enums as the planned language machinery; no ambient nullability. | Normative direction | historical M1 profile recommends an explicit option type; payload enums and exhaustive `match` exist; no canonical `Option` or optional-property design exists. | No fixture: explicit optionality representation remains unresolved. | Use an explicit tagged representation once specified; no `null`/`undefined` sentinel. |
| CL-EQ-001 | equality | Equality operators are currently accepted only for operands with matching type names; the semantic distinction between `==`/`===` and identity/value equality is not yet a product law. | Partially implemented | binder accepts all four equality spellings for equal names; MIR preserves spellings; no explicit equality doctrine or targeted runtime tests. | No language fixture while equality semantics remain unresolved. | Do not assume JavaScript loose/strict equality meanings; hold lowering decision open. |
| CL-NUM-001 | numbers | The current `number` implementation is represented as C# `double`; the normative numeric kinds remain undecided. | Partially implemented | `PrimitiveTypeSymbol.Number`, C# proof backend, runtime tests returning `double`; historical profile names only `number`. | No language fixture while numeric-kind law remains unresolved. | A direct JS number may be provisional only; preserve any later numeric-kind distinction. |
| CL-NUM-002 | conversions | Numeric kinds, explicit conversions, integer behavior, NaN/infinity, and overflow are unresolved. | Unresolved | no type kinds other than `number`, conversion syntax, overflow rule, or tests found. | — | Do not select JavaScript number behavior as language law. |
| CL-OBJECT-001 | objects | JavaScript object literals and ordinary member access are outside the current subset. | Implemented | binder rejects object literals (`COPE-TYPE-0011`) and member access except enum cases (`COPE-TYPE-0012`); historical M1 profile defers them. | Existing focused and corpus evidence; no CTS-M0b fixture was added. | Reject ordinary property emission. |
| CL-OBJECT-002 | prototypes | JavaScript prototype behavior, mutation, and dynamic property addition are excluded from the intended closed-world model. | Normative direction | historical profile bans JS object/prototype semantics; support matrix names prototype mutation/dynamic addition as initial bans. | No fixture: the concrete source-law surface is not specified. | Must not expose normal prototype-chain semantics. |
| CL-OBJECT-003 | classes | A strict nominal class/inheritance model is intended, but its member, constructor, `this`, inheritance, and object-identity laws are not yet defined. | Intended, unimplemented | historical support matrix plans a strict class subset; no class syntax/type/MIR support. | No fixture: a class profile has not been defined. | Reject until a class profile defines representation and dispatch. |
| CL-ARRAY-001 | arrays | Homogeneous `T[]` literals and values are supported. | Implemented | array type/binder/lowering; C# proof runtime test; corpus. | Accepts [homogeneous-array](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/arrays/homogeneous-array.cl-valid.ts). Mixed and empty-array rejection remain corpus/focused-test evidence. | Emit a representation preserving element type; no implicit widening. |
| CL-ARRAY-002 | arrays | Indexing, mutation, length, bounds behavior, and out-of-range behavior are unresolved. | Unresolved | no array index/member MIR node or language tests. | — | Do not inherit JavaScript sparse-array or out-of-range semantics. |
| CL-CALL-001 | functions | Named functions, typed calls, returns, and argument checking are supported. | Implemented | binder/lowering/MIR/C# runtime tests and corpus. | Accepts [typed-named-call](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/functions/typed-named-call.cl-valid.ts); rejects [wrong-arity](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/functions/wrong-arity.cl-invalid.ts) and [wrong-argument-type](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/functions/wrong-argument-type.cl-invalid.ts). | Direct calls are safe only after the same validation. |
| CL-CALL-002 | closures | Function values, lambdas, closures, capture lifetime, and captured-state mutation are unresolved. | Unresolved | historical matrix defers arrows/delegates and says capture rules need definition; no production representation. | — | No closure lowering or capture emulation yet. |
| CL-FLOW-002 | order | Evaluation order and side-effect sequencing are unresolved as a language contract. | Unresolved | current tree/lowering order is implementation detail; no stated law or proof. | — | Do not allow backend reordering to become the accidental specification. |
| CL-FAIL-001 | failure | Fallible signatures (`! ErrorType`) and propagation (`?`) are explicit language constructs; unhandled fallible calls are rejected. | Implemented | binder diagnostics `COPE-TYPE-0013`–`0016`, MIR, C# proof/runtime tests, historical profile. | Accepts [fallible-propagation](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/fallibility/fallible-propagation.cl-valid.ts); rejects [unhandled-fallible-call](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/fallibility/unhandled-fallible-call.cl-invalid.ts) and [wrong-error-propagation](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/fallibility/wrong-error-propagation.cl-invalid.ts). | Preserve tagged success/failure flow; do not substitute exceptions. |
| CL-FAIL-002 | exceptions | Explicit failure values are the intended alternative to exception-driven flow; source `throw`/catch semantics and construction of an error result remain unresolved. | Partially implemented | historical matrix states exception-driven flow -> fallible model; current language proves successful fallible paths/propagation but has no source failure constructor or exception syntax contract. | No fixture: error-result construction and `throw`/catch semantics remain unresolved. | No implicit JavaScript exception semantics; defer concrete representation. |
| CL-ENUM-001 | tagged data | Nominal payload enums and exhaustive `match` are supported and are the established tagged-data mechanism. | Implemented | enum/match binder rules, MIR nodes, C# proof/runtime tests, M1 corpus. | Accepts [payload-enum-construction](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/tagged-data/payload-enum-construction.cl-valid.ts) and [payload-enum-match](../../../tests/Copeland/Copeland.TS.Tests/Language/Valid/tagged-data/payload-enum-match.cl-valid.ts); rejects [nonexhaustive-match](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/tagged-data/nonexhaustive-match.cl-invalid.ts) and [payload-pattern-arity](../../../tests/Copeland/Copeland.TS.Tests/Language/Invalid/tagged-data/payload-pattern-arity.cl-invalid.ts). | Preserve nominal tag and payload fields; do not lower to untagged JS objects. |
| CL-MODULE-001 | modules | Module/import/export semantics are intended but unresolved. | Unresolved | historical matrix plans minimal modules; no parser, binder, CLI project model, or current law. | — | Do not imply ES-module/npm compatibility. |
| CL-INTEROP-001 | host boundary | JavaScript/host interop, if offered, must cross an explicit controlled unsafe/dynamic boundary. | Normative direction | CTS-M0a product direction and closed-world doctrine; no implementation or syntax. | No fixture: interop syntax and boundary semantics are not defined. | Reject implicit host access; future lowering must visibly mark and contain the boundary. |
| CL-MIR-001 | semantic boundary | Cope MIR is the current frontend-to-backend semantic boundary for the Copeland TS lane, not a universal IR or source language. | Implemented | project graph, `MirProgram` model, CLI `--emit mir`, topology doctrine, MIR corpus. | The CTS-M0b runner itself proves diagnostic-gated lowering for every fixture; no MIR artifact belongs under `Language`. | JavaScript backend consumes validated Cope MIR; it must not reintroduce frontend semantics. |

## JavaScript-backend readiness

This is a readiness view, not a lowering design. “Unresolved” means no backend action is authorized by this document.

| Rule/feature | Direct JavaScript emission safe? | Requires generated enforcement? | Requires runtime support? | Must reject? | Unresolved? |
| --- | ---: | ---: | ---: | ---: | ---: |
| Boolean conditions (CL-FLOW-001) | Yes | No | No | No | No |
| Typed declarations and `const` (CL-TYPE-001) | Yes | Validation only | No | No | No |
| No implicit coercion (CL-TYPE-002) | No | Yes | Possibly | No | No |
| `any` / `undefined` (CL-TYPE-003, CL-NULL-002) | No | No | No | Yes | No |
| `unknown` (CL-TYPE-004) | No | No | No | No | Yes |
| `null` exclusion (CL-NULL-001) | No | No | No | Yes | No |
| Explicit optionality representation (CL-NULL-003) | No | No | No | No | Yes |
| Equality (CL-EQ-001) | No | Yes | Possibly | No | Yes |
| Numbers/conversions/overflow (CL-NUM-001/002) | No | Yes | Possibly | No | Yes |
| Objects/prototypes/classes (CL-OBJECT-001–003) | No | Yes | Likely | current objects yes | Yes |
| Array literal core (CL-ARRAY-001) | Yes | Validation only | No | No | No |
| Array indexing/bounds (CL-ARRAY-002) | No | Yes | Likely | No | Yes |
| Named calls (CL-CALL-001) | Yes | Validation only | No | No | No |
| Closures (CL-CALL-002) | No | Yes | Possibly | No | Yes |
| Evaluation order (CL-FLOW-002) | No | Possibly | No | No | Yes |
| Fallibility and propagation (CL-FAIL-001) | No | Yes | Yes | No | No |
| Explicit failure / exceptions (CL-FAIL-002) | No | Yes | Yes | `throw` pending | Yes |
| Payload enums and `match` (CL-ENUM-001) | No | Yes | Possibly | No | No |
| Modules (CL-MODULE-001) | No | Yes | Possibly | No | Yes |
| Host interop (CL-INTEROP-001) | No | Yes | Likely | implicit access yes | Yes |
| Cope MIR boundary (CL-MIR-001) | No | No | No | No | No |

## Current implementation boundary

The implemented path is source text -> syntax -> binder -> Cope MIR -> deterministic `.cope` text or C# proof emission. The CLI accepts `compile <source> --emit mir|csharp`; it does not emit JavaScript. `.cope` is an expected textual projection of MIR, not source input or a parser contract. The C# backend proves selected behavior but is not authority for JavaScript semantics.

## Explicit exclusions and planned work

Current exclusions include `var`, `eval`, `null`, ordinary object literals/member access, ternary `?:`, optional chaining `?.`, and implicit global assignment. The language direction also excludes truthiness, implicit coercion, `any`, ambient nullability, prototype behavior, and implicit host access. Payload enums and explicit fallibility are the intended native alternatives for tagged alternatives and failure; they do not yet settle all optionality or error-construction details.

## Unresolved decision surface

Before a construct is admitted or lowered, decide: `unknown`; equality spelling and identity/value rules; numeric kinds/conversions/overflow/non-finite values; canonical optional representation; class/object/identity/inheritance and member rules; arrays and bounds; closures/captures; evaluation order; source failure construction and exception policy; modules; and the explicit JavaScript interop boundary. These are decisions, not TODOs for a backend to choose opportunistically.

## CTS-M0b fixture contract

`TestData/Corpus` remains compiler-regression and generated-artifact evidence. `Language` is curated language-law evidence:

```text
tests/Copeland/Copeland.TS.Tests/Language/
  Valid/<semantic-area>/*.cl-valid.ts
  Invalid/<semantic-area>/*.cl-invalid.ts
```

- A valid fixture must parse, bind, validate, and lower to Cope MIR without diagnostics.
- An invalid fixture must be rejected by the intended language-validation stage. A crash, parser accident, backend failure, or unrelated diagnostic is not a correct rejection.
- Add exact diagnostic expectations only when diagnostic identity is itself contractual; add runtime companions only for observable execution semantics.
- No test DSL is needed merely to classify a source file as valid or invalid.

CTS-M0b currently contains 8 valid and 12 invalid fixtures. The `var` law has no invalid fixture because the current parser does not carry `var` declarations through to binder validation; its parser-recovery rejection is intentionally not treated as language-contract evidence. Explicit optionality also remains unresolved: payload-enum fixtures establish tagged-data behavior, not a canonical `Option` representation or any JavaScript representation.

Historical sources and the detailed audit are recorded in [CTS-M0a doctrine audit](../../migrations/cts-m0a-copeland-ts-language-doctrine-audit.md).
