# Copeland TS compile-time TSON assets (CTS-TSON-M1b)

**Status:** implemented compiler-to-runtime vertical slice. Its ordinary projected values can now be canonically encoded by [CTS-TSON-M2b](copeland-ts-runtime-tson-encoding-cts-tson-m2b.md) without retaining asset paths or source formatting.

## Source and identity contract

M1b accepts the compiler intrinsic only as the initializer of an explicitly annotated local `const`:

```ts
const $schema: string = "copeland://example/settings";

record Settings {
    title: string;
    enabled: boolean;
}

function load(): Settings {
    const settings: Settings = tsonAsset("./settings.tson");
    return settings;
}
```

The expected type must be exactly one same-compilation-unit nominal record or payload enum. Return, argument, conditional, match, inferred-variable, global-initializer, computed-path, and first-class uses are rejected. `tsonAsset` is reserved: it cannot be declared, shadowed, taken as a value, or emitted as a call.

An asset-participating compilation unit requires exactly one top-level directive in the exact form `const $schema: string = "...";`. The literal must be a nonblank whitespace-free `copeland://` identity without `#`. It is compiler metadata and is omitted from MIR and generated programs. Identities are derived without compiler-local IDs:

```text
type          schema#Type
record field  schema#Type.field
enum case     schema#Enum.Case
enum payload  schema#Enum.Case.payload
```

M1b has one source file and no import/module system, so an expected nominal declaration necessarily belongs to that compilation unit. Aliases, versions, package identities, and cross-module inference remain absent.

## Resolution and profiles

`CopelandCompilationOptions` supplies `SourcePath`, `ProjectRoot`, and an injected `ICopelandAssetSource`. Resolution is relative to the source file, normalizes both separator spellings plus `.` and `..`, rejects absolute paths and paths outside the root, and accepts only `.obj.ts` and `.tson`. Missing or unreadable content is reported without host exception text. The CLI supplies a filesystem source and uses the source directory as its declared compilation root; tests supply deterministic in-memory and filesystem sources.

Each successful logical load records a root-relative `/`-separated path and lowercase SHA-256 content hash in `CopelandCompilation.AssetDependencies`. Repeated normalized loads share compiler-host parse input and one dependency entry but still expand into ordinary construction at each use site. Backends do not resolve or reread assets.

`.obj.ts` selects `TsonDocumentProfile.ObjectTypeScript`, permits production-parser comments and layout, and requires embedded `$schema`. `.tson` selects `CanonicalTson`, requires embedded identity and exact canonical text. Both go through `TsonDocumentReader.ReadSelfDescribed`, and extension selection does not change semantic values.

## Expansion and diagnostics

The compiler proves the asset root is the expected `TsonRecord` or `TsonEnum`, its stable identity is exact, and every reachable declaration agrees by kind, identity, declaration order, fields, cases, payloads, and child types. It permits only Boolean, Number, String, nominal Record, and nominal Enum. A structural object is only an authored record field envelope; a structural root or compiled structural value is rejected.

Successful values expand recursively into `BoundLiteralExpression`, `BoundRecordConstructionExpression`, and `BoundEnumValueExpression`. Existing `MirLowerer`, `MirValidator`, and backends own everything afterward:

```text
tsonAsset literal
  -> compiler resolver
  -> production parser and M0b TSON validation
  -> exact compiled schema validation
  -> existing bound constructions
  -> canonical Cope MIR
  -> existing C# or JavaScript constructors
```

There is no TSON bound executable node, MIR node, backend node, runtime parser, runtime library, reflection, dynamic traversal, or runtime filesystem call.

M1b diagnostics use `COPE-TSON-ASSET-0001` for intrinsic/context misuse, `0002` for resolution/profile selection failures, `0003` for stable identity/schema/root mismatch, `0004` for compilation-unit schema metadata, and `0005` for M1b-unsupported compiled value families such as structural runtime objects. Asset parser and restriction failures retain their `COPE-LEX-*`, `COPE-PARSE-*`, or `COPE-TSON-0001`–`0005` identifiers. `Diagnostic.SourcePath` carries the normalized asset path for asset-owned failures; positions and nonempty spans remain asset-local. The current diagnostic model has no related-location collection, so messages also name the normalized asset while the intrinsic site owns resolution and matching diagnostics.

## Backend and binary64 behavior

Generated C# contains ordinary complete record constructors and sealed enum cases. Generated JavaScript contains the existing closure-private type tokens, symbols, and constructors. Neither artifact contains `$schema`, `tsonAsset`, the asset path, TSON/compiler types, parsing code, or filesystem access.

`TsonNumber.Value` becomes an ordinary numeric bound/MIR literal. JavaScript's invariant writer preserves `-0`, finite exponent spellings, NaN, and infinities. M1b exposed a general C# literal-writer defect: nonfinite values previously emitted invalid identifiers and the finite custom format was not a complete binary64 round-trip contract. The owning writer now emits exact NaN bits through `BitConverter.UInt64BitsToDouble`, named infinity constants, explicit `-0.0`, and invariant round-trip finite text. A non-TSON MIR regression covers all categories.

Runtime parity executes canonical asset values three times in generated C# and twice in Node. The observed binary64 trace is:

```text
0000000000000000|8000000000000000|3FF8000000000000|7FEFFFFFFFFFFFFF|0000000000000001|7FF8000000000000|7FF0000000000000|FFF0000000000000|"quote \" slash \\ newline\n雪 😀"|true|42
```

## Evidence and exclusions

`TsonAssetFeatureTests` owns in-memory resolution, both profiles, record and enum roots, nested record/enum construction, normalized and repeated paths, dependency hashes, intrinsic/schema/path failures, same-shaped wrong identity, canonical rejection, and provenance. `TsonAssets/Valid` and `TsonAssets/Invalid` own filesystem fixtures. `TsonAssetRuntimeTests` owns deterministic MIR/backend emission, generated-token inspection, special binary64 values, Unicode/escaping, nested enum-record observation, and repeated C#/Node parity. CLI tests own real filesystem composition and stale-output preservation.

M1b adds no runtime TSON encode/decode, runtime TSON package, second parser, JSON, TSON array/Result/table/optional variant, `null`, `undefined`, structural runtime object, arbitrary compile-time execution, dynamic path, network/package resolution, import/module system, reflection, dictionary traversal, or package version change.

The documentation-only [CTS-TSON-M2a design](../language/copeland-ts-runtime-tson-encoding-design-cts-tson-m2a.md) now specifies the recommended runtime canonical encoding architecture: a Result-valued string intrinsic, one demand-driven validated MIR plan, and generated type-specific bounded writers. Runtime decoding remains blocked on one parser architecture available to both generated C# and JavaScript.
