# CTS-TSON-ARRAY-M0b array values and asset lowering

**Outcome:** implemented the first TSON array semantic family without widening runtime TSON encoding.

- Added immutable `TsonArray(TsonArraySchema, elements)` and structural nested array type evidence.
- Reused the production parser's array expression and array type nodes; no TSON grammar, lexer, JSON path, or package was added.
- Enforced contextual empty arrays, exact homogeneous nominal compatibility, nested schema matching, a 100,000 element limit, and no array root.
- Printed and reparsed canonical multiline arrays deterministically.
- Lowered nested asset arrays through the ordinary bound/MIR array path.
- Completed the ordinary JavaScript backend array path with the existing left-to-right staging mechanism. The backend emits mutable ordinary arrays, matching the current C# carrier law; source still exposes no array mutation/indexing feature.
- Kept runtime `tsonEncode` array-free: no plan, writer, decoder, or JSON implementation exists.

Focused test coverage includes semantic/schema and filesystem fixtures, asset lowering, malformed MIR validation, and non-TSON JavaScript literal emission. Full solution validation remains recorded by the implementation change that owns this migration.
