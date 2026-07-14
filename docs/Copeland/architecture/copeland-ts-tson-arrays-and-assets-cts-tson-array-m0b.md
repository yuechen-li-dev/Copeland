# Copeland TS TSON arrays and assets: CTS-TSON-ARRAY-M0b

**Status:** complete and ratified.

ARRAY-M0b adds the closed compiler-host `TsonArray` value. It owns a structural `TsonArraySchema` containing an explicit `TsonTypeReference` element schema and an immutable copied ordered element list. Arrays have no nominal identity; record and enum element identities remain exact. There is no public equality or hash contract.

Authoring and canonical documents both use the production `ArrayLiteralExpressionSyntax` and ordinary `T[]` type syntax. Nonempty arrays print multiline with four-space indentation and trailing commas; empty arrays print as `[]`. The printer has one LF terminator and UTF-8 materialization is BOM-free.

Arrays are homogeneous, finite trees. Supported elements are Boolean, Number, String, nominal Record, nominal Enum, and nested arrays. Structural objects, Result, table, optional, interface, alias, JSON, and executable expressions are excluded. Empty arrays require the enclosing field, payload, or nested array schema. An explicitly typed root array is rejected; asset roots remain one nominal record or enum.

`TsonDocumentReader` applies the existing source, depth, declaration, aggregate, node, string, and new `MaximumArrayLength` (100,000) bounds. Each array and every element count as value nodes. Schema reachability follows array element references and retains recursive-schema rejection.

Compile-time `tsonAsset` lowering validates the array schema against the expected `ArrayTypeSymbol`, then lowers recursively to `BoundArrayExpression` and existing `MirArrayExpression`/`MirArrayType`. No TSON semantic object, path, parser, schema, or asset intrinsic reaches either backend.

JavaScript now realizes valid ordinary array MIR as normal JavaScript array literals, using the existing ordered staging helper so elements are evaluated once, left-to-right. Arrays are deliberately not frozen: they are ordinary Copeland runtime carriers, while only `TsonArray` is immutable semantic data. C# continues to use its existing `T[]` realization.

Runtime `tsonEncode` remains unchanged and rejects reachable arrays before a runtime plan or writer is generated. This milestone adds no `MirTsonArrayPlan`, runtime decoder, JSON use, second parser, or array writer helper. ARRAY-M1 should define bounded runtime array encoding only after this ordinary-carrier parity has further corpus coverage.

## Completion evidence

The dedicated asset parity harness executes one authoring `.obj.ts` asset and its canonical `.tson` fixed point through both generated backends. It proves empty contextual arrays, boolean/number/string arrays, positive and negative zero (by binary64 bits), finite values, NaN, both infinities, escape-sensitive and Unicode strings, record and payload-enum arrays, nested primitive arrays, nested empty arrays, a record containing arrays, an enum payload containing an array, nominal identities, payloads, and source order. Host-side inspection is test-only; generated products remain closed and reflection-free.

`TsonArray` is immutable compile-time semantic data. A compiled Copeland array is an ordinary runtime carrier: C# uses `T[]` and JavaScript uses a mutable ordinary array. JavaScript has separate non-TSON Node evidence for empty/nested/primitive/record/enum arrays, left-to-right construction, exactly-once calls, and an unselected conditional branch. No array is frozen merely because its originating TSON value was immutable.

Canonical array fixtures prove production-parser authoring input, comment/noncanonical normalization, canonical reparsing, four-space multiline output, trailing commas, one final LF, retained empty/nested schema evidence, stable order, and repeated byte-identical printing. Programmatic boundary tests cover 99,999, 100,000, and 100,001 elements; semantic depth; aggregate value nodes; and empty-array node counting. Existing bounded depth remains the guard against recursive traversal overflow.

Shared MIR validation now rejects malformed ordinary arrays before either backend emits: expression type, element type, nested type, record nominal, enum nominal, malformed empty-array type, missing element, local initializer, and return boundaries. Runtime `tsonEncode` still rejects an array-reachable nominal root before a plan or artifact exists; there is no array plan entry or writer.

The representative corpus is `tests/Copeland/Copeland.TS.Tests/TsonAssets/Corpus/arrays`:

| Artifact | SHA-256 |
| --- | --- |
| `batch.obj.ts` | `32c551b037fa503a646b4fcc30c983aea8b94f3235bde0b67bae64f963871ede` |
| `main.ts` | `d95366df1041d079075628c8132c44b1325835b4bfbd9ada8a71a0dc033f5e03` |
| `main.cope` | `840a285a4238f341f34aa89348d00e5cdf5677422009192e0150d2c1c7a4b12e` |
| `main.g.cs` | `a3b97b999c7bc529fd40ac7b38bc860a89a664d6cf5639952f8123f562777015` |
| `main.g.js` | `de884da8fdaacd96ba8ac92e75076df0df268298f29da65df0c74b7af56f5873` |

The existing record corpus stays byte-identical under its pre-existing pinned hashes. ARRAY-M1 remains the exact next recommendation: design a bounded, shared runtime array encoding plan and direct indexed writers without JSON, reflection, root arrays, decoding, Results, tables, optionality, interfaces, or aliases.
