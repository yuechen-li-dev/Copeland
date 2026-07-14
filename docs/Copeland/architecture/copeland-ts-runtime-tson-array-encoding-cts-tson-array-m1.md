# Copeland TS runtime TSON array encoding: CTS-TSON-ARRAY-M1

**Status:** implemented.

`tsonEncode` now accepts a same-compilation-unit, same-schema nominal record or payload-enum root whose reachable fields or payloads contain homogeneous arrays of Boolean, Number, String, nominal Record, nominal Enum, or nested arrays. Root arrays remain invalid. Structural objects, Results, tables, optionality, interfaces, aliases, heterogeneous arrays, tuples, runtime decoding, JSON, and new collection APIs remain excluded.

## Shared plan and canonical form

`MirTsonArrayPlan(elementPlan)` is a structural node of the existing demand-created `MirTsonEncodingPlan`. It has no stable identity; reachable nominal record and enum elements retain their stable identities and declaration ordering. The shared validator checks array element/type agreement, recursively visits array schemas for reachability and cycles, rejects invalid roots and unsupported families, and requires the fixed maximum array length of 100,000 before either backend emits.

Canonical declaration text uses ordinary `T[]` syntax. Empty arrays retain their static element schema through the plan and print as `[]`; nonempty arrays use M0b four-space multiline layout, element-order commas, and the final document LF.

## Runtime law

The root operand is evaluated once. At every array entry the generated writer captures its carrier once, captures length once, rejects a length above 100,000 before reading an element, and reads each index once in ascending order. It observes the ordinary mutable carrier state at the call under ordinary synchronous execution; it does not clone arrays, preserve aliases, or promise concurrent snapshot isolation.

The ordinary error precedence is array length, then per-string UTF-16 length, invalid Unicode, then total canonical UTF-8 output. Length/string/output failures return the existing `OutputLimitExceeded`; invalid strings return `InvalidUnicode`. Host-mutated malformed carriers, holes, and wrong values are terminal generated-runtime invariants, not new Result cases.

## Backend strategy and evidence

C# emits statically typed `T[]` helpers with `array.Length` and indexed `for` traversal. JavaScript validates `Array.isArray`, rejects holes with direct own-index checks, captures `array.length`, and uses indexed `for` traversal without enumeration, copying, JSON, reflection, or schema discovery. Both recurse only through validated finite schema structure.

`TsonEncodeRuntimeTests.Both_backends_encode_nested_arrays_with_canonical_schema_evidence` proves exact C#/Node parity for primitive, record, enum, nested, and empty arrays; the emitted result reparses with `CanonicalTson` and canonically reprints byte-identically. `TsonEncodeFeatureTests.Supported_nested_arrays_build_one_structural_plan` proves binder/MIR shape and demand planning. Existing non-array corpus hashes remain stable because array-only helpers are emitted only for plans that contain arrays.

The next recommended milestone is a focused expansion of two-generation asset-array fixed-point and malformed-array-plan corpus coverage, without broadening runtime decoding or the TSON data algebra.
