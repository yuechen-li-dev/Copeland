# CTS-OPT-M0 compiler optimization reconnaissance

> **CTS-OPT-M2 resolution (2026-09-01):** the JavaScript emitter now owns a
> deterministic generated-definition graph and removes unreachable record/class
> carrier families and record/Result validators after MIR validation. The
> post-M1 Production burn-in falls from 94,652 to 84,499 bytes while preserving
> Node output. See the CTS-OPT-M2 architecture and dogfood reports.

> **CTS-OPT-M1 resolution (2026-09-01):** Production table realization now
> uses one module-local trusted scaffold over compiler-known column payloads.
> Tables fell from 43,535 to 36,450 bytes; repeated column wrappers fell from
> 6,340 to 1,105 bytes; validator call sites fell from 70 to 25. The remaining
> recommended optimization is module-local generated-definition reachability
> DCE. See the CTS-OPT-M1 architecture and dogfood reports.

## Outcome

**Outcome B — repeated removable scaffolding materially affects size and some
runtime paths.** Copeland's runtime mechanisms are mostly semantic enforcement,
not accidental ceremony. The concrete removable pressure is narrower:
unreachable generated definitions, repeated table column wrappers, and repeated
validation of compiler-owned Result/table values.

The existing Production profile is important evidence. Across the four runtime
programs it reduces 122,066 Diagnostic bytes to 101,737 bytes (16.65%) while
preserving output. It already trusts compiler-created record and enum values.
Tables remains 43,535 bytes and 16.946 times source size, however, with 70
validator call sites versus 79 in Diagnostic. The next optimization work belongs
in Result/table emission and generated-symbol reachability, not in a new generic
optimizer framework or a new release profile.

M0 also found and repaired a real Production table bug. Production omitted enum
WeakSets but table constants still registered into them, then constructed bounds
errors with the Diagnostic payload shape. The table program now executes in
both profiles with identical output hashes.

## Baseline corpus

MIR node count is a reference-distinct walk of public objects in the
`Copeland.TS.Mir` namespace, excluding identity records whose names end in
`Id`. Fresh Node time includes process startup. Steady time is the median of
five processes after 1,000 warmup calls and 20,000 measured `main` calls.

| Program | Source LOC / bytes | MIR nodes | Diagnostic JS LOC / bytes / ratio | Production JS LOC / bytes / ratio | helpers / carriers / validators | closures / functions / top-level statements | fresh Node ms | steady Diagnostic / Production |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Application | 212 / 4,893 | 800 | 629 / 40,683 / 8.315 | 503 / 28,097 / 5.742 | 28 / 10 / 15 | 6 / 53 / 99 | 34.669 | 29.087 us / 7.679 us |
| Tables | 106 / 2,569 | 527 | 690 / 45,849 / 17.847 | 671 / 43,535 / 16.946 | 20 / 1 / 14 | 8 / 35 / 61 | 34.609 | 72.545 us / 70.240 us |
| Flow | 116 / 2,846 | 355 | 419 / 21,321 / 7.492 | 392 / 18,542 / 6.515 | 7 / 2 / 3 | 1 / 11 / 23 | 33.704 | 1.889 us / 0.186 us |
| Async/Batch/Generator | 81 / 1,915 | 430 | 276 / 14,213 / 7.422 | 247 / 11,563 / 6.038 | 11 / 3 / 4 | 7 / 31 / 33 | 34.936 | 2.594 us / 2.593 us |
| Metaprogramming | 90 / 2,202 | compile-time | 0 | 0 | 0 | 0 | N/A | N/A |

An empty fresh Node process measured 31.822 ms. The seven-run generated-script
startup medians were Application 33.282 ms, Tables 34.643 ms, Flow 33.002 ms,
and Async/Batch/Generator 32.866 ms. Tables has the largest measured parse and
top-level initialization increment, about 2.821 ms over the empty-process
median. These are coarse signals, not benchmark claims.

## Emitted cost by feature

### Application

Diagnostic bytes divide into 560 runtime helper, 17,291 record/class carrier,
2,015 enum, 4,128 Result, and 16,689 user logic/staging bytes. Production cuts
the artifact by 30.9% and the steady `main` signal by about 3.8 times. That is
consistent with Production's compact record fields and omitted internal
record/enum checks.

Named and inferred record shapes are interned. Pure classes deliberately use
the same nominal carrier machinery plus owner-authorized construction and
updates. WeakSets, Symbols, freezing, private slots, and counterfeit rejection
are load-bearing in checked profiles. Production's compact representation is a
valid trusted path because explicit boundary validation remains available.

`with` reconstructs complete immutable values and stages replacements in source
evaluation order. There are 55 single-use generated locals, but no direct
`const t = expr; return t;` peephole. The corpus does not justify weakening
ordering or adding a temporary optimizer.

### Tables deep dive

| Region | Lines | Bytes | Classification |
|---|---:|---:|---|
| core value/unwrap helpers | 1-19 | 630 | mandatory |
| record cell carrier | 20-42 | 1,512 | mandatory |
| bounds/cell enum runtime | 43-108 | 3,060 | mandatory |
| eight closed Result identities/validators | 109-252 | 8,284 | mandatory shape, one dead validator |
| column protocol | 253-263 | 784 | mandatory, shareable |
| table/row/column identity | 264-277 | 981 | mandatory |
| table validator | 278-288 | 1,210 | boundary/load-bearing |
| row validator | 289-295 | 707 | boundary/load-bearing |
| row construction | 296-305 | 554 | mandatory |
| five column wrappers and table singleton | 306-409 | 9,404 | mandatory semantics, duplicated implementation |
| authored access logic and staging | 410-690 | 18,723 | logic plus redundant internal validation |

Validator definitions occupy 13,616 Diagnostic bytes and the artifact has 79
validator call sites. Production still has 70. Result values are commonly
validated after construction, aliased for propagation/unwrap/match, and
validated again. Table and column receivers are likewise checked at internal
compiler-owned use sites.

The code-versus-data answer is unambiguous. The five column blocks contain only
1,062 bytes of literal storage and 6,340 bytes of wrapper code. Each wrapper
repeats finite/integral checks, bounds checks, concrete Result creation, private
tokens, and freezing. A module-local factory can share implementation while
receiving those identities as private data. A global runtime library is not yet
justified.

The table bounds checks, Result identity, schema/table/row/column tokens,
immutable storage, and counterfeit rejection must remain. The optimization is
to share implementation and trust compiler-owned results, not to change table
law.

### Flow

Diagnostic bytes divide into 391 runtime helper, 3,694 carrier, 1,826 enum,
2,287 authored logic, and 13,123 FLOW runtime bytes. The FLOW runtime's state
switch, guards, revision, reentrancy, board replacement, terminal checks, and
result objects are semantic. Production reduces steady `main` from about 1.889
us to 0.186 us by avoiding internal carrier checks.

The board MIR record carrier is nevertheless emitted even though the dedicated
FLOW runtime uses its own board object. Its constructor/validator/tokens are an
exact 2,182-byte dead family in Diagnostic. This is reachability pressure, not a
reason to redesign FLOW.

### Async, batch, and generator

Diagnostic bytes divide into 1,305 async core, 305 general helpers, 4,561
record/class carriers, 1,017 Result runtime, 4,186 async state-machine bodies,
and 2,839 synchronous user/batch/generator logic. Generators use native
`function*`, `yield`, and `yield*`. Batch is one bounded serial loop; there is no
universal batch helper to remove. Async owns explicit terminal arbitration,
continuations, frame state, and propagation, all load-bearing.

The internal `__cope_async_pending` seam is unused by this local program but is
used by remote/TSON host paths and dedicated runtime tests. It should be
usage-gated, not globally deleted. The unused `Sample` carrier family and unused
inferred-record validator are ordinary generated-definition DCE candidates.
Production reduces source size by 18.6%, but this small synchronous `main`
workload shows no stable steady-state difference.

### Metaprogramming control

The 90-line program performs seven template instantiations and five reflection
queries, producing 15 deterministic artifacts. It emits zero runtime JavaScript.
`template`, `static`, `reflect`, aliases, and interfaces have no residue. Any
explicitly materialized runtime declaration remains a root.

## Dead code and reachability

The Diagnostic artifacts contain 6,037 exact bytes of unreachable generated
definition regions, 4.95% of their combined 122,066 bytes:

| Program | Dead generated region | Bytes |
|---|---|---:|
| Application | unused `Customer` require function | 714 |
| Tables | unused closed Result validator | 958 |
| Flow | unused board-record carrier family | 2,182 |
| Async/Batch/Generator | unused pending seam, `Sample` carrier family, inferred-record require | 2,183 |

Production exposes additional unused validators because trusted field access no
longer calls them. A safe pass needs roots for authored/public functions,
exports, top-level initializers, host/npm/CLR/remote/TSON boundaries, module
factories, and materialized artifacts. Library exports are roots even when the
local corpus does not call them. Application/link emission may later use
stronger reachability, but no linker was added here.

## Duplication and universal support

There are no byte-identical helper definitions repeated within one artifact.
The meaningful duplication is structural:

- record constructors and validators repeat by field count and private slots;
- closed Result validators repeat one common shell around distinct payload
  checks and type tokens;
- table columns repeat one bounds/Result wrapper per column;
- enum validators contain every case because a public value may arrive at a
  boundary even when local code constructs only a subset.

Table query helpers are not emitted universally in this table program. TSON
plans/codecs remain demand-driven. Closed generic specializations in the corpus
are all referenced; generic-specific DCE is not warranted.

## Validators, public boundaries, and trusted paths

Production already has `BoundaryFunctionNames` and skips internal record/enum
parameter and access validation while retaining explicit hostile-boundary
checks. The policy is correct. The incomplete portion is Result/table lowering,
which emits most of the same validation in both profiles.

Checks must remain for exported functions, module factories, npm/JavaScript
host calls and callbacks, CLR/remote transport, runtime deserialization, TSON
transport, and any untrusted carrier. Compiler-owned construction followed by a
compiler-owned match/access may use a trusted path. Distinct nominal identities
and exact errors remain mandatory in both cases.

## Result and enum runtime

Results use distinct type tokens for each closed `T ! E`, one frozen tagged
shape, and type-specific validators. The identities are semantic; the repeated
validator shell and repeated internal validation are not. A shared
module-local validator implementation may accept the Result token and payload
validators as immutable data.

Enums use per-enum identity and payload validation. Diagnostic provenance sets
defend against counterfeit values. Production uses compact `$pN` payload
fields and validates exact own properties at boundaries. The experimental fix
made table constants and `TableBoundsError` use that selected representation
instead of mixing profiles. Unused enum case removal is unsafe for public or
foreign input without link knowledge.

## Temporaries, constants, branches, and allocation

Single-use generated locals number 55 Application, 41 Tables, 2 Flow, and 3
Async. Most stage receivers, indexes, replacements, binary operands, or Result
propagation in authored order. No direct const-return peephole occurs. No
`if (true|false)`, literal switch discriminator, or direct literal arithmetic
residue was found. Constant folding and general temporary elimination are not
priority M0 findings.

`Object.freeze` occurs 17, 25, 9, and 8 times lexically in the four Diagnostic
artifacts; these are constructors and sites, not per-run allocation counts.
Result success paths allocate frozen tagged values, which dominates table
access. Error objects are not eagerly allocated on successful bounds checks.
Null-prototype, Symbol, and WeakSet machinery remains semantically justified.

## Node startup and runtime interpretation

Tables has the largest startup increment and the slowest hot `main`. Production
only changes Tables from about 72.545 us to 70.240 us because most table/Result
checks remain. Application and Flow improve materially under Production, which
validates the trusted-internal policy itself. Async's measured `main` is a small
synchronous collection/batch workload and does not measure the later `compose`
continuation path; no broader async performance claim is made.

## C# comparison

Generated C# sizes are Application 15,752 bytes, Tables 16,131, Flow 18,529,
and Async/Batch/Generator 15,335. The table C# backend shares one generic
`CopeResult<TValue,TError>` but specializes typed column classes. It has no
JavaScript counterfeit-carrier ceremony. Roslyn/JIT can remove dead locals,
constant branches, and inline small typed helpers more reliably than the JS
startup path, so duplicating a mature C# optimizer is not justified. Generated
source/tooling size and unused emitted types remain future DCE beneficiaries,
but JavaScript is the primary pressure.

## MIR optimization boundary

Verdict: **`MIR_NEEDS_SMALL_ANALYSIS_METADATA`.** MIR already represents typed
definitions, bodies, exports, imports, tables, flows, and materialized assets.
It is sufficient for authored-definition reachability, constant analysis, and
trusted/public classification. Backend-owned validators and helper tokens are
not MIR definitions, so the JS emitter needs a deterministic generated-symbol
dependency graph or usage summary. Module/compilation-unit scope is sufficient.
SSA, CFG optimization, whole-program linking, and text-level rewriting are not.

## Candidate scorecard

| Candidate | Safety | Complexity | Size benefit | Runtime benefit | Compile cost | Debuggability cost | Best layer |
|---|---|---|---|---|---|---|---|
| finish Production trusted path for table/Result internals | MEDIUM | MEDIUM | HIGH for Tables | MEDIUM | LOW | MEDIUM | MIR provenance plus JS backend |
| generated-definition reachability | MEDIUM | MEDIUM | MEDIUM | LOW-MEDIUM startup | LOW | LOW | JS backend structured emission |
| module-local table column factory | MEDIUM | MEDIUM | MEDIUM-HIGH | LOW/UNCERTAIN | LOW | MEDIUM | JS runtime-helper factoring |
| single-use temporary folding | MEDIUM | MEDIUM | LOW | LOW | LOW | HIGH without source maps | future MIR simplification |
| constant/branch folding | HIGH | LOW | NONE observed | NONE observed | LOW | LOW | binder/static or MIR |
| global external runtime library | LOW | HIGH | HIGH across artifacts | UNCERTAIN | MEDIUM | HIGH | not recommended |

## Experimental fix and before/after evidence

The only implementation change is narrow and semantics-preserving. Before it,
Production Tables failed during singleton initialization with
`ReferenceError: __cope_m3_instances_13 is not defined`; after the registration
repair it failed matching a bounds error because the payload still used the
Diagnostic shape. After routing table enum constants and bounds errors through
Production enum construction, both profiles produce runtime output SHA-256
`09cce4a6ef6781ba44aead7eaae84f580ba21bf794f0c6880b619288003c6915`.
Production Tables is 43,535 bytes with SHA-256
`d5e90b979f6127b192a1a070ff8979fec19ff584783efe902a32832e3a9768ea`.
A focused regression covers payload enums stored in a table and read/matched
through the Production path.

## Exactly three next recommendations

1. Finish the existing Production trusted-internal path for table and Result operations, preserving all validation at explicit public, host, transport, deserialization, and counterfeit boundaries.
2. Add deterministic module-local generated-symbol reachability rooted by exports, authored entrypoints, top-level initializers, interop contracts, module factories, and materialized artifacts; remove only unmarked compiler-private definitions.
3. Prototype one module-local table-column factory and compare bytes, fresh startup, and steady table access against the 43,535-byte Production artifact before considering any external runtime library.

## Validation and artifacts

The maintained burn-in now writes both Diagnostic and Production JavaScript,
compiles each input twice for deterministic output, and requires identical Node
output across profiles. Focused Production tests pass 2/2. Full solution and
playback validation is recorded in
`artifacts/cts-opt-m0/cts-opt-m0-validation.json`.

Machine-readable evidence is under `artifacts/cts-opt-m0/`: baseline metrics,
emitted inventory, findings, table breakdown, before/after proof, and JSON/text
manifests.

No language syntax or semantics changed. Bounds behavior, Result identity,
nominal branding, counterfeit rejection, immutability, evaluation order, and
deterministic output remain unchanged. Oblivion work, Theory UX, and native
graph work remain unchanged.
