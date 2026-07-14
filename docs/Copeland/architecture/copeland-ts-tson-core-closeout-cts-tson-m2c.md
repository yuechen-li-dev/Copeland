# Copeland TS TSON core closeout (CTS-TSON-M2c)

**Status:** complete. This record ratifies the core contract implemented through CTS-TSON-M2c and links the finite evidence ledger in [`cts-tson-m2c-core-fixed-point-closeout.md`](../../migrations/cts-tson-m2c-core-fixed-point-closeout.md). It does not authorize a new value family or runtime decoding.

## Ratified core

TSON is a restricted semantic projection parsed by the production `SyntaxTree.Parse` path; it has no separate lexer or runtime parser. `.obj.ts` is the authoring profile and `.tson` is the exact canonical self-contained profile. Both produce the same compiler-host semantic document when their declarations and value agree. One root is required and executable syntax remains rejected.

The semantic algebra remains `Boolean | Number | String | Object | Record | Enum`. `Object` is document-only. Generated runtime encoding accepts only a nominal record or enum root and can reach only Boolean, Number, String, Record, and Enum. Exchange identity is stable and schema-scoped:

```text
schema#Type
schema#Type.field
schema#Enum.Case
schema#Enum.Case.payload
```

Asset loading remains compile-time-only: an explicitly typed `tsonAsset("./relative-path")` is parsed, validated, and lowered through ordinary bound/MIR constructors. Paths, comments, source layout, asset readers, parsers, and compiler-host TSON types are not runtime dependencies.

`tsonEncode(value)` returns `string ! TsonEncodeError`, evaluates its operand once, uses one demand-created validated plan per root, and emits exactly one canonical LF-terminated string. Its only ordinary errors are `InvalidUnicode` and `OutputLimitExceeded`; per-string UTF-16 length (262,144), Unicode validity, and total UTF-8 size (1,048,576) are checked in that order. Binary64 is exactly sixteen uppercase hexadecimal bits, with NaNs normalized to `7FF8000000000000`.

## Fixed-point evidence

`TsonEncodeRuntimeTests.Fixed_point_matrix_preserves_nominal_identity_and_erases_authoring_trivia` compiles a nested record/enum matrix through both C# and JavaScript. It verifies three roots (nested record root plus two same-cased distinct enum roots), exact C#/Node text parity, canonical-reader acceptance, canonical-printer equality, one final LF, ordinal declaration order, stable nominal distinction, `-0`, escaped supplementary Unicode, and absence of source comments from output.

`TsonEncodeRuntimeTests.Encoding_uses_existing_staging_for_once_order_and_result_flow` proves the encoder participates in ordinary expression staging: once-only operand evaluation, argument order, selected conditional and logical branch behavior, Result matching, forwarding, `?`, typed `try`/`except`, and repeated calls. Both generated backends use their existing Result flow rather than ordinary-failure exception handling.

M2c exposed one production defect in the JavaScript nominal-carrier validator: a hostile caller could copy discoverable symbol slots from a frozen record to a new null-prototype object. The shared carrier runtime now registers legitimate records and enums in private generated `WeakSet` provenance registries and requires membership before access or TSON encoding. Record validation no longer enumerates symbols. The adversarial clone regression and regenerated JavaScript corpus prove the repair; the resulting JavaScript artifact hash changes are intentional and limited to carrier hardening.

`MalformedTsonEncodingPlanValidationTests` has nineteen representable malformed-plan cases. Both backend entry points reject each through shared MIR validation and return no source artifact, including duplicate/missing plans, bad schema/static text/limits, expression root and Result mismatches, structural/array/Result roots, identity violations, missing/extraneous declarations, unsupported references, cycles, enum case/payload identity collisions, and declaration order. The linked ledger maps every production validator branch; optionality and cross-unit malformed references are inapplicable by MIR construction.

The pre-existing focused suites retain broader M0b/M1b/M2b evidence: parser/profile fixtures, compile-time asset resolution, canonical corpus hashes, Unicode and exact-size limits, binary64 categories, generated-artifact isolation, counterfeit nominal JavaScript values, and asset-to-runtime encoding. The M2c limit regression additionally covers 262,143/262,144/262,145 UTF-16 units, a valid supplementary pair at the boundary, both lone-surrogate directions, exact total UTF-8 capacity, and invalid-Unicode precedence over a simultaneous total overflow.

`TsonEncodeRuntimeTests.Runtime_canonical_output_recompiles_as_a_canonical_asset_without_byte_changes` supplies the two-generation proof. It captures C# and Node canonical output, uses that exact string as an in-memory `.tson` asset in a new compilation, then confirms both re-encoders return the same bytes with no runtime asset path. Existing CLI integration tests cover deterministic MIR/C#/JavaScript emission plus fresh-output and stale-output preservation for TSON encoding and asset failures.

## Boundaries

There is no runtime TSON parsing or decoding, JSON, bytes API, Results/tables/optionality as TSON data, structural runtime objects, interfaces, aliases, cross-schema/cross-unit encoding, filesystem access, reflection, `dynamic`, property enumeration, or public runtime `TsonValue`. [CTS-TSON-ARRAY-M0b](copeland-ts-tson-arrays-and-assets-cts-tson-array-m0b.md) subsequently implements compiler-host arrays and compile-time asset lowering, and [CTS-TSON-ARRAY-M1](copeland-ts-runtime-tson-array-encoding-cts-tson-array-m1.md) adds only bounded nested runtime array encoding.

## Closeout

The finite requirement ledger contains no `Missing` rows. It maps every original M2c requirement to direct evidence, stronger combined evidence, or a stated language/MIR construction exclusion. The non-TSON JavaScript runtime regression additionally proves that copied record and enum Symbol slots fail ordinary field access and matching, so the provenance repair is not TSON-specific.
