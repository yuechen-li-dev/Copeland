# CTS-JS-PERF-M1 — Production JavaScript representation

## Executive conclusion

CTS-JS-PERF-M1 gives Copeland JavaScript a fair production representation. The M0 record/enum losses were overwhelmingly backend-emission cost, not a V8 limitation. On the same machine, runtime versions, authored sources, iteration counts, warm-up, and 30-round protocol, production V8 changes the typed reducer from 3312.712 ms to 8.077 ms median and record/array transformation from 2225.735 ms to 11.336 ms. All C#/validated-JS/production-JS checksums match exactly.

RyuJIT remains faster for numeric work and the affine Machina subset on this run, while production V8 wins the reducer. V8 retains the cold-start advantage. Copeland therefore has two competitive mature-runtime backends for the M0 corpus; selection should be driven by host, deployment, startup, ecosystem, and workload rather than the former validated-JS artifact cost.

## Profiles and trust model

The new `Production` profile is explicit. `Diagnostic` and `Symbolic` remain the checked default representations.

| Profile | Internal representation law | Intended use |
| --- | --- | --- |
| `Diagnostic` / `Symbolic` | Frozen null-prototype values; `WeakSet` provenance and validation on each nominal read/match | Compiler development and hostile-interop diagnostics |
| `Production` | Stable generated shapes; direct trusted reads/matches; validators retained at explicit boundaries | Release Node/browser artifacts |

Trusted values are constructed by generated code, returned from generated calls, passed between generated modules, or generated zero-payload enum singletons. Raw host/npm/browser/dynamic-import/deserialized values are untrusted. Production nominal validators reject plain or forged lookalikes using compiler-private token identity, exact shape, and recursive type checks. Project ESM emission marks exported functions as boundaries and validates their typed parameters in production before entering trusted code.

The standalone emitter does not expose private constructors/tokens. General structural external-data-to-nominal projection is consciously deferred: accepting a plain structurally compatible object would weaken nominal identity. Existing primitive/callable host and npm contracts remain unaffected.

The canonical profile/ownership law is documented in `docs/Copeland/reference/javascript-emission-profiles.md`.

## Representation changes

### Records

Validated construction:

```js
const value = Object.create(null);
Object.defineProperties(value, { [type]: { value: type }, [field]: { value: field0 } });
Object.freeze(value);
instances.add(value);
```

Production construction:

```js
return { [typeToken]: typeToken, $f0: field0, $f1: field1 };
```

The private symbol token preserves nominal identity. `$fN` order follows declared record-field order. Generated trusted reads use `value.$fN`; `with` evaluates replacements once and constructs a new stable object from direct fields. It never mutates the source.

### Enums

Validated construction allocates and freezes a `$payload` array for every case. Production uses direct `$type`, `$tag`, and `$pN` fields. A zero-payload case is one frozen singleton per enum case:

```js
const Increment = Object.freeze({ $type: CounterEventToken, $tag: "Increment" });
```

Payload cases allocate a fresh stable object but no payload array. Trusted matching is `switch (value.$tag)` and payload binding is direct `$pN` access; it performs no repeated nominal validator call.

### Validation and immutability

Production validators remain emitted and verify type token, exact keys/symbol count, case/arity, and field/payload types. They reject a plain record, an externally mutated trusted record, a forged enum token, and mismatched nominal types. The focused Node test exercises these cases.

Production internal immutability is compiler-enforced: no Copeland lowering writes record or enum fields. Internal records/payload enum values are deliberately not frozen per construction; the host ownership law does not expose them as general mutable JavaScript data. Canonical zero-payload enum objects are frozen because they are shared.

## Benchmark protocol and checksums

The exact five M0 workload files under `tools/CtsJitM0/Workloads` were reused unchanged. Each benchmark run used 10 cold-process samples, 10 warm-up calls, and 30 warm in-process rounds. The validated rerun uses `Symbolic`; production uses `Production`. Both builds compile the same source alongside the same Release C# host.

| Workload | Checksum | Iterations / round |
| --- | ---: | ---: |
| Numeric kernel | 294727 | 10,000,000 |
| Machina affine-layout subset | 859540320 | 4,000 batches |
| Typed reducer | 1000001 | 2,000,000 |
| Record/array transform | 999766 | 2,000,000 |
| String processing | 12248977 | 250,000 |

The full C# Machina resolver still has no symmetric ordinary generated-JS implementation. The shared affine subset remains the exact, honest cross-backend workload; M1 does not claim full Machina parity.

## Warm throughput (ms; median, p10–p90)

| Workload | RyuJIT | V8 validated | V8 production | Production result |
| --- | ---: | ---: | ---: | --- |
| Numeric | 31.782 (31.506–31.922) | 33.995 (33.868–34.151) | 33.929 (33.881–34.087) | Near parity; RyuJIT 1.07× faster |
| Machina subset | 2.258 (2.240–2.326) | 824.064 (809.100–845.362) | 3.468 (3.435–3.525) | 238× validated improvement; RyuJIT 1.54× faster |
| Reducer | 10.366 (10.222–20.895) | 3312.712 (3283.237–3338.437) | 8.077 (7.950–10.134) | 410× validated improvement; V8 1.28× faster |
| Record/array | 5.091 (5.059–5.176) | 2225.735 (2183.551–2242.118) | 11.336 (11.218–11.576) | 196× validated improvement; RyuJIT 2.23× faster |
| Strings | 9.781 (9.408–9.985) | 44.286 (43.988–44.512) | 44.730 (44.587–46.023) | Representation-neutral; RyuJIT 4.57× faster |

The numeric control remains effectively unchanged, while every record/enum workload improves dramatically. This isolates the M0 loss to representation and repeated validation rather than V8 scalar execution.

## Cold, heap, and artifact observations

| Workload | Validated cold median | Production cold median | Validated heap delta | Production heap delta | JS bytes validated → production |
| --- | ---: | ---: | ---: | ---: | ---: |
| Numeric | 44.368 | 42.016 | 8,336 | 8,336 | 294 → 341 |
| Machina subset | 48.349 | 43.973 | 25,312,552 | 138,520 | 7,892 → 8,914 |
| Reducer | 42.840 | 42.135 | 28,217,736 | -579,456 | 4,045 → 4,642 |
| Record/array | 43.723 | 41.542 | 23,994,296 | 189,520 | 5,194 → 6,571 |
| Strings | 41.582 | 42.341 | 8,184 | 8,272 | 535 → 594 |

Heap deltas are coarse `process.memoryUsage().heapUsed` observations, not allocation equivalence. Negative production reducer delta means GC ran. Production code is slightly larger because it retains boundary validators, but removes the per-operation work that dominated M0.

## Generated-code and V8 evidence

Production reducer output contains a direct `Reduce` switch, direct `$f0`/`$f1` reads, and cached enum values. It has no `WeakSet`, `Object.defineProperties`, or record validator call inside `Run`/`Reduce`. Validated output retains all three plus a frozen payload array per event.

`node --trace-opt --trace-deopt` on the production reducer shows the generated record constructor, `Reduce`, and `Run` are selected for Maglev and TurboFan optimization. The trace still shows a generic-named-access deoptimization around host/measurement activity after OSR; it does not prevent the stable 8.077 ms measured median. The validated trace spends hot-path work in record/enum validator and constructor helpers and reports generic keyed-access feedback, matching the benchmark gap.

## Security and compatibility evidence

Focused tests cover profile selection, deterministic production emission, direct production fields/payloads, immutable `with`, zero-payload singleton construction, semantic equivalence, and adversarial rejection. The adversarial test proves that:

- a production record modified after construction fails its boundary validator;
- a plain `{ $f0, $f1 }` object fails nominal validation;
- an enum with a forged token/tag fails validation.

Exported functions in ESM project emission receive parameter validation in production, giving host/browser callers an explicit entry boundary. Internal direct calls are not revalidated. Callable representation was intentionally unchanged: M0 does not exercise callable carriers, and direct known calls are already ordinary JS calls.

One narrow correctness fix was also made: JavaScript boundary validation now recognizes `int` and `float` as JavaScript numbers rather than treating them as impossible payload types. This changes an inherited symbolic corpus artifact; no validated baseline was updated.

## Updated decision matrix

| Concern | RyuJIT | V8 production | Recommendation |
| --- | --- | --- | --- |
| Cold script/process startup | ~61–63 ms | ~42–44 ms | V8 for short-lived scripts |
| Numeric | Slight lead | Near parity | Host/ecosystem decision |
| Immutable reducers | Competitive | Slight lead | Both credible |
| Record/array transforms | Lead | Competitive | RyuJIT for throughput-sensitive typed data |
| String-heavy work | Clear lead | Functional but slower | RyuJIT where sustained text throughput matters |
| Browser-native application | Requires host bridge | Native engine fit | V8 production |
| Server/desktop typed logic | Native CLR integration | Viable | RyuJIT preferred today |
| Controlled Copeland shell | Strong | Compatibility/host role | RyuJIT logic plus narrow host remains plausible |

## Profile policy and remaining work

Recommendation: retain checked `Diagnostic`/`Symbolic` as the default compiler-development profiles; select `Production` explicitly for release Node/browser artifacts. This avoids silently changing interop expectations while making a production path available now.

Remaining deficits:

- generalized structural external-data projection into nominal records/enums needs an explicit ownership/identity contract;
- production ESM cross-module tests should be expanded around independent realms and exported nominal constructors;
- results/tables still use the checked tagged carrier representation;
- the full Machina resolver remains C#-hosted.

Recommended next milestone: **CTS-JS-BOUNDARY-M2**, limited to explicit host/npm/browser nominal projection and ownership/copy semantics, with independent ESM-module adversarial tests. Do not broaden into browser packaging or a full Machina port.

## Evidence and validation

- Profile documentation: `docs/Copeland/reference/javascript-emission-profiles.md`
- Harness: `tools/CtsJitM0/Program.cs`
- Raw validated/prod results and artifacts: `artifacts/cts-js-perf-m1/` (machine-local, ignored)
- Production security/runtime test: `tests/Copeland/Copeland.TS.Backend.JavaScript.Tests/JavaScriptRuntimeTests.cs`
- Profile CLI and deterministic corpus tests: `tests/Copeland/Copeland.Cli.Tests/`

CTS-JS-PERF-M1 is complete for the bounded production representation: it establishes stable trusted records/enums, direct internal access, explicit generated validators, boundary parameter checks for project exports, and a fair re-characterization. It deliberately does not claim general structural external-data projection or full Machina JS parity.
