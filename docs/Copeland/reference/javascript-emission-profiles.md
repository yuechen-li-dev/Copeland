# JavaScript emission profiles

Copeland has two JavaScript value representations. Profile selection is an emission/build decision; authored Copeland source is unchanged.

## Validated

`Diagnostic` (the default) and `Symbolic` are checked representations for compiler development and hostile-interop diagnostics. They construct null-prototype, frozen nominal carriers, register provenance in `WeakSet`s, and validate nominal values at internal field reads and matches. `Symbolic` differs only in generated identifier vocabulary.

Validated emission is intentionally expensive in typed hot paths. It remains the appropriate profile when diagnosing malformed values or validating backend invariants.

## Production

`Production` is for shipped generated JavaScript. It has this trust law:

- trusted values are made by generated constructors, returned by generated calls, passed within generated module graphs, or are compiler-generated enum singletons;
- untrusted values are raw host, browser, npm, dynamic-import, handwritten-JS, and deserialized values;
- trusted internal operations use direct fields and tags without repeated validators;
- a value must pass the generated nominal validator at an explicit interop boundary before it can be treated as an existing Copeland nominal value.

Production does not mean unsafe. It moves checks to boundaries and uses compiler-controlled code for internal semantic immutability. Generated Copeland code never writes record/enum fields. A production value is not frozen for every internal construction; code handed to arbitrary JavaScript must not expose mutable nominal carriers as a general ownership contract. Current generated standalone modules do not export compiler-private constructors/tokens.

## Records

Production record constructors emit one stable object shape:

```js
return { [recordTypeToken]: recordTypeToken, $f0: field0, $f1: field1 };
```

The private `Symbol` token preserves nominal identity; `$fN` properties use deterministic declaration order. Generated reads use direct `$fN` access. `with` evaluates replacements once and calls the type-specific constructor with unchanged direct fields, creating a new object without mutating the source.

The generated record validator remains available for boundaries. It verifies the private token, exact visible field set, expected symbol count, and recursive field types. Plain, frozen, structurally similar, wrongly nominal, or externally mutated objects fail it because they cannot manufacture the module-private token or preserve the expected field types.

## Enums

Production enums use `$type`, `$tag`, and deterministic `$pN` payload fields. Pattern matching is a direct `switch (value.$tag)` in trusted code. Zero-payload cases are one frozen canonical singleton per generated enum case; payload-bearing cases allocate a fresh stable object and never allocate a payload array.

The generated enum validator remains the boundary gate. It verifies the enum-private type token, case tag, exact property shape, payload arity, and payload types. An enum from another nominal type with the same tag is rejected.

## Interop and limits

Generated host/npm/browser call arguments are trusted Copeland values unless a declared contract applies an ownership conversion. Raw external results are untrusted and must not be used as nominal records/enums without a generated boundary validator/projection. Existing declared contracts primarily use primitives/callables; a general structural external-data-to-nominal projection surface is deliberately deferred rather than silently accepting structural lookalikes.

For generated ESM module graphs, production nominal tokens must continue to come from the compiler-owned cross-module registration path when values cross independently emitted modules. The local single-module emitter uses lexical private tokens. Do not expose tokens, constructors, or validators as authored exports.

## Recommendation

Keep `Diagnostic`/`Symbolic` as the default checked profiles. Select `Production` explicitly for release/browser/Node artifacts after interop contracts have been reviewed. This preserves high-signal validation for compiler tests while giving V8 an optimization-friendly production path.
