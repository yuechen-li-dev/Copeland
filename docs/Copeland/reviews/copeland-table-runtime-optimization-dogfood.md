# Table runtime optimization dogfood — CTS-OPT-M1

CTS-OPT-M1 is **Outcome A** and lands as `PRODUCTION_REPLACEMENT`. The shared
trusted scaffold materially reduces emitted code, startup is flat in the coarse
process benchmark, every measured access path improves, and semantic/security
qualification remains green.

## Scorecard

| Metric | Old | New | Change |
| --- | ---: | ---: | ---: |
| Production Tables JS | 43,535 B | 36,450 B | -7,085 B (-16.274%) |
| JS/source ratio | 16.946 | 14.188 | -2.758 |
| repeated column wrappers | 6,340 B | 1,105 B | -5,235 B (-82.571%) |
| validator call sites | 70 | 25 | -45 (-64.286%) |
| top-level functions | 27 | 30 | +3 shared helpers |
| emitted closures | 8 | 4 | -4 |
| `Object.freeze` calls | 23 | 15 | -8 |
| `Object.defineProperties` calls | 7 | 3 | -4 |
| Symbols | 20 | 20 | unchanged |
| fresh Node startup median | 39.454 ms | 39.110 ms | -0.344 ms |
| row access | 3,563.029 ns | 2,200.180 ns | -38.25% |
| column access plus checked cell | 1,072.222 ns | 429.327 ns | -59.96% |
| direct checked cell | 1,095.610 ns | 431.685 ns | -60.60% |
| ten-cell sum query | 2,327.980 ns | 2,030.975 ns | -12.76% |

Startup uses 15 fresh Node v26.2.0 processes per artifact. Steady-state values
are medians of seven fresh processes after in-process warmup. These are bounded
signals, not general JavaScript benchmark claims. Raw samples are in
`artifacts/cts-opt-m1/runtime-comparison.json`.

## Byte attribution

M0's 6,340 repeated bytes were five copies of carrier creation, descriptor
setup, bounds branches, Result construction, and freezes. The replacement has
1,912 fixed bytes for the shared row/read/column scaffold and 1,105 repeated
wrapper bytes across five columns. The 1,062-byte literal payload is unchanged.

Validator definitions remain because runtime boundaries need them. Calls at
trusted typed access sites fall from 70 to 25; remaining calls are boundary and
genuinely dynamic checks.

## Scaling

| Axis | Case | Old | New | Savings |
| --- | ---: | ---: | ---: | ---: |
| tables | 1 | 11,130 B | 10,014 B | 1,116 B |
| tables | 10 | 81,424 B | 53,907 B | 27,517 B |
| columns | 1 | 9,276 B | 9,208 B | 68 B |
| columns | 20 | 45,009 B | 24,907 B | 20,102 B |
| rows | 0 | 11,122 B | 10,006 B | 1,116 B |
| rows | 10,000 | 128,922 B | 127,794 B | 1,128 B |

For the two-column row sweep, non-payload scaffold stays near 10 KB while
payload grows to 117,776 bytes. Scaffold/payload is 1.024 at 1,000 rows and
0.085 at 10,000 rows. Exact 1/2/5/10 table, 1/2/5/10/20 column, and
0/1/10/100/1,000/10,000 row results are in the two scaling JSON artifacts.

## Generated shape

```javascript
const storage = [/* compiler-emitted values */];
const column = __cope_table_trusted_column(
    compilerSelectedColumnToken,
    storage,
    compilerKnownRowCount,
    concreteResultToken);
```

The generic reader receives known row count and identities. It never discovers
column count, type, identity, or rectangularity from data. Diagnostic retains
the explicit shape, and focused runtime outputs are identical across profiles.

## Semantic dogfood

Coverage includes empty, one/multi-column, primitive, record, payload enum, and
Result cells; row, column, cell, and query access; all numeric bounds classes;
nominal cross-table rejection; deterministic Production emission; and no-table
zero-runtime. Existing table/TSON suites cover the broader corpus. Diagnostic,
Production, and C# observable outputs agree for the representative program.

The C# backend is deliberately unchanged. Its typed carrier construction
already follows construct-then-publish without JavaScript descriptor costs.

## Tradeoffs and recommendation

Function count rises by three because the shared scaffold is explicit. It
replaces four closures and five repeated code blocks. Table validators, Result
identities, symbols, and static slots remain semantic boundaries or inputs to
later reachability analysis.

Proceed next to **module-local generated-definition reachability DCE**, using
the M0 inventory as roots. Do not compress identity or bounds carriers first:
the dominant repeated table shape is removed, and the next measured residue is
unreachable definitions.
