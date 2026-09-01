# Copeland TS generated JavaScript burn-in review

## Verdict

**JS_BACKEND_VERBOSE_BUT_SOUND.** The implementation uses JavaScript as a
realization substrate rather than an erasure target. After fixing async record
construction/access, all four runtime programs emit deterministic JavaScript and
execute with stable Node output. The largest pressure is table runtime size, not
semantic ambiguity.

## Program measurements

The checked-in manifest is authoritative for exact timings and hashes. Compiler
timings are one warmed in-process coarse measurement. Node timing is the median
of five fresh processes and includes startup.

| Program | Source LOC / bytes | JS LOC / bytes | Size ratio | Carriers | Helpers | Node output |
|---|---:|---:|---:|---:|---:|---|
| Application | 212 / 4,893 | 629 / 40,683 | 8.315 | 10 (4 inferred) | 28 | `1354` |
| Tables | 106 / 2,569 | 690 / 45,849 | 17.847 | 1 + table row/runtime | 20 | `194` |
| Flow | 116 / 2,826 | 419 / 21,319 | 7.544 | 2 | 7 | terminal `Completed`, board total `24` |
| AsyncBatchGenerator | 81 / 1,915 | 276 / 14,213 | 7.422 | 3 (1 inferred, 1 class) | 11 | `30`, `ok`, `84`, `0,1,2,3,4` |
| Metaprogramming | 90 / 2,168 | none | none | none | none | 15 compile-time artifacts |

## Source-to-JS mappings

| Program | Source construct | Semantic lowering | Generated JS pattern | Assessment |
|---|---|---|---|---|
| Application | `const parcel = Parcel(...)!` | class constructor as Result-returning function plus unwrap | ordinary function call, Result validator, frozen class carrier | CLEAR; no host constructor semantics |
| Application | private class field | private record field provenance | non-exported Symbol slot and WeakSet-branded null-prototype value | VERBOSE_BUT_REASONABLE; stronger than TS erasure |
| Application | `Parcel.adjust(parcel, 2)` | associated function and owner-authorized record-with | ordinary function and complete carrier reconstruction | CLEAR |
| Application | nested inferred `with` | interned record type plus ordered replacements | staged receiver/replacement temporaries and one carrier call per level | VERBOSE_BUT_REASONABLE; preserves order |
| Application | Result `?` / `!` | explicit tag branch and propagation/unwrap target | validated `$tag`/`$payload` checks | VERBOSE_BUT_REASONABLE |
| Tables | authored table columns | table constant/singleton | frozen column arrays plus a branded table singleton | VERBOSE_BUT_REASONABLE |
| Tables | `Readings[index]?` row view | row access returning `Result<Row, TableBoundsError>` | checked index and branded lightweight row reference | VERBOSE_BUT_REASONABLE |
| Tables | column access and bounds match | column getter plus payload enum match | column provenance validation and `$tag` branches | SUSPICIOUS in aggregate size, semantically clear |
| Flow | guarded self-transition | flow graph transition | `switch (state)`, guard, explicit `Unhandled` result | CLEAR |
| Flow | adjacent board assignments | immutable board updates | staged object spreads in authored order | VERBOSE_BUT_REASONABLE |
| Flow | `finish` / `fail` states | terminal contract | terminal flag, completed/failed result, later `Terminal` response | CLEAR |
| Metaprogramming | `fieldsOf` / `enumCasesOf` | bound typed metadata arrays | no runtime JS | CLEAR; complete erasure |
| Metaprogramming | `callsOf<CompileService>` | compiler-owned direct call sites | no runtime JS; artifact paths retain source correlation | CLEAR |
| Metaprogramming | repeated `Label<"same">` | memoized static instantiation | no runtime JS; equal artifact bytes | CLEAR |
| AsyncBatchGenerator | generator delegation | generator MIR | native `function*`, `yield`, `yield*` | CLEAR |
| AsyncBatchGenerator | batch map | bounded batch value block | indexed input/output loop | CLEAR; intentionally serial in JS |
| AsyncBatchGenerator | async Result propagation | suspension automaton | frame plus explicit `switch` states and computation subscriptions | VERBOSE_BUT_REASONABLE |
| AsyncBatchGenerator | inferred/class records after await | record MIR inside suspension states | active profile constructors and Symbol-slot access | CLEAR after CTS-BURN-001 fix |

## Runtime carriers

Application emits six declared carriers (including the pure class) and four
inferred carriers. Equal ordered `{ x, y }` shapes reuse one carrier across
multiple functions/literals. Reordered `{ y, x }` is distinct. The nested
`{ name, position }` shape and the evaluation-order trace shape are separately
interned. AsyncBatchGenerator adds one inferred carrier beside its declared
record and pure class.

Pure classes reuse the record runtime with retained class provenance. Diagnostic
JavaScript realizes them as private Symbols, frozen null-prototype values, and a
WeakSet membership check. This is a concrete advantage over traditional
TypeScript erasure: private/invariant provenance survives runtime emission
without adopting JavaScript prototype or mutation semantics.

## Helpers and duplication

The mechanical count records functions whose names begin `__cope_`. Within each
artifact, helper definitions are emitted once per required runtime role; manual
inspection found no identical repeated definitions. Most apparent repetition is
type-specific validation rather than duplicate helper text:

- Result tokens/validators differ by concrete `T ! E` identity.
- record/class validators differ by field slots and provenance sets.
- table bounds, columns, rows, enums, and nested cell validators account for the
  table artifact's 20 helpers.
- FLOW owns a single session/result implementation for its definition.

This pass does not recommend blind helper deduplication. Table helper factoring
is worth measuring because the 17.847 ratio is material; cross-artifact shared
runtime design would carry module/provenance risk.

## Evaluation order

Application deliberately traces record initializer and `with` replacement
evaluation. Node returns the expected staged value, and generated JS introduces
temporaries in source order before declaration-order carrier construction.
Async lowering similarly evaluates record inputs into frame slots before the
carrier call. Arguments, binary operands, Result propagation, table indexes, and
FLOW board updates remain visibly ordered in emitted code.

## Async correctness fixes

Before the fix, Diagnostic-profile async emission generated
`__cope_record_00720032(...)` and `.$f0`/`.$f1` while the same artifact declared
profile-owned Symbol-based record functions. Node failed with a `ReferenceError`.
The emitter now resolves the `MirRecordDefinition`, orders initializers by its
fields, selects the active profile constructor, and selects production fields or
Diagnostic/Symbolic slots consistently.

The same program found the corresponding C# gap: async state emission did not
support record/class construction or field access and lacked function-local
record catalog state. Both backends now consume the same valid MIR composition.

## Determinism and runtime behavior

Every runtime program is compiled twice in one run; MIR and JavaScript must be
byte-identical. Each generated script is executed five times; output must be
identical. SHA-256 values for generated JS and runtime output are retained in the
manifest. Template evaluation is repeated and its path/length/hash summary must
match. C# is emitted for all runtime programs, and Application/Tables are
compiled with Roslyn and invoked for parity with the Node observations.

## Optimization opportunities

| Current output | Likely cause | Opportunity | Semantic risk | Expected benefit |
|---|---|---|---|---|
| Tables: 45,849 bytes from 2,569 source bytes | per-artifact table/row/column/result/enum validation | measure a backend-local shared helper factoring plan | provenance and standalone artifact behavior | material only for table-heavy output |
| Application: 40,683 bytes | ten nominal/inferred carriers plus multiple result/enum validators | dead carrier/helper elimination only if reachability proves unused definitions | reflection/export/boundary reachability | moderate |
| FLOW repeated board spreads | one complete immutable update per authored assignment | coalesce adjacent updates after evaluation-order proof | guards, reads between updates, observable order | moderate for update-heavy flows |
| async frame switch | explicit suspension automaton | local state/temporary simplification | cancellation/propagation correctness | small-to-moderate |

The evidence supports local simplification and table helper measurement, not an
optimizer framework. MIR remains sufficient: the failures were backend name and
catalog routing bugs, not missing intermediate semantics.

## Traditional TypeScript constraint comparison

Traditional TypeScript normally erases its added semantics while preserving
ordinary JavaScript objects, classes, Promises, and mutation. Copeland can select
stronger runtime realization: branded frozen values, private fields that remain
private, explicit Result tags, bounds checks, columnar tables, deterministic
FLOW sessions, and compiler-owned async state. The tradeoff is larger generated
code and a debugging surface that is compiler-shaped. In this corpus that trade
is healthy except for the table size signal.
