# Generated-definition DCE dogfood — CTS-OPT-M2

CTS-OPT-M2 is **Outcome A**. A deterministic emitter-owned reachability graph
removes dead record/class carrier families and record/Result validators while
preserving runtime output, boundary rejection, nominal identities, and survivor
ordering.

## Production burn-in result

CTS-OPT-M0 estimated 6,037 dead Diagnostic bytes before CTS-OPT-M1. Recomputed
against the post-M1 Production emitter, the controlled DCE-off baseline is
94,652 bytes across the four runtime burn-ins. DCE reduces that to 84,499 bytes:
10,153 bytes and 16 generated definitions removed.

| Program | Before | After | Bytes removed | Definitions retained / removed |
|---|---:|---:|---:|---:|
| Application | 28,097 | 24,263 | 3,834 | 18 / 6 |
| Tables | 36,450 | 33,547 | 2,903 | 7 / 3 |
| Flow | 18,542 | 16,956 | 1,586 | 1 / 3 |
| Async/Batch/Generator | 11,563 | 9,733 | 1,830 | 3 / 4 |

The Application removals are six internal record/class validators unused by
the Production trusted path. Tables removes three closed Result validators;
the M1 shared table scaffold, tokens, storage, bounds behavior, and used Result
paths remain. Flow removes its unused board carrier family plus a separate dead
validator. Async/Batch/Generator removes the unused `Sample` carrier family and
three validators. Metaprogramming remains zero runtime bytes for the same
compile-time-erasure reason as before M2.

The focused graph fixture additionally proves a dead record carrier and
validator disappear while a live carrier's generated name is unchanged.
Boundary-function input validation roots the validator and transitively retains
its carrier. A direct graph test proves reachable and unreachable cycles,
self-reference, and a shared dependency.

## Runtime, startup, and compile cost

All four baseline/optimized artifacts execute through Node and produce
byte-identical observable output hashes. Updated Diagnostic and Symbolic corpus
artifacts parse and execute through the maintained backend suite. Production's
foreign-boundary test still rejects forged nominal records and enums.

Nine-process startup medians are coarse and mixed: changes ranged from about
1.0 ms faster to 0.7 ms slower in this run. This is ordinary fresh-process
noise, not a startup claim. Median emitter time over twenty post-warmup samples
changed by -0.177 to +0.010 ms; the graph introduced no measured compile-time
regression.

Exact samples and emission timings are in
`artifacts/cts-opt-m2/startup-comparison.json`. C# source is measured but
unchanged because the emitter-local policy is JavaScript-only.

## Unexpected roots and remaining pressure

Production exposes more dead validators than M0's checked-profile manual
inventory because trusted internal field access does not call them. Conversely,
module factories, explicit boundary functions, TSON writers, table constants,
and retained initializers correctly root their carrier or validator
dependencies. No initializer-bearing family was made deletable without a
structural purity proof.

The exact next recommendation is to stop and reassess the remaining Production
inventory. If another milestone is justified, qualify one additional
emitter-owned category—most plausibly the isolated async pending seam—using the
same definition-block graph. Do not broaden this into authored-function DCE or
a general optimizer.
