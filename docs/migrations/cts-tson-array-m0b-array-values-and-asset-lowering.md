# CTS-TSON-ARRAY-M0b array values and asset lowering

**Outcome:** complete and ratified: implemented the first TSON array semantic family without widening runtime TSON encoding.

- Added immutable `TsonArray(TsonArraySchema, elements)` and structural nested array type evidence.
- Reused the production parser's array expression and array type nodes; no TSON grammar, lexer, JSON path, or package was added.
- Enforced contextual empty arrays, exact homogeneous nominal compatibility, nested schema matching, a 100,000 element limit, and no array root.
- Printed and reparsed canonical multiline arrays deterministically.
- Lowered nested asset arrays through the ordinary bound/MIR array path.
- Completed the ordinary JavaScript backend array path with the existing left-to-right staging mechanism. The backend emits mutable ordinary arrays, matching the current C# carrier law; source still exposes no array mutation/indexing feature.
- Kept runtime `tsonEncode` array-free: no plan, writer, decoder, or JSON implementation exists.

Focused test coverage includes semantic/schema and filesystem fixtures, asset lowering, malformed MIR validation, and non-TSON JavaScript literal emission. Full solution validation remains recorded by the implementation change that owns this migration.

Closeout evidence adds a C#/Node executed asset parity harness for both authoring and canonical assets; JavaScript runtime-carrier behavior independent of TSON; exact array and node/depth boundaries; shared malformed-MIR rejection through both backend entry points; deterministic CLI output and stale-output preservation; and a pinned representative corpus. The only production repair was shared `MirValidator` boundary checking for array-typed locals and returns, including missing-element rejection; no backend-specific TSON workaround was added.

`TsonArray` remains immutable semantic data, while compiled C# `T[]` and JavaScript arrays remain ordinary mutable carriers. Canonical arrays use production `[]`/`T[]` syntax, homogeneous element schemas, contextual empty-array typing, nominal record/enum matching, four-space multiline printing, trailing commas, one final LF, and the 100,000-element ceiling. Arrays remain non-root data beneath nominal record/enum roots and have no alias/cycle identity law.

The new corpus hashes are recorded in the [ARRAY-M0b architecture record](../Copeland/architecture/copeland-ts-tson-arrays-and-assets-cts-tson-array-m0b.md#completion-evidence). Existing TSON and non-TSON corpus artifacts are unchanged. Runtime TSON array encoding, root arrays, JSON compatibility, decoding, Results, tables, optionality, interfaces, aliases, and package changes remain out of scope. The bounded follow-up, [ARRAY-M1 runtime encoding](../Copeland/architecture/copeland-ts-runtime-tson-array-encoding-cts-tson-array-m1.md), is now closed without widening this M0b boundary.
