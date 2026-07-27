# CTS-REACT-M0 closure: unified React + CLR application proof

Status: complete as a bounded integration milestone.

This closure proves one Copeland TS / TS-XML application crossing the accepted
TSPack browser, React, state/dispatch, remote-operation, CLR, and ASP.NET Core
hosting seams. It is not full React compatibility or a full-stack framework.

## Authored application

The canonical sample is [samples/copeland-ts/tsxml-react-m0](../../../samples/copeland-ts/tsxml-react-m0/).
Its authored Copeland source is deliberately small:

- `Copeland/App.tsx` imports `createElement` from `react` and exposes the
  plain `View(state, send)` function.
- `Copeland/State.ts` declares `AppState` and `AppEvent`, creates the initial
  snapshot, and implements the pure `Reduce` function.
- `Copeland/Main.ts` mounts React, dispatches through the browser host, and
  runs `SerializeEffect` after a loading state. The reducer does not perform
  HTTP work.
- `Copeland/Bridge.ts` visibly contains `using System.Text.Json;`, the nominal
  request/error records, and one declared remote operation.

The source keeps the DTO declaration in one place. The browser client and the
ASP.NET endpoint are generated from that declaration.

## Ecosystem ownership

React and ReactDOM are resolved by TSPack from npm at the locked version
`19.2.7`. TSPack materializes the production browser ESM, the React singleton
import map, the React preflight module, and the browser host package.

Copeland owns the typed state, events, reducer, effect intent, remote contract,
and callback typing. React owns element representation, mounting,
reconciliation, and DOM updates. There are no React hooks, React state,
React-specific stores, global event buses, handwritten fetch calls, handwritten
ASP.NET endpoints, reflection, or dynamic operation-name dispatch.

The CLR side uses `System.Text.Json.JsonSerializer.Serialize(request)` in the
generated direct CLR operation. The `using` directive remains authored and
visible; the fully qualified call is used because the current React-profile
name binder requires the CLR receiver to remain explicit at this boundary.

## State and effect law

The verified sequence is:

```text
click
  -> typed AppEvent.Increment
  -> pure Reduce
  -> loading effect intent
  -> generated SerializeState browser client
  -> same-origin generated ASP.NET route
  -> direct generated CLR call
  -> System.Text.Json string
  -> typed SerializationCompleted event
  -> Reduce incorporates the result
  -> React renders the next snapshot
```

The generated browser client uses the deterministic route
`/__copeland/m0/bridge/serialize-state`. It validates the request envelope,
response schema, nominal string result, and stable error kinds. The host uses a
loopback dynamic port and serves the generated browser output and bridge from
the same origin.

## TS-XML React profile and generated artifacts

The generator compiles the full graph with the explicit `ReactM0` profile and
declared npm contracts for `react` and `react-dom/client`. It compiles the
bridge graph separately for the C# and ASP.NET backends, then passes the
generated route map into browser emission.

The browser output contains `Main.js`, `App.js`, `State.js`, `Bridge.js`, the
TSPack packages, the import map, and the React preflight. The generated bridge
contains the typed request DTO, JSON envelope validation, stable failures, and
the endpoint that directly invokes the generated `CopelandModule` operation.

## Real Chromium proof

A fresh in-app Chromium tab was used against a fresh loopback host at a
dynamic port. The observable results were:

```text
initial:
Count:0
{"message":"Hello from CLR","count":0}

after one real Increment click:
Count:1
{"message":"Hello from CLR","count":1}
```

The browser proof also observed the DOM dataset marker
`data-copeland-react-preflight="ready"`, proving that the TSPack React/ReactDOM
preflight created and unmounted a React root before the application mounted.
The button locator resolved to exactly one element. The page had no console
entries, no React warnings, and no page errors before or after the click.

Server-side evidence from the same run:

```text
COPELAND_CLR_DIRECT operation=Bridge.ts/SerializeState
COPELAND_BRIDGE_REQUEST path=/__copeland/m0/bridge/serialize-state status=200
COPELAND_CLR_DIRECT operation=Bridge.ts/SerializeState
COPELAND_BRIDGE_REQUEST path=/__copeland/m0/bridge/serialize-state status=200
```

There were exactly two POST bridge calls: one for count 0 and one for count 1.
The generated CLR source contains the direct `System.Text.Json` call, and both
requests produced the corresponding visible JSON. No failed or pending bridge
request remained. The Chromium tab was closed and the ASP.NET process was
stopped cleanly after proof.

## Focused coverage

The focused React/bridge tests cover React JSX lowering through `ReactM0`,
direct CLR static-call binding in the React profile, browser emission for a
declared remote operation and typed await, async parameter/local frame-slot
reuse, generated bridge endpoint behavior, typed completion, bridge route reuse,
and the bounded `SerializationFailed` projection for a rejected bridge call.

The real fixture additionally covers TSPack materialization, production React
ESM, same-origin hosting, two distinct CLR serialization requests, real browser
dispatch, and cleanup.

## Additional work performed

- Added the unified `tsxml-react-m0` authored sample and generated host shape;
  required to close the original application milestone rather than add a new
  subsystem.
- Extended TSPack’s generated `@copeland/browser-v1` module with the bounded
  `getMountElement` and `dispatchReact` exports required by the accepted React
  seam; existing browser-M1 exports remain intact.
- Made TSPack’s React preflight marker observable through a DOM dataset flag;
  this only exposes proof state and does not affect React ownership.
- Made the sample host copy generated browser artifacts on every build so a
  generated index cannot be shadowed by a stale Web SDK output copy.
- Fixed React-profile CLR static-call ordering and browser async remote-call
  typing/lowering exposed by this integration. The async JavaScript emitter now
  uses reserved suspension-state and parameter/local frame-slot names, and the
  lowerer visits nested record, field, and enum expressions needed by the
  remote effect.

These changes are local to the integration seams and preserve the prerequisite
fixtures. They do not broaden the bridge type system.

## Validation and inherited failures

`dotnet build Copeland.slnx --no-restore` passed. The focused React and
ASP.NET bridge test filters passed. TSPack `go build ./cmd/tspack`, dependency
sync, browser materialization, and the unified Chromium proof passed.

The full solution test command was also run: 12 tests failed, and none were
integration-specific; all other reported test assemblies passed. The failures are the inherited JavaScript npm
positional-argument emission, JavaScript async state-transition/runtime
assertions, JavaScript and C# corpus/hash baselines, the nominal-union corpus
hash, and callable-corpus byte-stability assertions. Those baselines were not
changed. The integration-specific React/bridge filters and the real application
path are green; the remaining failures are separate corpus/backend work.

## Limitations and next milestone

This is a bounded unified application proof for primitive values, one nominal
record, a fallible string result, production React ESM, and a single generated
ASP.NET operation. It does not claim hooks, context, arbitrary third-party
components, `@types/react`, SSR, hydration, Next.js, authentication, databases,
EF Core, NestJS, OpenAPI productization, generalized RPC, streaming,
WebSockets, CLR callbacks, deployment automation, or full ASP.NET ergonomics.

The most valuable next milestone is `CTS-REACT-COMPONENTS-M1`: prove that
existing third-party React widgets can be imported and consumed with minimal
friction. That is the next adoption test after this unified ecosystem proof.

CTS-REACT-M0 is honestly complete: one Copeland application imports React from
npm, uses `System.Text.Json` through CLR, owns state and effects through typed
Copeland code, calls a generated ASP.NET boundary, and renders both CLR-produced
snapshots in real Chromium without handwritten client/server glue or React hook
semantics.
