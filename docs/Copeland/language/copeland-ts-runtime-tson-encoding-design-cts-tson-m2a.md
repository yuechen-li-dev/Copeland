# Copeland TS runtime canonical TSON encoding design (CTS-TSON-M2a)

> **Sidecar routing:** runtime encoding remains encode-only here. [CTS-SIDECAR-M0a](copeland-ts-sidecar-interop-design-cts-sidecar-m0a.md) requires a future bounded generated schema-directed decoder for transport, not a public runtime `TsonValue` or general authored-document parser.

**Status:** accepted documentation-only architecture milestone. Its decisions are implemented by [CTS-TSON-M2b](../architecture/copeland-ts-runtime-tson-encoding-cts-tson-m2b.md); M2a itself introduced no runtime encoding API.

## Executive decision

M2b should add one reserved compiler intrinsic:

```ts
const encoded: string ! TsonEncodeError = tsonEncode(value);
```

The input must have a statically known, same-compilation-unit nominal record or payload-enum type whose complete reachable schema contains only Boolean, Number, String, Record, and Enum. The result is canonical Unicode text ending in one LF. It is Result-valued because a runtime string can violate TSON's Unicode-scalar law or the fixed output limits. `TsonEncodeError` has exactly two zero-payload cases: `InvalidUnicode` and `OutputLimitExceeded`.

The frontend builds one immutable, validated, demand-driven `MirTsonEncodingPlan` per eligible root type. A dedicated `MirTsonEncodeExpression` evaluates its operand once and refers to the plan by ID. The plan carries the source `$schema`, stable nominal identities, complete reachable declarations, declaration-ordered fields/cases/payloads, canonical definition order, carrier access keys, and fixed limits. Both backends consume that same order and emit closed type-specific writer functions inside their existing generated scope. They do not inspect host objects, reconstruct schema facts, build runtime `TsonValue`, or parse TSON.

M2b should return `string` only. BOM-free UTF-8 bytes remain a distinct future operation. The writer nevertheless counts canonical UTF-8 bytes incrementally so C# and JavaScript enforce the same one-mebibyte output bound without first allocating the byte output.

## Audited revision and evidence

This audit began from clean revision `95ec9f8b12a14e6b5f7292cfc93df64440f15cda` on branch `main`, tracking `origin/main` with no divergence.

### Implemented TSON document path

- [`TsonValues.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonValues.cs) defines compiler-host `TsonValue`, `TsonBoolean`, `TsonNumber`, `TsonString`, `TsonObject`, `TsonRecord`, `TsonEnum`, and `TsonField`. `TsonNumber` normalizes every NaN to `7FF8000000000000`; `TsonString` rejects isolated UTF-16 surrogates.
- [`TsonSchema.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonSchema.cs) defines `TsonTypeReference`, record/enum definitions, and single-schema `TsonCatalog` with ordered definitions and collision checks.
- [`TsonDocument.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonDocument.cs) defines `ObjectTypeScript` and `CanonicalTson`, `TsonLimits`, diagnostics, document, and read result. Defaults are source length 1,048,576 UTF-16 code units; nesting 64; declarations 256; fields 256; enum cases 256; payloads 64; nodes 100,000; and string length 262,144 UTF-16 code units.
- [`TsonDocumentReader.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonDocumentReader.cs) uses the production parser, applies restriction/schema/value/limit checks, rejects schema cycles, derives stable identities, and compares canonical input to printer output. It sorts catalog definitions by ordinal name.
- [`TsonCanonicalPrinter.cs`](../../../src/Copeland/Copeland.TS/Tson/TsonCanonicalPrinter.cs) emits `$schema`, ordinal-name-sorted declarations, declaration-ordered members, four-space indentation, LF, one final LF, `$number("XXXXXXXXXXXXXXXX")`, uppercase `\uXXXX` controls, and raw valid Unicode scalars. `PrintUtf8` uses BOM-free `Encoding.UTF8` bytes.
- [`TsonFeatureTests.Both_profiles_share_parser_and_round_trip_semantics_and_bytes`](../../../tests/Copeland/Copeland.TS.Tests/TsonFeatureTests.cs), `Canonical_binary64_form_preserves_complete_categories_and_normalizes_nan`, `Every_resource_limit_has_a_tson_diagnostic`, and `Unicode_surrogate_escape_pair_projects_to_one_scalar_and_prints_stably` prove the laws. [`TsonFixtureTests.Valid_filesystem_fixture_round_trips`](../../../tests/Copeland/Copeland.TS.Tests/TsonFixtureTests.cs) owns the filesystem round trip.

Current compile-time ingestion is:

```text
.obj.ts/.tson asset
  -> ICopelandAssetSource + compiler resolver
  -> production parser + TsonDocumentReader
  -> exact stable-schema validation
  -> BoundLiteral/BoundRecordConstruction/BoundEnumValue
  -> existing MIR
  -> ordinary generated C#/JavaScript value
```

[`CopelandAssets.cs`](../../../src/Copeland/Copeland.TS/Compiler/CopelandAssets.cs) defines `ICopelandAssetSource`, normalized dependency identity, SHA-256 evidence, and root-bounded resolution. [`Binder.cs`](../../../src/Copeland/Copeland.TS/Semantics/Binder.cs) recognizes `$schema` and `tsonAsset`, attaches `StableIdentity` to record/enum symbols, validates the reachable catalog, then expands values into existing bound nodes. [`TsonAssetFeatureTests.ObjectTypeScript_asset_expands_to_existing_bound_and_mir_nodes`](../../../tests/Copeland/Copeland.TS.Tests/TsonAssetFeatureTests.cs) and [`TsonAssetRuntimeTests.Both_backends_execute_compiled_asset_with_exact_repeated_parity`](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/TsonAssetRuntimeTests.cs) prove that TSON metadata disappears before current MIR and runtime.

[`CliIntegrationTests.Compile_resolves_Tson_asset_relative_to_source_for_every_emit_target`](../../../tests/Copeland/Copeland.Cli.Tests/CliIntegrationTests.cs) proves CLI composition for MIR, C#, and JavaScript; `Failed_Tson_asset_compilation_preserves_stale_output` proves that a failed asset compilation does not replace the prior artifact. The checked-in M1b corpus under [`TsonAssets/Corpus/record`](../../../tests/Copeland/Copeland.TS.Tests/TsonAssets/Corpus/record) pins source, `.cope`, C#, and JavaScript evidence.

### Implemented nominal and fallibility path

[`Types.cs`](../../../src/Copeland/Copeland.TS/Semantics/Types.cs) retains `StableIdentity` on `RecordTypeSymbol` and `EnumTypeSymbol`; [`Symbols.cs`](../../../src/Copeland/Copeland.TS/Semantics/Symbols.cs) retains declaration-ordered fields, cases, and payloads. [`BoundNodes.cs`](../../../src/Copeland/Copeland.TS/Semantics/Bound/BoundNodes.cs) contains primitive, record, enum, Result, propagation, unwrap, and typed try/except expressions.

Current [`MirRecordDefinition` and `MirEnum`](../../../src/Copeland/Copeland.TS.Mir/MirNodes.cs) retain source names, order, nested `MirType`, and transient record/field keys, but no `$schema` or stable exchange identity. [`MirLowerer`](../../../src/Copeland/Copeland.TS/Lowering/MirLowerer.cs) therefore cannot currently deliver enough information for runtime canonical encoding. [`MirValidator`](../../../src/Copeland/Copeland.TS.Mir/MirValidator.cs) and [`MirTextWriter`](../../../src/Copeland/Copeland.TS.Mir/MirTextWriter.cs) know no TSON expression or plan.

The C# backend emits sealed record carrier classes with internal complete constructors/getters and abstract enum records with sealed nested cases. The JavaScript backend emits closure-private record `Symbol` tokens and field slots, frozen null-prototype records, private enum tokens, and frozen `$tag`/`$payload` carriers. [`RecordRuntimeTests.Same_shaped_records_have_distinct_generated_nominal_types`](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/RecordRuntimeTests.cs), [`JavaScriptRuntimeTests.Node_Proves_Record_Nominality_Immutability_And_Representation_Isolation`](../../../tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/JavaScriptRuntimeTests.cs), and `Node_Executes_Payload_Enum_Match_Repeatedly` prove those private layouts.

Existing Result helpers are emitted on demand. Propagation, unwrap, and typed `try`/`except` are validated in MIR; host exceptions and invariant panics are not ordinary typed failures. [`ResultBackendParityTests.JavaScript_And_CSharp_Observe_The_Same_Typed_Try_Except_Behavior`](../../../tests/Copeland/Copeland.TS.Backend.CSharp.Tests/Runtime/ResultBackendParityTests.cs) is the parity evidence.

The CLI composes frontend, BCL-only MIR, and MIR-only backends as recorded by the [compiler topology](../architecture/copeland-ts-compiler-topology-jtf-m6c.md). Generated programs do not reference the compiler assembly. No runtime-parser, reflection, trimming, or NativeAOT dependency is introduced by this design; generated closed code preserves the existing NativeAOT-compatible posture without claiming a publish proof.

## Operation boundary

| Operation | Status after M2a |
| --- | --- |
| A. `.tson`/`.obj.ts` to compiled nominal value | Implemented by M1b; unchanged. |
| B. Compiled value to runtime TSON representation | Private writer events only; no public operation. |
| C. Compiled value to canonical TSON | Designed here; implement bounded text encoding in M2b. |
| D. Canonical TSON to compiled value | Deferred; no cross-backend runtime parser. |
| E. TSON to JSON/other compatibility form | Deferred; never bypass TSON semantics. |

## Exact source API

`tsonEncode(expression)` is a reserved compiler intrinsic, not an ordinary library member. It may appear wherever an expression of type `string ! TsonEncodeError` is legal: local initializer, argument, return, conditional branch, match arm, Result handling, propagation, unwrap, or protected `try` value. No result annotation is required; the argument's already-bound static type selects the plan and the result type is known.

The argument root must be exactly one nominal record or payload enum declared in the same compilation unit as the call and `$schema`. Primitive roots are deferred to avoid presenting a schema-less-looking universal serializer; primitives are supported only when reached through that nominal root. Unsupported roots or reachable types are compile-time errors.

The name cannot be declared, shadowed, or taken as a first-class value. A call has exactly one argument. Generic declarations/calls do not exist today, so generic wrappers are unsupported rather than specially designed. Ordinary nongeneric wrappers may accept a concrete eligible nominal type and call the intrinsic. The operand is evaluated exactly once each time execution reaches the expression, and encoding runs each time; plan/helper reuse never memoizes values or text.

`Tson.encode` is rejected for the initial slice because there is no runtime TSON namespace/value. `tsonEncodeText` is unnecessarily redundant while bytes are absent. The short verb remains precise because the only implemented output is canonical TSON text and the argument restrictions are statically enforced.

The proposed runtime path is:

```text
ordinary compiled nominal value
  -> evaluate once at MirTsonEncodeExpression
  -> generated root-specific projector
  -> generated bounded canonical writer
  -> Result containing canonical TSON string or TsonEncodeError
```

## String output and Result model

| Choice | Benefit | Cost | Decision |
| --- | --- | --- | --- |
| Canonical `string` | Natural on both backends; no array reopening | BOM is inapplicable until encoding | Implement in M2b. |
| BOM-free UTF-8 bytes | Direct wire artifact | No accepted portable byte-array API; reopens arrays | Accepted but deferred as `tsonEncodeUtf8`. |
| Both initially | Convenience | Doubles API/limits before bytes have a language carrier | Rejected for M2b. |

The returned string contains the exact canonical scalar sequence, uses LF only, and ends in exactly one LF. “No BOM” is a rule for the future strict UTF-8 transform: encode the returned scalar sequence as UTF-8 without a preamble or replacement fallback.

| Fallibility choice | Consequence | Decision |
| --- | --- | --- |
| Infallible | Cannot honestly represent bounded output or current UTF-16 string domain | Rejected. |
| Always `Result<string, TsonEncodeError>` | Composes with implemented propagation/try/except and is backend-neutral | Implement. |
| Host exception | Backend text/type would leak into language semantics | Rejected. |

The compiler supplies a reserved zero-payload enum conceptually equivalent to:

```ts
enum TsonEncodeError {
    InvalidUnicode,
    OutputLimitExceeded,
}
```

Unsupported type, missing schema, cycles, collisions, and cross-schema dependencies are compile-time diagnostics. Invalid Unicode and any runtime string/output bound failure return `err`. A counterfeit record carrier, impossible enum tag, malformed plan, or missing generated helper is a terminal backend invariant failure. No host exception message becomes language data.

M2b bakes fixed limits into the plan: maximum canonical UTF-8 output 1,048,576 bytes and maximum individual string length 262,144 UTF-16 code units. Static schema analysis applies the existing declaration/member/nesting/node maxima at compile time. With no arrays or recursive types, aggregate depth and non-string value-node count are fixed by the type graph; only string validity and size remain runtime-variable. Configurable compiler options and source parameters are deferred.

## Stable identity retention and MIR plan

The frontend must create plans before stable identities disappear:

```text
bound tsonEncode(value) + frontend symbols + $schema
  -> shared reachability/identity analysis
  -> validated MirTsonEncodingPlan in Copeland.TS.Mir
  -> MirTsonEncodeExpression(value, planId)
  -> unchanged plan consumed by C# and JavaScript backends
```

The smallest backend-neutral addition belongs in `Copeland.TS.Mir`:

```text
MirProgram.TsonEncodingPlans: ordered plans
MirTsonEncodingPlan
  Id, SchemaIdentity, RootType, Definitions, Limits
MirTsonRecordPlan
  MirRecordTypeId, Name, StableIdentity, ordered fields
MirTsonEnumPlan
  Mir enum key, Name, StableIdentity, ordered cases/payloads
MirTsonValuePlan
  Boolean | Number | String | Record(plan type key) | Enum(plan type key)
MirTsonEncodeExpression
  Operand, PlanId, Result<string, TsonEncodeError>
```

Record fields retain their `MirRecordFieldId`; enum cases retain name and payload ordinal. These are carrier access keys only, never serialized identities. Each plan stores canonical stable identities `schema#Type`, `schema#Type.field`, `schema#Enum.Case`, and `schema#Enum.Case.payload` and prevalidated static schema fragments.

`MirValidator` must reject duplicate plan IDs, unknown roots/carrier keys, unsupported types, missing/malformed identities, identity/name collisions, cross-schema references, noncanonical definition ordering, mismatched declaration order/types, cycles, nonpositive/excess limits, and encode expressions whose operand/result does not match the plan. `MirTextWriter` must render plans and `tson-encode [plan] operand` deterministically so corpus review can inspect the contract.

Plans are deduplicated by the static root nominal identity. They are created only for reached intrinsic calls and emitted in deterministic first-use plan-ID order; their internal declarations have an independent canonical ordering. Repeated calls reuse the plan/helper while preserving operand evaluation and encoding on every execution.

| Compiler-host `TsonValue` | Generated encoding plan |
| --- | --- |
| Runtime object graph in the compiler process | Immutable compile-time backend contract |
| Contains one concrete semantic value | Describes how to encode any value of one closed root type |
| Lives in frontend `Copeland.TS` | Lives in BCL-only `Copeland.TS.Mir` |
| Used by reader/printer | Used by both generated backends |
| Includes structural `TsonObject` | Excludes document-only structural objects |

## Reachability, cycles, and ordering

Starting at the nominal root, the frontend includes the root and recursively follows every record field and every payload of every case in a reachable enum. It rejects any non-Boolean/Number/String/Record/Enum child, any nominal outside the call's compilation unit or `$schema`, any schema cycle, missing stable identity, or identity/name collision. Repeated nominal types are deduplicated. Unused declarations are omitted.

The final catalog order is ordinal lexical order by nominal declaration name, exactly matching `TsonDocumentReader` and `TsonCanonicalPrinter`. Fields, cases, and payloads retain source declaration order. This is not dependency-topological order; cycles are rejected and forward type names are valid in the catalog. Source declaration reordering therefore does not alter identity or output. First-use traversal is used only to discover the set, never to order canonical text.

Recursive nominal schemas are compile-time errors. Eligible immutable values therefore cannot contain runtime cycles. Host reference aliases and repeated equal subvalues are not semantic; both occurrences encode independently as tree nodes. No visited-object table is emitted.

## Canonical document envelope

Every successful call emits one complete reachable single-schema catalog and exactly one `$value`:

```ts
const $schema: string = "copeland://example/settings";

enum Mode {
    Auto,
    Manual(label: string),
}

record Settings {
    enabled: boolean;
    mode: Mode;
}

const $value = $record.Settings({
    "enabled": true,
    "mode": Mode.Auto,
});
```

The exact form is the current M0b printer contract, not a new grammar: four spaces, LF, blank lines between envelope sections, one final LF, uppercase 16-digit number bits, current string escapes, record field names as canonical strings, and trailing record-field/enum-declaration commas. A zero-field record prints `record Empty {\n}` and `$record.Empty({})`. A zero-payload enum case prints `Case,` in its declaration and `Enum.Case` as its value.

For every successful result `text`, `TsonDocumentReader.ReadSelfDescribed(text, CanonicalTson)` must succeed, and reprinting that document must return the identical string and BOM-free UTF-8 byte sequence. This existing reader/printer pair is the conformance oracle for M2b fixtures.

Only same-compilation-unit declarations sharing the exact `$schema` are supported. Two same-named types from different schemas cannot enter one M2b plan. Cross-schema and imported roots are deferred until module/schema composition has deterministic identity, collision, and version laws.

| Same-schema root | Cross-schema root |
| --- | --- |
| Existing `$schema` and same-unit identity law is complete | Imports/modules and catalog composition do not exist |
| One catalog, no qualification ambiguity | Name collisions and version ownership unresolved |
| Implement in M2b | Defer |

## Writer architecture

| Architecture | Allocations/size | Parity and access | Decision |
| --- | --- | --- | --- |
| Construct runtime `TsonValue`, then print | DOM allocation and compiler-model duplication | No cross-backend runtime model | Rejected. |
| Public runtime representation/projector | Useful only if users inspect TSON values | Invents operation B without demand | Rejected. |
| Type-specific projector into small writer | Shared backend-local mechanics; closed direct access | Plan fixes events/order; parity is testable | Selected. |
| Monolithic direct concatenation | Can specialize static text | Duplicates limits/escaping across every plan | Rejected. |

This is generated specialization: each plan supplies static schema fragments and closed value functions; one demand-emitted backend-local writer owns escaping, binary64 bits, indentation, byte accounting, and Result completion. There is no runtime package in M2b. A package may graduate only after multiple consumers prove a stable helper ABI and JavaScript ownership remains explicit.

### C# generated flow

```text
MirTsonEncodeExpression
  -> evaluate typed operand once
  -> plan-specific __WriteSettings
  -> direct internal getters / sealed-case switch
  -> __TsonWriter (StringBuilder + UTF-8 count)
  -> CopeResult<string,TsonEncodeError>
```

Illustrative generated C# (not current production output):

```csharp
private static CopeResult<string, TsonEncodeError> __EncodeSettings(
    __CopeRecord_r0 value)
{
    var writer = new __TsonWriter(1_048_576, 262_144);
    if (!writer.Static(__SettingsSchemaPrefix)
        || !__WriteSettingsValue(writer, value)
        || !writer.Static(";\n"))
    {
        return CopeResult<string, TsonEncodeError>.Err(
            new TsonEncodeError.OutputLimitExceeded());
    }

    return writer.Finish();
}

private static bool __WriteSettingsValue(
    __TsonWriter writer,
    __CopeRecord_r0 value)
{
    return writer.Static("$record.Settings({\n    \"name\": ")
        && writer.String(value.__cope_record_field_r0_f0)
        && writer.Static(",\n    \"mode\": ")
        && __WriteMode(writer, value.__cope_record_field_r0_f1)
        && writer.Static(",\n})");
}

private static bool __WriteMode(__TsonWriter writer, Mode value)
{
    switch (value)
    {
        case Mode.Auto:
            return writer.Static("Mode.Auto");
        case Mode.Manual manual:
            return writer.Static("Mode.Manual(\n        ")
                && writer.String(manual.Label)
                && writer.Static("\n    )");
        default:
            throw new global::System.InvalidOperationException(
                "Copeland C# backend invariant failure.");
    }
}
```

The actual helper uses explicit branches so it can distinguish `InvalidUnicode` from a byte/length overflow. It accesses internal record getters and nested case properties directly. `System.Text.StringBuilder`, `BitConverter.DoubleToUInt64Bits`, invariant uppercase hex nibbles, and integer counters are NativeAOT-friendly and reflection-free. Static fragments are pre-counted by the plan.

### JavaScript generated flow

```text
MirTsonEncodeExpression
  -> evaluate typed operand once
  -> plan-specific __writeSettings in existing closure
  -> private Symbol slots / private type token + closed tag switch
  -> __tsonWriter (parts + UTF-8 count)
  -> private Result carrier
```

Illustrative generated JavaScript:

```javascript
function __encodeSettings(value) {
    __requireSettings(value);
    const writer = __makeTsonWriter(1048576, 262144);
    if (!writer.static(__settingsSchemaPrefix)
        || !__writeSettingsValue(writer, value)
        || !writer.static(";\n")) {
        return __makeResult(__encodeResultType, "err", [__outputLimit]);
    }
    return writer.finish();
}

function __writeSettingsValue(writer, value) {
    return writer.static("$record.Settings({\n    \"name\": ")
        && writer.string(value[__settingsName])
        && writer.static(",\n    \"mode\": ")
        && __writeMode(writer, value[__settingsMode])
        && writer.static(",\n})");
}

function __writeMode(writer, value) {
    __requireMode(value);
    switch (value.$tag) {
        case "Auto":
            return writer.static("Mode.Auto");
        case "Manual":
            return writer.static("Mode.Manual(\n        ")
                && writer.string(value.$payload[0])
                && writer.static("\n    )");
        default:
            __panic();
    }
}
```

The encoder is emitted inside the same lexical scope as `__settingsName`, record validators/constructors, enum type tokens, and `$payload`. Tokens remain private. There is no `Object.keys`, `for...in`, `Object.getOwnPropertySymbols` schema discovery, prototype dispatch, arbitrary traversal, or `JSON.stringify`. Existing validators may establish carrier invariants; failures panic rather than become ordinary encode errors.

| C# helper | JavaScript helper |
| --- | --- |
| `StringBuilder`; direct internal members | Parts array; closure-private Symbols/tokens |
| `BitConverter.DoubleToUInt64Bits` | Reused 8-byte `ArrayBuffer` + `DataView` |
| Sealed-case type switch | Validated closed `$tag` switch |
| Generated once when demanded | Generated once when demanded |
| No runtime/compiler package | Standalone browser-compatible source |

## Binary64 law

Numbers always print as `$number("16 UPPERCASE HEXADECIMAL BITS")`. The mapping is:

| Value | Canonical bits |
| --- | --- |
| `+0` | `0000000000000000` |
| `-0` | `8000000000000000` |
| smallest positive subnormal | `0000000000000001` |
| maximum finite | `7FEFFFFFFFFFFFFF` |
| any NaN sign/payload | `7FF8000000000000` |
| `+Infinity` | `7FF0000000000000` |
| `-Infinity` | `FFF0000000000000` |

All other finite values retain their exact IEEE 754 binary64 bits. C# extracts a `ulong` with `BitConverter.DoubleToUInt64Bits`. JavaScript writes the number with `DataView.setFloat64(0, value, false)` and reads two big-endian `Uint32` words; explicit endianness makes host endianness irrelevant. Both detect exponent-all-ones/nonzero-fraction and replace the bits with canonical NaN. A 16-step uppercase nibble table avoids culture and decimal formatting. The fixed buffer and char emission avoid per-number byte arrays.

[`CSharpLiteralWriter.cs`](../../../src/Copeland/Copeland.TS.Backend.CSharp/CSharp/CSharpLiteralWriter.cs) currently preserves general runtime literal categories, and `TsonAssetRuntimeTests.General_CSharp_number_lowering_supports_all_binary64_categories` protects that fix, but neither backend literal writer defines canonical TSON.

## String and Unicode law

Canonical escaping follows the implemented M0b printer: `\"`, `\\`, `\b`, `\f`, `\n`, `\r`, and `\t`; U+0000 through U+001F not covered by the short escapes and U+2028/U+2029 use uppercase four-digit `\uXXXX`; other valid BMP scalars and supplementary pairs are emitted raw. No Unicode normalization occurs.

The general language equality law currently treats strings as UTF-16 code-unit sequences in [core semantics](copeland-ts-core-semantics-cts-m0c.md), while `TsonString` requires Unicode scalars. Therefore each runtime string writer must scan code units, combine valid surrogate pairs, and return `InvalidUnicode` for an isolated high or low surrogate. C# and JavaScript use the same algorithm and the same 262,144-code-unit input bound.

For each emitted fragment, byte count is increased before append: ASCII/escape characters count one each; a raw scalar counts one byte through U+007F, two through U+07FF, three through U+FFFF, and four above. Static fragments carry compile-time byte counts. This proves the one-mebibyte bound without materializing UTF-8. The final string is joined/built only after success.

## Record and payload-enum projection

Records dispatch by the plan's static `MirRecordTypeId`, never by runtime shape. Stable identity comes from the plan; fields are read through exact generated members/Symbol slots in declaration order. Missing/extra/counterfeit state is impossible for compiler-created C# values and guarded by the existing JavaScript validator; violation is terminal. Same-shaped nominal types have different plans/carrier tokens and cannot cross.

Enums dispatch by the plan's static enum identity and a closed case switch. Each case identity and named payload identity comes from the plan; values are read in declaration order by property/payload ordinal. Zero-payload cases emit the bare canonical `Enum.Case`. Unknown/counterfeit cases panic. Enums are never encoded as `{ tag, payload }`.

## Structural objects and deferred families

`TsonObject` remains document-only. Ordinary object literals require contextual nominal-record construction; they do not establish a general runtime structural-object type. JavaScript plain objects and CLR objects are not TSON objects. Runtime structural encoding requires a separate accepted source type, ordering, access, and schema law.

| Family | Graduation criterion |
| --- | --- |
| Arrays | Add `TsonArray`, canonical syntax, bounds, parser/printer law, and portable runtime array eligibility. |
| Results | Define canonical success/error semantics distinct from payload enums. |
| Tables | Graduate arrays and Results, then define logical columnar TSON independent of carriers. |
| Interfaces/type aliases | Define schema-algebra identity/composition; they are not runtime values. |
| JSON | Consume TSON semantics under explicit loss policy; never private carriers. |

## Diagnostics and invariants

| Condition | Classification |
| --- | --- |
| Invalid intrinsic arity/context, unsupported root | Compile-time `COPE-TSON-ENCODE-0001` |
| Missing/malformed `$schema` or unstable identity | Compile-time `COPE-TSON-ENCODE-0002` |
| Unsupported reachable field/payload type | Compile-time `COPE-TSON-ENCODE-0003` |
| Schema cycle or identity/name collision | Compile-time `COPE-TSON-ENCODE-0004` |
| Cross-unit/cross-schema dependency | Compile-time `COPE-TSON-ENCODE-0005` |
| Invalid runtime UTF-16 sequence | `TsonEncodeError.InvalidUnicode` |
| String/output limit exceeded | `TsonEncodeError.OutputLimitExceeded` |
| Counterfeit carrier or impossible case | Terminal backend invariant panic |
| Missing/malformed plan/helper after validation | Terminal compiler/backend invariant |

Diagnostic identifiers are recommendations for M2b and do not exist today. Messages must name the static root/reachable member and be deterministic. Backend diagnostic text and host exception types are not source semantics.

## Demand-driven emission and `tsonAsset`

A program with no `tsonEncode` gains no plans, writer, schema text, number/string helper, error values, or TSON-related output size. One encoded root gains only its reachable catalog, its plan-specific value functions, the primitive helpers it uses, and Result/error support. Multiple calls for the same root share helpers but execute independently.

A value loaded by M1b is ordinary after binding, so no special round-trip path exists:

```ts
const loaded: Settings = tsonAsset("./settings.tson");
const encoded: string ! TsonEncodeError = tsonEncode(loaded);
```

The result is semantically equivalent canonical text. Original whitespace/comments are not preserved, and generated code does not retain the asset path, source text, or runtime filesystem dependency.

## Runtime decoding boundary

```text
canonical TSON text
  -X-> runtime parser unavailable on both backends
  -> schema-specific constructor path therefore deferred
```

The production parser is compiler-host C# in `Copeland.TS`; generated JavaScript does not contain it. Shipping the compiler only helps C#, and a second parser is rejected. The M2b writer accepts typed events and emits text; it contains no tokenizer, parser, document model, or decode path.

## Decision matrix

| Item | Classification | Reason |
| --- | --- | --- |
| `tsonEncode` | implement in M2b | Precise reserved intrinsic over one static nominal root. |
| String output | implement in M2b | Natural common carrier. |
| Byte output | accepted but deferred | Needs a portable byte carrier and strict transform API. |
| Exposed runtime `TsonValue` | rejected | No user need; compiler-host type is not portable. |
| Backend-neutral encoding plan | implement in M2b | Carries identity/order to both backends. |
| Dedicated TSON MIR expression | implement in M2b | Preserves evaluation and plan reference explicitly. |
| Generated direct writer | implement in M2b | Closed, reflection-free, demand-driven. |
| Shared runtime writer package | accepted but deferred | Generated helpers suffice initially. |
| Fixed output limit | implement in M2b | Deterministic and bounded. |
| Configurable output limit | accepted but deferred | Avoids compiler/source option design now. |
| Result-valued encoding | implement in M2b | Unicode and bounded output can fail. |
| Same-schema records | implement in M2b | Existing identity law is complete. |
| Cross-schema records | accepted but deferred | Module/catalog composition unresolved. |
| Payload enums | implement in M2b | Closed dispatch exists on both backends. |
| Structural objects | rejected | No runtime structural-object type. |
| Arrays | accepted but deferred | Requires TSON array law. |
| Results | accepted but deferred | Requires canonical success/error law. |
| Tables | accepted but deferred | Requires arrays and Results. |
| Interfaces | accepted but deferred | Schema algebra only; semantics absent. |
| Type aliases | accepted but deferred | Schema identity/alias law absent. |
| Runtime decoding | unresolved blocker | No one-parser cross-backend architecture. |
| JSON | accepted but deferred | Lossy compatibility lowering only. |
| Reflection | rejected | Violates closed NativeAOT-compatible design. |
| Host object traversal | rejected | Static nominal projection only. |

## Exact CTS-TSON-M2b scope

Implement exactly one reserved `tsonEncode(value)` intrinsic returning `string ! TsonEncodeError` for a statically known, same-compilation-unit, same-`$schema` nominal record or payload-enum root; permit nested Boolean, Number, String, Record, and Enum; lower through one demand-created, immutable, validated MIR encoding plan and dedicated encode expression; generate demand-driven C# and JavaScript type-specific projectors plus backend-local bounded writers; emit current M0b canonical text with ordinal-name-sorted reachable declarations, exact binary64 bits, strict Unicode validation, one final LF, and identical canonical UTF-8 byte accounting; reuse existing private carriers and Result mechanisms without reflection, enumeration, runtime parser, runtime filesystem, public `TsonValue`, runtime package, arrays, Results-as-data, tables, structural objects, cross-schema types, decoding, bytes, or JSON.
