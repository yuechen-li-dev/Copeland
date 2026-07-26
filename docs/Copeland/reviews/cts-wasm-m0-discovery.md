# CTS-WASM-M0 — C# WebAssembly host-boundary discovery

## Result

**Outcome B: useful for coarse computation, not a primary browser UI backend.**

The ordinary Copeland C# backend can run in a real .NET WebAssembly browser application without a WASM-specific lowering. A release browser run proved a Copeland-authored immutable record reducer, discriminated event model, exhaustive switch, state persistence, reset, and a numeric workload.

The experiment also makes the cost boundary clear: browser DOM/bootstrap remains JavaScript, and repeated JS-to-WASM calls are materially more expensive than asking WASM to perform one coarse unit of work. Native JavaScript remains the preferred target for ordinary browser application state and rendering. CLR/WASM is promising as an optional compute and CLR-library-compatibility target.

This is a discovery sample, not a declaration of production browser-WASM support.

## Trial architecture

```text
Copeland/Counter.ts + Workload.ts + Main.ts
  -> Copeland.TS.MSBuild
  -> ordinary generated C# (obj/.../CopelandProject.g.cs)
  -> .NET 10 browser-wasm publish
  -> [JSExport] BrowserBridge
  <- small browser-host.js -> DOM/event APIs
```

The trial is [`samples/copeland-ts/browser-wasm-m0`](../../../samples/copeland-ts/browser-wasm-m0/).

- `Copeland/Counter.ts` contains `CounterState`, `CounterEvent`, `Reduce`, and the record replacement expression.
- `Copeland/Workload.ts` contains the deterministic numeric loop.
- `Copeland/Main.ts` presents exported, typed application entry points for the bridge.
- `BrowserBridge.cs` is a narrow host-project bridge. It retains the current integer state in the WASM runtime, converts the integer wire discriminant to the corresponding typed Copeland event entry point, and returns a coarse display string.
- `wwwroot/Host/browser-host.js` loads `dotnet.js`, registers DOM events, calls exported bridge methods, and updates DOM text. It has 92 nonblank lines, including DOM validation and measurement code.

The generated C# is written to `obj/<configuration>/net10.0/Copeland/CopelandProject.g.cs`; it is not a handwritten reducer. The experiment uses the existing MSBuild integration and C# backend unchanged except for the literal portability fix below.

## Commands and browser proof

The WASM build tools were absent initially, so the standard .NET `wasm-tools` workload was installed. No third-party browser runtime or UI framework was installed.

```powershell
dotnet build src/Copeland/Copeland.TS.MSBuild/Copeland.TS.MSBuild.csproj -c Release --no-restore
dotnet publish samples/copeland-ts/browser-wasm-m0/Copeland.Browser.Wasm.M0.csproj -c Release --no-restore -o samples/copeland-ts/browser-wasm-m0/publish
python -m http.server 4174 --directory samples/copeland-ts/browser-wasm-m0/publish/wwwroot
```

The published page was loaded through static HTTP in the in-app Chromium browser. Browser automation observed:

```text
initial:        Count: 0
click increment twice
after clicks:   Count: 2
click reset
after reset:    Count: 0
console errors: none
```

The JavaScript host uses `dotnet.create()` and `getAssemblyExports`; it does not use Razor components, Blazor components, a server, or an ASP.NET UI abstraction. The browser runtime is Microsoft’s supported .NET browser host infrastructure only.

## Boundary findings

| Concern | JS host | WASM/C# | Finding |
| --- | --- | --- | --- |
| Runtime bootstrap | Yes | No | `dotnet.js` and asset loading are inherently browser/module work. |
| DOM lookup and mutation | Yes | No | Keeping DOM objects out of generated C# yields a small typed seam. |
| Browser event registration | Yes | No | JS retains `addEventListener`; the event value is immediately forwarded. |
| Typed event semantics | Wire only | Yes | The host sends `0`/`1`; the bridge selects Copeland `CounterEvent.Increment`/`Reset` entry points, whose reducer constructs and matches the generated nominal event. |
| Reducer, records, switch, replacement | No | Yes | Executed in the published browser runtime. |
| Durable counter state | No | Yes | A private static field in the WASM bridge owns it across browser events. |
| Pure numeric workload | No | Yes | A generated Copeland loop executed in the browser. |
| Coarse render result | Apply only | Produce | A `string` crosses back; JS assigns `textContent`. |
| Fetch, timers, storage | Not tested | Either later | They were outside this intentionally synchronous M0. |

The tested wire forms were `int` event discriminants and workload arguments, `string` render snapshots, and a `bool`-free primitive-only public bridge. The richer internal representation was a generated record and nominal event. Direct record marshalling was intentionally not added: the built-in `[JSExport]` shape is most natural for primitives and strings, while a general record serializer would be premature. The coarse string snapshot is the practical M0 result representation.

## State, callbacks, and failures

State option A was tested: WASM owns current state. Each browser callback invokes one exported `Dispatch(int)`, the bridge invokes generated Copeland reducer code, retains the next value, and returns one render snapshot. This is clearer and less chatty than JS owning an opaque immutable state value, and it avoids mutable closure capture.

The callback law is intentionally small: JavaScript owns the retained native DOM callback; the callback invokes a stable CLR export. CLR does not retain a JavaScript callback. This avoids GC rooting and callback identity concerns in M0. Browser-to-WASM exceptions are ordinary exported-method failures; the bridge rejects an unknown event discriminant with `ArgumentOutOfRangeException`. A production contract should represent expected host failures with typed Copeland fallibility rather than send arbitrary exceptions across the seam.

## Workload and measurements

The workload is a generated deterministic integer recurrence for 100,000 iterations. Its checksum was first checked for equality, then timed:

```text
checksum:     587747 in both native JS and C#/WASM
WASM:         1.200 ms
native JS:    0.500 ms
startup:      68.000 ms
```

These are one release-browser run’s coarse timings, not a benchmark claim. The WASM app uses interpreted managed IL over the native Mono browser runtime; `RunAOTCompilation` was not enabled. Consequently, the result does not establish a steady-state AOT comparison and native JS won this small integer loop.

For boundary characterization, the same release run performed 10,000 reducer dispatches in two shapes:

```text
10,000 individual JS -> WASM calls: 29.100 ms
one exported call containing 10,000 generated reducer transitions: 1.500 ms
```

The observed result supports coarse calls and rejects fine-grained DOM/event chatter. It includes startup/warm-up effects and does not claim general-purpose throughput.

The release `wwwroot` tree was 11,800,528 bytes across 42 files before HTTP compression. The framework assets alone were 11,794,866 bytes. The app assembly WebAssembly asset was 14,101 bytes raw (5,158 bytes Brotli); the dominant cost is the .NET runtime, core library, and globalization data. The first interactive run measured 68 ms in the host’s `performance.now()` interval, but this localhost measurement is not a network cold-start figure.

## Portability and debugging findings

The generated reducer required no unsupported API: it uses records, nominal enum carriers, integer arithmetic, and normal static calls. The sample has no reflection, dynamic loading, filesystem access, threads, process APIs, native libraries, or sidecars.

The trial exposed one existing C# portability defect: `int` and `long` literals were emitted as floating literals (for example `1.0`), causing ordinary generated C# record and loop code to fail compilation. `CSharpLiteralWriter` now emits integer literals as integer C# literals (`long` has an `L` suffix), and a focused Roslyn compile/runtime test guards the fix. This is a general C# correctness correction, not a WASM-specific lowering.

Release browser errors identify JavaScript host and generated C# artifacts, not Copeland source spans. Source maps/PDB-to-Copeland mapping are therefore still an unsolved debugging layer. The release publish trimmed managed code successfully; no AOT application compilation was requested. The emitted host has no `eval`, inline script, dynamic code generation, workers, or shared memory requirement. A deployment needs normal static hosting with correct `.wasm` MIME handling and permits module scripts; a strict CSP can allow the external host module and external runtime assets without inline script.

## Native JavaScript relationship

The existing [`browser-dispatch`](../../../samples/copeland-ts/browser-dispatch/) sample remains the native ESM semantic reference. Both samples use the same `CounterState`/`CounterEvent`/`Reduce` shape. No target checks were added to ordinary Copeland code; their host projects select the target realization.

The source was duplicated for this discovery sample rather than factoring a shared sample package, so the comparison stays self-contained. A supported M1 should determine whether a shared application project can feed both materialization paths without obscuring target-specific host configuration.

## Recommended next milestone

Do not make CLR/WASM the primary browser backend. Pursue it as an **optional coarse compute and CLR-library compatibility target** if a real workload (for example layout or a reusable CLR library) justifies its runtime payload.

If pursued, **CTS-WASM-M1** should define a browser-WASM profile and a generated typed export boundary with these restrictions:

- primitives and compact immutable snapshots at the public boundary;
- WASM-owned state only for explicit sessions or coarse modules;
- JS owns DOM/events and batches work before crossing;
- typed fallible results for expected host errors;
- explicit unsupported shapes for records, callbacks, async, and browser-only CLR APIs;
- source-location mapping before presenting this as a supported application backend.

Keep normal browser UI state, DOM mutation, high-frequency input, timers, fetch, storage, and rendering in native JS unless a later experiment provides contrary evidence. No mixed-module compiler, generalized serializer, DOM binding layer, Blazor component model, or AOT optimization project should be started from this M0 alone.

## Additional work performed

- Corrected C# integer/long literal emission because the real generated reducer could not otherwise compile; covered by a focused backend test.
- Added the self-contained browser-WASM sample, static-host instructions, release publish path, and discovery report.
- Installed the standard .NET `wasm-tools` workload because the repository’s SDK initially had no WebAssembly tooling.

No compiler target taxonomy, host-contract feature, broad marshalling system, or production support claim was added.

## Validation

- `dotnet build samples/copeland-ts/browser-wasm-m0/Copeland.Browser.Wasm.M0.csproj --no-restore` passed.
- Release `dotnet publish` passed and the resulting `publish/wwwroot` was served statically.
- Real Chromium automation proved the release counter (`Count: 0` -> two clicks -> `Count: 2` -> reset -> `Count: 0`), workload equality, and zero error-level console messages.
- The native browser dispatch sample was rebuilt and separately proved in Chromium (`Count: 0` -> two clicks -> `Count: 2`) with zero error-level console messages.
- Focused C# backend tests passed: 241 tests.
- Focused browser-host emission tests passed: 4 tests.
- `dotnet build Copeland.slnx --no-restore` passed with zero warnings/errors.
- `dotnet test Copeland.slnx --no-build` passed: 1,404 tests across the included suites.
- `git diff --check` passed.
