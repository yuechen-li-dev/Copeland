# CTS-TSON-M2b runtime canonical TSON encoding

**Status:** implemented. This milestone implements the accepted CTS-TSON-M2a design for reflection-free runtime encoding in the C# and JavaScript backends.

## Source contract

The reserved compiler intrinsic is:

```ts
const encoded: string ! TsonEncodeError = tsonEncode(value);
```

It accepts exactly one statically known nominal immutable record or nominal payload-enum root declared in the same compilation unit. The root and every reachable nominal declaration must belong to the unit's accepted `$schema`. Reachable values are limited to `boolean`, binary64 `number`, `string`, records, payload enums, and nested combinations of those families.

`tsonEncode` cannot be declared, shadowed, referenced as a value, or called dynamically. Its operand is an ordinary expression evaluated exactly once. The result participates in typed locals, returns, arguments, conditional branches, Result matches, `?`, postfix unwrap, typed `try`/`except`, and statementful expression staging.

The compiler-owned payload enum is exactly:

```ts
enum TsonEncodeError {
    InvalidUnicode,
    OutputLimitExceeded,
}
```

It is emitted only when encoding demands it. Invalid runtime UTF-16 returns `InvalidUnicode`. A string longer than 262,144 UTF-16 code units, or canonical output longer than 1,048,576 UTF-8 bytes, returns `OutputLimitExceeded`. Per-string length is checked first, Unicode validity second, and total bounded output third. Impossible generated-carrier mismatches remain terminal backend invariants.

## Stable identity and reachable schema

Binding creates one closed encoding plan per distinct root and reuses it for repeated calls. The plan retains only demanded identities:

```text
schema#Type
schema#Type.field
schema#Enum.Case
schema#Enum.Case.payload
```

Reachability starts at the root, deduplicates repeated nested types, rejects cycles and unsupported or cross-schema dependencies, and excludes unused declarations. Record fields, enum cases, and payloads preserve declaration order. Reachable declarations are sorted by ordinal nominal name before entering MIR. Identity collisions are compile-time errors; compiler-local IDs and generated helper names never become exchange identity.

Programs without `tsonEncode` contain no encoding plan, retained schema text, writer, or encoding error carrier.

## MIR ownership and validation

`MirTsonEncodingPlan` is an immutable, inspectable backend-neutral description of the schema, root, closed value plans, stable identities, declaration ordering, and fixed limits. `MirTsonEncodeExpression` contains the ordinary operand and references a plan ID. Deterministic `.cope` text renders both.

One shared MIR validation pass runs before either backend. It rejects missing or duplicate plans, missing references, root/result mismatches, malformed schema and identities, duplicate identities, invalid ordering or declaration members, unsupported values, cycles, extraneous declarations, invalid limits, and malformed canonical static text. Neither backend walks frontend symbols to rediscover schema order, and malformed MIR produces no artifact.

The shared `MirTsonCanonicalText` builder owns the canonical static document prefix. Its text and UTF-8 size are validated once and consumed by both backends.

## Canonical writer law

Output is the CTS-TSON-M0b canonical document: `$schema`, all and only reachable declarations, one `$value`, four-space indentation, LF newlines, and exactly one final LF. It is a string; BOM and byte-array surfaces do not exist. Tests reparse emitted output with the compiler-host `TsonDocumentReader` under `CanonicalTson` and compare the semantic document and stable identities.

Binary64 is emitted as `$number("XXXXXXXXXXXXXXXX")` using exactly 16 uppercase hexadecimal logical bits. NaNs normalize to `7FF8000000000000`; signed zero, subnormals, finite maxima, and signed infinities retain their exact bits. C# uses deterministic `BitConverter` bits. JavaScript uses an explicitly big-endian `DataView` conversion.

Strings use the M0b quote, backslash, control, and U+2028/U+2029 escapes. Both writers explicitly scan UTF-16 surrogate pairs, preserve valid supplementary scalars, reject lone surrogates, and count emitted UTF-8 bytes incrementally. Neither delegates validity or size decisions to a host encoder.

## Backend realization

The C# backend demand-emits a private closed `__TsonWriter`. It reads statically known record properties, dispatches statically known sealed enum cases, accumulates into a bounded `StringBuilder`, and returns the ordinary private `CopeResult` carrier. It uses no reflection, `dynamic`, JSON, parser, filesystem, or compiler-host TSON assembly at runtime.

The JavaScript backend demand-emits one private writer closure inside the existing record/enum token scope. It validates existing nominal brands, reads private symbol slots directly, dispatches closed enum cases, and returns the existing frozen null-prototype Result/error carriers. It performs no property enumeration, shape inference, JSON conversion, prototype traversal, or ordinary-failure `catch`/`finally`.

Repeated calls reuse the plan/helper while evaluating each operand once. Distinct roots receive deterministic distinct plan entry points while sharing only closed primitive helpers inside the demanded writer family.

## Asset round trip

An ordinary value produced by `tsonAsset` follows the same encoder path:

```ts
const loaded: Settings = tsonAsset("./settings.tson");
const encoded: string ! TsonEncodeError = tsonEncode(loaded);
```

The generated runtime retains neither asset path nor original formatting/comments. Deleting the asset after compilation does not affect execution. The result is freshly canonical semantic TSON and reparses through the M0b reader.

## Diagnostics and exclusions

Stable `COPE-TSON-ENCODE-0001` through `0005` diagnostics cover malformed calls, missing/malformed schema, unsupported root or reachable values, schema cycles, identity collisions, and cross-schema dependencies. All diagnostics have source spans; host exception text is not exposed.

This milestone adds no runtime parsing/decoding, filesystem access, runtime compiler dependency, public `TsonValue`, general serialization package, structural carrier traversal, arrays/Results/tables/optionals as TSON data, `null`, `undefined`, maps, JSON, reflection, cross-unit roots, or cross-schema roots.

## Evidence and next milestone

Focused source, MIR, malformed-plan, backend/runtime, Unicode/limit, binary64, adversarial JavaScript, asset-round-trip, CLI, and corpus tests live beside the existing Copeland TS suites. The representative corpus is `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/record`.

The next TSON milestone should remain separate: runtime decoding requires an explicitly accepted parser/runtime architecture and must not be inferred from this closed writer.

