# Copeland TS runtime TSON array encoding: CTS-TSON-ARRAY-M1

**Status:** closed in this worktree. Closeout began from `0f7f69689e6278d3794a49d415f3760413c70a9d` on `main`, aligned with `origin/main`. No package/version, commit, push, publish, parser, or runtime asset API change was made by this milestone.

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

## Completion evidence

The representative checked-in corpus is `tests/Copeland/Copeland.TS.Tests/TsonEncoding/Corpus/arrays`. It uses the ordinary `tsonAsset` compiler-host route with a nominal `Packet` root, a `.obj.ts` authoring asset, generated MIR/C#/JavaScript artifacts, and exact canonical runtime text. It covers contextually typed empty arrays, homogeneous primitive arrays, nested arrays, record arrays, payload-enum arrays, declaration/element order, escaped/Unicode text with a surrogate pair, and `0`, `-0`, finite fractional, normalized NaN, positive infinity, and negative infinity binary64 forms.

| Artifact | UTF-8 bytes | SHA-256 |
| --- | ---: | --- |
| `main.ts` | 542 | `5F7506BE9A496A8B6970E48553D7AF8656A3EA1A28FFDF3BAD8C39AFBF2D4342` |
| `packet.obj.ts` | 1,089 | `8BDA38AB1B62167C8794F5864777312BA674EA08D47C73042F3634FB4D1FFB8C` |
| `expected.tson` | 1,476 | `3E9DC91E15DA05DEE0F41556225914C7AD375A0DE1AD928FE423EC8AA3E94E51` |
| `main.cope` | 2,907 | `CCC4064D7FAFCD393FDD4FB0DD4F4E229EE20087F19AD739BE8EC990900AFB37` |
| `main.g.cs` | 20,516 | `9D4EFAF8827733808FF4A560B85CA64BC204898C3C547A2BDFA0F432856566F0` |
| `main.g.js` | 25,224 | `1335FE7939F9CB535DCD0E8116F5F9B4F227FA2B82B37E4EEB6E8CE5DE817E15` |

`TsonEncodeRuntimeTests.Array_corpus_has_two_generation_csharp_node_fixed_point_and_pinned_artifacts` performs both generations. Generation 1 compiles the authoring asset, checks emitted files and repeated backend emission byte-for-byte, executes generated C# and Node, requires identical UTF-8 canonical text with one final LF and no BOM, then reparses/reprints it exactly. Generation 2 supplies those exact bytes to a fresh in-memory canonical `.tson` asset and proves C# and Node re-encode the same bytes. It also asserts that authoring/canonical asset paths and authoring comments occur in neither MIR, generated source, nor runtime text.

## Requirement ledger

| Requirement | Status | Exact evidence |
| --- | --- | --- |
| Structural plan, shared MIR validation, canonical `T[]`, nominal root only | Satisfied | `MirTsonArrayPlan`, `MirValidator.ValidateTsonEncodingPlan`, `MirTextWriter`; `MalformedTsonEncodingPlanValidationTests.Cases` |
| Primitive/nested/record/payload-enum/empty runtime parity and order | Satisfied | `Both_backends_encode_nested_arrays_with_canonical_schema_evidence`; ARRAY-M1 corpus fixed-point test |
| Binary64, escaping, Unicode, surrogate-pair corpus evidence | Satisfied | ARRAY-M1 `packet.obj.ts` and `expected.tson`; pinned corpus test |
| 99,999/100,000/100,001 reader limit; depth and total node limits | Satisfied | `TsonFeatureTests.Array_length_boundary_is_exact_and_reports_the_array_span`; `Array_depth_and_total_node_boundaries_are_bounded_without_stack_overflow` |
| Runtime length boundary and exactly-once indexed observation | Satisfied | `Array_runtime_carriers_have_exact_boundaries_and_javascript_terminal_invariants` |
| JavaScript sparse/non-array rejection; mutable private carrier distinction | Satisfied | `Array_runtime_carriers_have_exact_boundaries_and_javascript_terminal_invariants`; `JavaScriptArrayEmission_WritesOrdinaryArrayOutput` |
| Malformed array plans rejected before either backend artifact | Satisfied | `MalformedTsonEncodingPlanValidationTests` array mismatch/missing-element/limit cases and both-backend no-artifact assertion |
| Demand emission and unchanged ordinary Result/try flow | Satisfied | `Writer_helpers_are_demand_emitted_and_forbidden_runtime_apis_are_absent`; `Encoding_uses_existing_staging_for_once_order_and_result_flow` |
| Unicode/output precedence within array-capable encoder | Satisfied | `Utf8_output_and_per_string_limits_have_exact_shared_boundaries`; array carrier boundary test |
| CLI fresh/repeated artifacts, malformed no-partial output, stale hash preservation | Satisfied | `CliIntegrationTests.Array_m1_corpus_cli_emission_is_fresh_repeatable_and_preserves_stale_artifacts_on_failure` |
| Two-generation compiler-host fixed point and no retained source path/comments | Satisfied | `Array_corpus_has_two_generation_csharp_node_fixed_point_and_pinned_artifacts` |
| Pinned source/generated/runtime hashes | Satisfied | ARRAY-M1 corpus fixed-point test and inventory above |
| Runtime parser/filesystem/JSON/reflection and excluded feature expansion | Satisfied by inspection | Generated-source forbidden API assertions; `Writer_helpers_are_demand_emitted_and_forbidden_runtime_apis_are_absent` |

The ledger has 13 rows: 12 `Satisfied`, 1 `Satisfied by inspection`, and zero `Missing`. `TsonArray` remains immutable semantic data. Generated C# arrays and JavaScript arrays are private, ordinary mutable runtime carriers; canonical encoding observes their state at the moment `tsonEncode` executes. Array identity, aliasing, and mutation history are not represented. TSON remains a finite immutable tree model.

No root arrays, runtime parsing/decoding/filesystem access, JSON, TSON Results/tables/optionality/interfaces/aliases, collection APIs or mutation syntax, reflection/`dynamic`/property enumeration, public runtime models, direct IL emission, NativeAOT claim, package update, commit, push, or publish was added. ARRAY-M1 is therefore honestly closed.
