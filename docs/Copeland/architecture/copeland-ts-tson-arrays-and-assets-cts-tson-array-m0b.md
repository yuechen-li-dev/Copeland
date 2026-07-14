# Copeland TS TSON arrays and assets: CTS-TSON-ARRAY-M0b

**Status:** implemented.

ARRAY-M0b adds the closed compiler-host `TsonArray` value. It owns a structural `TsonArraySchema` containing an explicit `TsonTypeReference` element schema and an immutable copied ordered element list. Arrays have no nominal identity; record and enum element identities remain exact. There is no public equality or hash contract.

Authoring and canonical documents both use the production `ArrayLiteralExpressionSyntax` and ordinary `T[]` type syntax. Nonempty arrays print multiline with four-space indentation and trailing commas; empty arrays print as `[]`. The printer has one LF terminator and UTF-8 materialization is BOM-free.

Arrays are homogeneous, finite trees. Supported elements are Boolean, Number, String, nominal Record, nominal Enum, and nested arrays. Structural objects, Result, table, optional, interface, alias, JSON, and executable expressions are excluded. Empty arrays require the enclosing field, payload, or nested array schema. An explicitly typed root array is rejected; asset roots remain one nominal record or enum.

`TsonDocumentReader` applies the existing source, depth, declaration, aggregate, node, string, and new `MaximumArrayLength` (100,000) bounds. Each array and every element count as value nodes. Schema reachability follows array element references and retains recursive-schema rejection.

Compile-time `tsonAsset` lowering validates the array schema against the expected `ArrayTypeSymbol`, then lowers recursively to `BoundArrayExpression` and existing `MirArrayExpression`/`MirArrayType`. No TSON semantic object, path, parser, schema, or asset intrinsic reaches either backend.

JavaScript now realizes valid ordinary array MIR as normal JavaScript array literals, using the existing ordered staging helper so elements are evaluated once, left-to-right. Arrays are deliberately not frozen: they are ordinary Copeland runtime carriers, while only `TsonArray` is immutable semantic data. C# continues to use its existing `T[]` realization.

Runtime `tsonEncode` remains unchanged and rejects reachable arrays before a runtime plan or writer is generated. This milestone adds no `MirTsonArrayPlan`, runtime decoder, JSON use, second parser, or array writer helper. ARRAY-M1 should define bounded runtime array encoding only after this ordinary-carrier parity has further corpus coverage.
