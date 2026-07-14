# Copeland TS JavaScript immutable records (CTS-REC-M2)

**Status:** implemented JavaScript backend realization. CTS-REC-M0a, M0b, M1, and M2 are complete; CTS-REC-M3 owns final stress and doctrine closeout.

## Representation

Each canonical `MirRecordDefinition` emits one private `Symbol` type token, one private `Symbol` slot per stable `MirRecordFieldId`, a complete constructor helper, and a nominal assertion helper. Generated names include the stable record or field ID and are allocated deterministically. Source names may aid symbol descriptions but do not select identity.

The constructor creates `Object.create(null)`, defines the brand and every field with non-writable, non-enumerable, non-configurable symbol-keyed properties, and publishes only the completed `Object.freeze` value. There is no class, prototype method, ordinary user-field storage key, public constructor, registry, schema lookup, runtime package, JSON contract, or public host ABI.

| Copeland law               | JavaScript realization                           |
| -------------------------- | ------------------------------------------------ |
| Nominal record identity    | Private per-record token                         |
| Closed field set           | Fixed private field-ID properties                |
| Complete construction      | Private record constructor helper                |
| Immutable shape/fields     | Non-writable properties plus `Object.freeze`     |
| No prototype semantics     | `Object.create(null)`                            |
| Authored initializer order | Ordered staged evaluation                        |
| Declaration-order storage  | Canonical helper arguments/properties            |
| Resolved field access      | Brand assertion plus fixed slot read             |
| `with`                     | Source/replacement staging plus new frozen value |
| Equality deferred          | No emitted source equality operation             |

The freeze is deliberately shallow enforcement of the record's own shape and field slots. Nested Copeland records are independently frozen. A contained value retains its own language law; this milestone does not recursively freeze unknown host graphs or claim arbitrary transitive immutability.

## Lowering and runtime checks

Construction evaluates initializers exactly once in authored left-to-right order and stores each result in a temporary. Only then does it call the record constructor in field declaration order. The same statementful-expression path works in locals, assignments, returns, arguments, nested fields, Result and enum payloads, matches, conditionals, protected blocks, and handlers.

Resolved access captures its receiver once, invokes the expected record assertion, and reads the fixed symbol slot. `with` captures and validates its source before evaluating replacements, stages replacements once in authored order, then constructs a new value in declaration order from replacements and unchanged source slots. The source is never mutated, and a `let` binding may be rebound to the distinct new value.

Record assertions require an object with null prototype, a frozen shape, the expected private token, and exactly the expected compiler-owned symbol slots. Same-shaped records, ordinary and frozen objects, null-prototype textual impostors, payload enums, Results, and typed flow-transfer records fail. The shared invariant panic throws a terminal host exception; ordinary Result flow still uses private structured values and no host exceptions, and Copeland `except` never catches invariant or unwrap panics.

The expression combiner now stages earlier operands before a later statementful operand prelude. This closes the argument/binary ordering defect exposed by record construction while retaining expression-only output. Branch preludes remain within the selected conditional, enum arm, Result arm, protected block, or handler.

## Privacy, equality, and host boundary

Record tokens and slots are compiler-owned representation details. Records do not share source semantics with payload enums, Results, or flow-transfer values even where low-level frozen-value practices are similar. Record `==` and `!=` remain rejected; JavaScript identity, token comparison, hashing, and ordering are not Copeland operations.

Determined hostile JavaScript reflection is outside the undefined host ABI. The representation supplies private nominal discipline for generated code, not a cryptographic security boundary. Interop, reflection, JSON, serialization, and mutation through a future unsafe boundary require separate designs.

## Evidence and artifacts

Focused backend and Node tests prove repeated deterministic emission/execution, same-shape isolation, ordinary/frozen/null-prototype impostor rejection, isolation from enum/Result/flow records, null prototype, freezing, fixed descriptors, failed add/delete/write attempts, nested freezing, source-preserving `with`, receiver/source/replacement exactly-once behavior, authored order, branch selection, and terminal invariant behavior. Cross-backend tests repeatedly execute the same MIR through generated C# and Node, including the vertical record/Result/enum/handler program returning `42` and the argument-order trace result `1132`.

The JavaScript corpus hashes are:

| Artifact | SHA-256 |
| --- | --- |
| `record-basic.g.js` | `AA91167AF8D33B45731748BF5D0861FBCE4EF7D195E96E2ADFFB7C77F62EB8A0` |
| `record-order-with.g.js` | `EC92548B37415D888B02ACB6C9D163096DD2D46FF66C23767E5BE0E43DA56060` |
| `record-result-enum.g.js` | `DDACF318CB2777D5A4E5A138B8875F3AB3752F8AD93D6C64DD185EF55B56BB24` |
| `record-try-except.g.js` | `859A7CD39986AC6D3410943A529AAA0222E320240FFE96D36A2E2883DC733F7D` |

Programs without record MIR retain their existing text and emit no record helpers. Shared MIR validation still runs before artifact creation, and malformed record MIR returns no partial output. The CLI now emits MIR, `.g.cs`, or `.g.js` for accepted record source without `COPE-JS-REC-0001`.

## Ladder

1. CTS-REC-M0a: design — complete.
2. CTS-REC-M0b: frontend/MIR — complete.
3. CTS-REC-M1: C# backend — complete.
4. CTS-REC-M2: JavaScript backend — complete after validation.
5. CTS-REC-M3: cross-backend stress, diagnostic/doctrine ratification, and closeout.

Record tables and `record table` belong only to a separately deferred future ladder.
