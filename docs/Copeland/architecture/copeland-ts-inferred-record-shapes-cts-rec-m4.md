# Copeland TS inferred immutable record shapes (CTS-REC-M4)

**Status:** implemented bounded binder/backend reuse slice.

CTS-REC-M4 permits unannotated object literals and `with` results in variable
bindings. An uncontextualized literal becomes one compiler-interned exact ordered
record shape. The key is ordered field name plus canonical field type; equal keys
reuse one private identity and carrier. A contextual literal still constructs its
expected named record directly.

The feature reuses `RecordTypeSymbol`, `BoundRecordConstructionExpression`,
`BoundRecordWithExpression`, existing record MIR, shared validation, and existing
C#/JavaScript immutable carriers. There is no parser, MIR-schema, validator, or
backend semantic addition.

Generic inference retains two-phase contextual binding. If ordinary arguments
already infer a parameter, a deferred literal is constructed contextually as
before. If a parameter remains unresolved, an object literal may now bind
independently and contribute its inferred record identity. Named records remain
nominal and do not accept an already-bound anonymous value by shape.

Structural `type` aliases and field-only interfaces remain erased compiler facts.
This milestone does not create runtime structural objects, object spread,
mutation, optional fields, equality, hashing, TSON identity for anonymous values,
runtime reflection, or runtime structural dispatch.

The canonical design and assignability matrix are in
`docs/Copeland/language/copeland-record-shapes-types-interfaces-design.md`; the
real authoring assessment is in
`docs/Copeland/reviews/copeland-record-shape-dogfood.md`.
