# CTS-TSON-M2a runtime encoding audit

## Outcome

CTS-TSON-M2a is a documentation-only architecture success. The authoritative design is [Copeland TS runtime canonical TSON encoding](../Copeland/language/copeland-ts-runtime-tson-encoding-design-cts-tson-m2a.md), subsequently implemented by [CTS-TSON-M2b](cts-tson-m2b-runtime-canonical-encoding.md). No production or test behavior changed in M2a itself.

## Baseline

Work began from clean revision `95ec9f8b12a14e6b5f7292cfc93df64440f15cda` on branch `main`, tracking `origin/main` with no divergence.

## Audited implementation

The audit inspected the M0a/M0b/M1a/M1b design and migration records; `TsonValue` and its six variants; `TsonDocument`, profiles, limits, diagnostics, catalog/schema definitions, reader, and canonical printer; `$schema`, `tsonAsset`, `ICopelandAssetSource`, stable source-symbol identity, bound expansion, dependency evidence, and M1b parity corpus; MIR record/enum/Result/try nodes, lowering, validation, and text; C# record/enum/literal/helper generation; JavaScript private tokens/Symbols, record/enum carriers, validation, and demand mechanisms; CLI composition/artifact policy; and topology/dependency boundaries.

Exact evidence includes:

- production: `TsonValues.cs`, `TsonSchema.cs`, `TsonDocument.cs`, `TsonDocumentReader.cs`, `TsonCanonicalPrinter.cs`, `CopelandAssets.cs`, `Types.cs`, `Symbols.cs`, `BoundNodes.cs`, `Binder.cs`, `MirNodes.cs`, `MirLowerer.cs`, `MirValidator.cs`, `MirTextWriter.cs`, `CSharpBackend.cs`, `CSharpLiteralWriter.cs`, `JavaScriptBackend.cs`, `JavaScriptLiteralWriter.cs`, and CLI `Program.cs`;
- tests: `TsonFeatureTests`, `TsonFixtureTests`, `TsonAssetFeatureTests`, `TsonAssetRuntimeTests`, `RecordRuntimeTests`, `JavaScriptRuntimeTests`, and `ResultBackendParityTests`;
- artifacts/fixtures: `Tson/Valid`, `Tson/Invalid`, `TsonAssets/Valid`, `TsonAssets/Invalid`, and `TsonAssets/Corpus/record`.

## Decisions

- **Source API:** reserved `tsonEncode(value)`, one statically known same-unit record or payload-enum root, usable in ordinary expression positions, never first-class or shadowable.
- **Output/failure:** `string ! TsonEncodeError`; the two runtime cases are `InvalidUnicode` and `OutputLimitExceeded`. Bytes are deferred.
- **Limits:** fixed 1,048,576 canonical UTF-8 output bytes and 262,144 UTF-16 code units per string; static graph limits are checked before emission.
- **Identity/MIR:** frontend stable identities must enter one demand-created `MirTsonEncodingPlan`; a dedicated `MirTsonEncodeExpression` references it. Current MIR definitions alone are insufficient.
- **Ordering:** include only reachable nominal declarations, sorted by ordinal name; retain declaration order inside records/enums. Both backends consume the plan order.
- **C#:** generated plan-specific functions directly access internal getters and sealed cases, using one demand-emitted `StringBuilder` writer and `BitConverter` bits.
- **JavaScript:** generated functions live beside closure-private Symbols/tokens, directly use known slots and closed tags, and use `DataView` for bits; no enumeration or prototype discovery.
- **Numbers/Unicode:** normalize every NaN to `7FF8000000000000`, retain all other bits, emit uppercase hex; validate surrogate pairing and count canonical UTF-8 incrementally.
- **Structural/cross-schema:** structural objects remain document-only; M2b is same-unit and same-schema only.
- **Runtime parser:** none. Decoding remains blocked on one cross-backend parser architecture.

## Classification

Implement in M2b: `tsonEncode`, string output, the two-case Result error, fixed limits, same-schema record/enum roots and nested primitives/nominals, one validated MIR plan, one dedicated expression, and generated bounded writers.

Accepted but deferred: UTF-8 bytes, configurable limits, shared runtime package, cross-schema composition, arrays, Results-as-data, tables, interfaces/type aliases in the schema algebra, and JSON compatibility lowering.

Unresolved blocker: runtime decoding, because the production compiler-host parser is unavailable to generated JavaScript and a second parser is rejected.

Rejected: exposed runtime `TsonValue`, runtime structural-object projection, reflection, `dynamic`, universal carriers, host object traversal/property discovery, host serializers, runtime filesystem, compiler assembly in generated programs, and JSON-defined semantics.

## Exact M2b recommendation

Implement exactly the bounded slice stated at the end of the authoritative design: one Result-valued canonical string intrinsic over a same-unit nominal root, backed by one validated demand-driven MIR plan and reflection-free generated C#/JavaScript writers, with no parser or deferred data families.

## Documentation-only boundary

M2a changes documentation only. It adds no production code, tests, fixtures, MIR nodes, backend helpers, runtime library, intrinsic, CLI behavior, package/version, parser, JSON, array, Result-as-data, table, interface, or type-alias behavior.
