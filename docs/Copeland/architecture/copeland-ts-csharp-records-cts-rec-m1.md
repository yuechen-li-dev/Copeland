# Copeland TS C# immutable records (CTS-REC-M1)

**Status:** implemented C# backend realization and semantic oracle, ratified by [CTS-REC-M3](copeland-ts-immutable-records-closeout-cts-rec-m3.md). CTS-REC-M2 supplies the JavaScript realization.

## Representation

Each canonical `MirRecordDefinition` emits one ordinary sealed CLR class. Its generated type name is derived bijectively and culture-invariantly from `MirRecordTypeId`; every member name is similarly derived from `MirRecordFieldId`. Source spelling, casing, C# keyword status, object identity, hashes, and dictionary traversal do not select runtime identity. Same-shaped declarations therefore remain distinct CLR types.

The class is public only because the existing proof module exposes generated functions publicly for invocation. Its full constructor and fixed field members are internal. Members are get-only, the constructor initializes every member, and no mutation operation exists. Accessibility and CLR reference identity are representation details, not Copeland source law.

Generated C# `record` and `record struct` were intentionally rejected for this representation. Their synthesized equality, hashing, cloning, deconstruction, and C# `with` surface would add behavior that Copeland has not approved.

| Copeland law               | C# realization                                   |
| -------------------------- | ------------------------------------------------ |
| Nominal record identity    | One generated sealed class per record ID         |
| Closed fields              | Fixed generated get-only members                 |
| Complete construction      | Full constructor                                 |
| Immutable fields           | No setters or mutation operations                |
| Authored initializer order | Ordered temporary evaluation                     |
| Declaration-order storage  | Constructor arguments in field order             |
| `with`                     | Source/replacement temporaries plus new instance |
| Equality deferred          | No generated structural equality                 |

## Type and expression lowering

`MirRecordType` maps directly to its generated class in locals, parameters, returns, nested fields, either Result component, payload-enum payloads, record-held Results, matches, propagation, unwrap, and typed handlers. It is never erased to `object` or a shared base/interface. Shared MIR validation runs before the first output line; unknown record or field identities and malformed construction/update shapes produce diagnostics and an empty artifact.

Construction evaluates every initializer once into a temporary in authored order. It then invokes the complete constructor with those temporaries in record declaration order. Nested construction uses the same statementful-expression path, including return, argument, Result, enum-payload, match, and handler contexts.

Field access emits the generated member selected by stable field ID. The receiver appears once and is parenthesized, which preserves both precedence and exactly-once behavior for assignment, call, update, and nested receivers. There is no reflection, `dynamic`, textual lookup, or writable target.

`with` first captures its source once, then captures replacements once in authored order. A new instance is constructed in declaration order from replacement temporaries and unchanged members of the original source temporary. No intermediate mutation exists, so later replacements cannot observe a partially updated record. A mutable `let` can be rebound to this new value without making either value mutable.

Enum matches whose selected arm needs statementful record lowering use an imperative generated switch so only the chosen arm executes. Existing expression-only enum matches keep their prior generated form and snapshot text.

## Composition and boundaries

The generated-C# proof harness compiles and executes record construction, nested access, functions, `with`, mutable binding rebinding, Result success and record error values, Result-held record fields, Result match, `?`, typed `try`/`except`, successful postfix unwrap, payload-enum storage/match, and record-returning match arms. A vertical program combines two record declarations, contextual nested construction, `let` rebinding, Result flow, payload-enum matching, and a final result of `42`; repeated generation and execution are stable.

Record `==` and `!=` remain frontend errors. The backend emits no `Equals`, `GetHashCode`, comparison, deconstruction, or clone implementation and does not use C# record equality. Unavoidable `object` members do not create a Copeland source operation.

CTS-REC-M2 emits private nominal frozen JavaScript values through the CLI and compares their observable behavior with this C# oracle. Existing non-record `.g.cs`, `.cope`, and `.g.js` artifacts remain unchanged.

## Artifact evidence

The exact C# corpus under `m1-record-csharp-valid` covers the representative lowering families. SHA-256 values are:

| Artifact | SHA-256 |
| --- | --- |
| `basic.g.cs` | `7E7EB61D7F4607F55578E929D950F56865C7F7EC278BB7F28C90D4256102E2DC` |
| `initializer-order.g.cs` | `A567AB3CA0CC5ACBE69DD2D3BE0988FF1B46827A6DA83C11210F80C9F7E5E1E4` |
| `nested.g.cs` | `DC7D989C0CF26C0B093D55DA023B3A94CFFD4A7FCD582E084D412585FE7EF8BC` |
| `with.g.cs` | `602D89CC68A6D49B117709EEE9C5F44D54C56F28C22EE91D1D91D9E883291ADB` |
| `result.g.cs` | `57377DDCB6607B43A31A7E8064E7851443D18DCD56E39C3E4AEB3168074B957C` |
| `payload-enum.g.cs` | `81D569833B169BB2CF95C394FB8266B551026473A33853A3E37A6CD1E7E4FEF3` |

The representation is ordinary ahead-of-time-friendly C#: sealed classes, constructors, properties, direct calls, and direct member reads. It adds no reflection activation, emitted runtime types, `dynamic`, runtime schema dictionary, or dynamic-code generation. CTS-REC-M1 does not claim a NativeAOT publish test.

## Ladder

1. CTS-REC-M0a: immutable nominal record design — accepted.
2. CTS-REC-M0b: frontend, type system, canonical MIR, validation, fixtures — implemented.
3. CTS-REC-M1: C# backend — implemented here.
4. CTS-REC-M2: JavaScript backend — complete after validation.
5. CTS-REC-M3: cross-backend stress, diagnostic/doctrine ratification, and closeout — implemented and closed.

Equality, patterns, and serialization require separate approval. Tables and `record table` belong only to a separately deferred future ladder.
