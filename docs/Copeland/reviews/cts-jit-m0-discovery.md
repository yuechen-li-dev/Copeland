# CTS-JIT-M0 — RyuJIT vs V8 backend characterization

## Executive conclusion

On this Windows x64 workstation, the current generated Copeland C# backend is faster than the current generated Copeland JavaScript backend for every warm, typed workload measured. The advantage is modest for the numeric kernel (32.123 ms versus 34.155 ms median), material for strings (14.928 ms versus 45.691 ms), and overwhelming for the current immutable-record, enum, and Machina-subset workloads.

This is a real finding about the current backends, not proof that CLR is universally faster than V8. The JavaScript output deliberately enforces nominal provenance and immutability through `WeakSet`, `Object.defineProperties`, `Object.freeze`, fresh payload arrays, and repeated validators. That is visible generated-code cost, not an intrinsic V8 limit. The JavaScript release emission profile is explicitly deferred, so this is a baseline for Copeland's currently available JavaScript backend, not a benchmark of hand-written idiomatic JavaScript.

The performance premise for a controlled Copeland-native application shell is credible: RyuJIT is competitive or substantially ahead for long-running typed application logic in this evidence. It is not evidence that Copeland can replace a JavaScript engine for the open web. V8 remained clearly preferable for the cold, short-lived process path (roughly 44–50 ms Node versus 61–63 ms framework-dependent .NET, medians).

## Questions and scope

This experiment asks whether the same authored Copeland programs, compiled by the repository's C# and JavaScript backends, can make RyuJIT a credible primary runtime for typed application logic; where V8 remains operationally preferable; and whether the losses are runtime or emission-quality effects.

It does not measure browser WASM, NativeAOT, SIMD, parallelism, DOM work, a JS compatibility engine, or an open-web browser. No handwritten C# or handwritten workload JavaScript was timed.

## Environment

| Item | Value |
| --- | --- |
| OS / architecture | Windows 10.0.26200 / x64 |
| Logical processors / available managed memory | 16 / 31.1 GiB |
| .NET SDK / runtime | SDK 10.0.302 / .NET 10.0.10 |
| Node / V8 | Node v26.2.0 / V8 14.6.202.34-node.20 |
| CLR configuration | Release, framework-dependent, default tiered compilation and dynamic PGO; no ReadyToRun publish, debugger, or profiler |
| Node configuration | Default flags; no inspector or source maps |

This is a developer-workstation characterization, not a controlled-lab or universal performance claim. The full machine/runtime capture is in `artifacts/cts-jit-m0/environment.json`.

## Method

Each workload under `tools/CtsJitM0/Workloads` is compiled once to generated C# and once to generated JavaScript from the same authored source. The harness builds a small Release C# host and appends an equivalent thin Node host. Both hosts:

- check the generated result before measurements;
- perform 10 warm-up calls;
- measure 30 in-process rounds of the same workload-specific iteration count;
- return an exact `int` checksum, failing the harness if the targets disagree.

Cold timing is separate: 10 fresh process executions per target after the generated C# host has been built. It includes runtime process startup plus one small checksum call; it does not include compile/build time. Warm timing is host-internal `Stopwatch` on CLR and `process.hrtime.bigint()` on Node. Tables show median, p10/p90, and minimum; mean exists only in raw results.

For CLR, the warm host records thread allocation bytes and generation collection deltas across all 30 measured rounds. For Node, it records only the change in `process.memoryUsage().heapUsed`; it is a coarse observation, not a comparable allocation measure. No forced collection or diagnostic optimization flags were used.

## Workloads and semantic results

| Workload | Authored source | Iterations / round | Exact checksum |
| --- | --- | ---: | ---: |
| Numeric kernel | `NumericKernel.ts` | 10,000,000 | 294727 |
| Machina layout subset | `MachinaSubset.ts` | 4,000 batches of 120 nodes | 859540320 |
| Typed reducer/event batch | `ReducerBatch.ts` | 2,000,000 | 1000001 |
| Record/array transform | `RecordArrayTransform.ts` | 2,000,000 | 999766 |
| String processing | `StringProcessing.ts` | 250,000 | 12248977 |

The layout workload is intentionally a common, benchmark-specific Machina semantic subset: affine `px + ui * parentAxis`, absolute placement, anchor offsets, vertical fixed/fill stack placement, horizontal placement, source-stable node order, resolved frames, and a geometry checksum. The current full Machina resolver is C#-hosted (`Machina.Layout`) and has no ordinary generated-JavaScript realization. It therefore cannot honestly be timed as a full symmetric RyuJIT/V8 benchmark. This subset is the strongest available fair cross-backend path; the lack of full Machina backend symmetry is itself an M0 finding.

## Cold process results (ms)

| Workload | RyuJIT median (p10–p90) | V8 median (p10–p90) | Preferred |
| --- | ---: | ---: | --- |
| Numeric kernel | 61.140 (56.898–66.658) | 43.433 (42.305–45.071) | V8 |
| Machina subset | 62.164 (56.808–66.354) | 50.031 (48.808–53.460) | V8 |
| Reducer batch | 61.713 (55.951–68.631) | 44.114 (43.248–44.890) | V8 |
| Record/array transform | 62.262 (59.382–66.234) | 44.184 (43.192–48.572) | V8 |
| String processing | 63.386 (59.534–67.328) | 45.083 (42.881–49.427) | V8 |

Node starts about 17–18 ms sooner in this framework-dependent deployment. This is a process-start observation, not a deployed-payload comparison.

## Warm throughput results (ms per measured round)

| Workload | RyuJIT median (p10–p90) | V8 median (p10–p90) | Faster target |
| --- | ---: | ---: | --- |
| Numeric kernel | 32.123 (31.932–34.492) | 34.155 (34.092–34.194) | RyuJIT, 1.06× |
| Machina subset | 2.317 (2.288–2.376) | 814.214 (804.527–821.186) | RyuJIT, 351× |
| Reducer batch | 9.615 (8.777–9.742) | 3295.572 (3271.404–3361.077) | RyuJIT, 343× |
| Record/array transform | 5.159 (5.110–5.298) | 2224.388 (2179.143–2341.273) | RyuJIT, 431× |
| String processing | 14.928 (14.336–16.435) | 45.691 (45.401–46.400) | RyuJIT, 3.06× |

The generated C# numeric result has a wider tail (44.711 ms maximum) than Node's very tight numeric range. The typed-object results are not evidence that V8 cannot do this kind of work quickly; they establish that the current Copeland JavaScript emission does not.

## Allocation, GC, and artifacts

| Workload | CLR allocated bytes / 30 rounds | CLR Gen0/1/2 collections | Node heap delta | Generated C# / JS bytes |
| --- | ---: | --- | ---: | ---: |
| Numeric kernel | 1,544 | 0 / 0 / 0 | 8,336 | 527 / 294 |
| Machina subset | 468,484,424 | 28 / 0 / 0 | 10,667,056 | 5,077 / 7,892 |
| Reducer batch | 3,046,292,744 | 182 / 0 / 0 | -9,472,912 | 3,049 / 4,045 |
| Record/array transform | 1,440,017,624 | 86 / 0 / 0 | -10,942,048 | 5,100 / 5,194 |
| String processing | 2,280,001,544 | 136 / 0 / 0 | 8,272 | 1,030 / 535 |

CLR is fast here despite very high allocation in immutable records and strings; that remains a backend-quality opportunity. Node's negative heap deltas merely indicate GC happened during the interval and must not be read as lower allocation. Compiled C# host assemblies were 11–14 KiB; the Node host plus generated script was 1.9–9.5 KiB. Those are application artifacts only. They do not compare installed runtime size, framework-dependent deployment, or self-contained deployment.

## Generated-code audit

### C# / RyuJIT

- Numeric lowering is ordinary `int` arithmetic and loops; it is close to V8.
- Record construction emits sealed reference classes. Immutable updates allocate a new class per operation, explaining the large allocation and Gen0 counts.
- Enum values allocate fresh nested record instances in the reducer (`new CounterEvent.Increment()` etc.).
- No LINQ, reflection, closures, or interface dispatch appeared in the timed paths.
- Array access uses a small bounds helper; the hot record/array path otherwise remains direct.

### JavaScript / V8

- Numeric code is ordinary scalar JavaScript and is near RyuJIT.
- Every nominal record construction uses `Object.create(null)`, `Object.defineProperties` with symbol slots, `Object.freeze`, and a `WeakSet` provenance registration.
- Every enum construction uses `Object.assign`, freezes a newly allocated payload array, freezes the carrier, and records it in a `WeakSet`.
- Generated field reads and matches repeatedly invoke full validators: prototype, frozen-state, `WeakSet`, own-property, type-token, and payload-shape checks.
- These checks and nonstandard shapes make the typed-object workloads allocation-heavy and likely inhibit V8's normal object-shape optimization opportunities.

The audit identifies an obvious backend-quality deficit: runtime nominal-validation and immutable-carrier construction sit inside every hot operation. No emission change was made in M0 because a safe release representation needs an explicit language/runtime contract: stripping provenance checks, changing frozen object shape, or caching zero-payload enum values affects the JavaScript runtime's boundary/forgery guarantees. A narrow production-JS-profile milestone should establish that contract, benchmark before/after, and retain the current validated form as a diagnostic or boundary mode.

## Backend selection matrix

| Workload / concern | RyuJIT | V8 | Preferred | Reason |
| --- | --- | --- | --- | --- |
| Cold startup | 61–63 ms | 43–50 ms | V8 | Smaller fresh-process overhead here |
| Numeric kernel | 32.123 ms | 34.155 ms | RyuJIT, slight | Near parity; CLR wins this run |
| Machina resolution subset | 2.317 ms | 814.214 ms | RyuJIT | Current JS nominal record emission dominates |
| Reducer batch | 9.615 ms | 3295.572 ms | RyuJIT | Current JS enum/record validation dominates |
| Record/array transform | 5.159 ms | 2224.388 ms | RyuJIT | Current JS record representation dominates |
| String processing | 14.928 ms | 45.691 ms | RyuJIT | CLR faster in current generated form |
| Long-running typed server/desktop logic | Strong | Viable after JS profile work | RyuJIT | Throughput plus CLR integration |
| Browser host glue | Not measured as a browser host | Strong operational fit | V8 | Existing browser engine and startup/host ecosystem |
| Controlled Copeland-native shell | Plausible | Useful compatibility boundary | RyuJIT logic + narrow host | Measured typed throughput supports it |

## Browser-platform interpretation

1. **Ordinary browser application:** Copeland should continue to generate JS for the existing browser engine. This result does not displace browser APIs, browser JIT integration, or web compatibility.
2. **Server/desktop application:** generated C# under CLR/RyuJIT is the preferred present path for sustained typed logic. CLR library interoperability is an additional non-benchmark advantage.
3. **Controlled Copeland-native application shell:** placing application logic on CLR/RyuJIT is performance-plausible, provided the host boundary is coarse and the application accepts CLR runtime payload/startup assumptions. Machina pre-resolution can reduce what the host needs to lay out, but it does not implement DOM, CSS, accessibility, networking, media, security, or browser APIs.

An open-web browser still requires broad JavaScript compatibility. A JS engine can be optional only for Copeland-native content in a controlled shell; it remains required for compatibility/legacy boundaries and open-web execution.

## Limitations and next milestone

- One machine, one .NET runtime, and one Node/V8 version.
- JavaScript has no release profile yet; the current symbolic/diagnostic representation is intentionally validation-heavy.
- The full C# Machina resolver has no symmetric JS runtime. The cross-target layout measurement is a shared semantic subset, not a port of the full resolver.
- Allocation metrics are runtime-specific and not directly comparable.
- Cold numbers are framework-dependent local-process results, not browser or packaged-app startup.

Recommended next milestone: **CTS-JS-PERF-M1 — production JavaScript representation**. Define a production-safe nominal record/enum representation, move repeated validation to explicit boundary checks where the language contract permits, stabilize object shapes, consider cached zero-payload enum values, add semantic/forgery tests, and rerun this exact corpus preserving M0 baselines. In parallel, decide whether the real Machina resolver should gain a shared backend-neutral implementation; do not claim full Machina backend symmetry until it does.

## Evidence and validation

- Harness: `tools/CtsJitM0/Program.cs`
- Shared authored workloads: `tools/CtsJitM0/Workloads/`
- Raw output and generated artifacts: `artifacts/cts-jit-m0/` (ignored machine-local evidence)
- Focused tests: `tests/Copeland/Copeland.Cli.Tests/CtsJitM0Tests.cs`

Focused benchmark correctness tests passed, including deterministic C#/JS emission and benchmark argument validation. The full M0 run completed all five workloads and rejected no semantic mismatch. Full solution build/test and diff validation are recorded with the task handoff.
