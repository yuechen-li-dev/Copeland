# CTS-BROWSER-M0 — Native browser interop discovery

## Result

Outcome C: the current native JavaScript backend can execute a small Copeland module graph directly as browser ESM after a narrow browser host-contract seam. The trial proves static ESM loading, local module resolution, typed event callbacks, immutable captured values, browser interaction, and visible updates. It also establishes that the present callable and state laws are not yet sufficient for ordinary closure-local mutable browser state.

This is not production browser support.

## Trial

The runnable trial is [`samples/copeland-ts/browser-m0`](../../../samples/copeland-ts/browser-m0/README.md).

Its authored sources are:

- `Copeland/Counter.ts`: pure `Increment(int): int`.
- `Copeland/Main.ts`: imports the local counter and the declared browser host, captures a stable element id, registers a `(int) => int` callback, renders the next value, and returns that value as the typed event transition.
- `index.html`: owns the explicit entry-point law: it imports `generated/Main.js` and calls `Main()`.

The build harness is intentionally explicit rather than pretending that MSBuild/TSPack browser ownership is settled:

```text
dotnet run --project samples/copeland-ts/browser-m0/Copeland.Browser.M0.csproj
python -m http.server 4173 --directory samples/copeland-ts/browser-m0
open http://127.0.0.1:4173/index.html
```

The compiler emits `generated/Main.js` and `generated/Counter.js`. `Main.js` imports `./Counter.js`; no Node runtime, CLR sidecar, CommonJS wrapper, filesystem path, `process`, or server entry point is in that graph.

## Real-browser proof

The generated page was served by Python's static HTTP server and loaded in the in-app Chromium browser. It rendered `Count: 0`; two browser clicks on the real HTML button rendered `Count: 2`. The page collected no error-level console messages.

This is a browser run, not Node/JSDOM simulation.

## Browser contract attempted

M0 adds a compiler-configured `CopelandJavaScriptHostModuleContract`. It is a deliberately constrained direct ESM boundary separate from npm's RPC/transport contract:

```text
@copeland/browser-m0
  setText(string, string): void
  onClick(string, (int) => int): void
```

The JavaScript host adapter uses real `document.getElementById` and `HTMLButtonElement.addEventListener`. Copeland sees only strings, integers, `void`, and an exact callable signature. It cannot see a DOM object, `any`, arbitrary globals, dynamic property lookups, or inline JavaScript.

The HTML import map resolves this one bare host specifier to `host/browser-m0.js`. The adapter is a fixture and discovery seam, not a proposed permanent DOM API.

## Callbacks and state

The backend now recognizes host-call callable arguments and emits a native JavaScript wrapper around its branded Copeland callable carrier. The browser can retain that wrapper after `Main` returns. The trial captures the immutable `countElement` local into the callback and invokes it later through the browser event API; the captured value survives and the identity/call signature remain checked by the existing callable runtime.

The adversarial result is important: `let count` captured by a callback cannot be reassigned. This is correctly rejected by the existing `COPE-CALL-0018` immutable-capture law. The M0 adapter therefore owns the durable event slot (`let state = 0`), while the Copeland callback owns the typed transition (`Increment`) and the visible rendering. This is adequate to prove the crossing, but it is not an ergonomic Copeland browser state model.

The next state milestone must decide whether browser state is a bounded host/session value, a new explicit mutable-cell law, or a source-level flow/session model. It should not silently relax capture mutability.

## What worked unchanged

- Native strict JavaScript ESM syntax and `"use strict"` are browser-valid.
- Local project module paths already rewrite to deterministic relative `.js` specifiers.
- Named local imports, aliases, privacy, function identity, and module output paths remain intact.
- Callable construction and invocation machinery is environment-neutral after the small host wrapper.
- The generated helper set used by this trial (`WeakSet`, `WeakMap`, `Object`, `String`) is browser-native.

## Node assumptions and profile findings

The core emitted code used for this trial does not assume Node. The old npm realization does: it emits bare package imports, and Node resolves those through `node_modules`; a browser will not resolve such an import without an import map, URL/CDN transformation, or bundling/materialization.

M0 introduces an explicit `JavaScriptRuntimeTarget` with `Node` as the default and `Browser` required to emit a JavaScript host contract. Trying to emit browser-host code under Node produces `COPE-JS-BROWSER-0001`. The CLR backend now also rejects these contracts with `COPE-CS-BROWSER-0001`.

There are no source maps today. Browser stack traces therefore identify generated JavaScript only. This is a tooling gap, not a reason to invent a browser runtime.

## npm and async findings

Existing npm JavaScript emission intentionally produces bare imports such as `import { x } from "some-package";`. M0 did not materialize an npm package for the static page. The host import map proves the browser-side mechanism for one declared module, but package resolution, package export conditions, dependency closure, CSP-compatible import-map generation, and static asset copying remain TSPack/browser-materialization work.

Existing async lowering uses JavaScript `Promise` and no Node-specific Promise primitive, but browser-native async was not added to this trial because the current host contract has no typed Promise/fetch/error projection. A browser M1 should add one deterministic, local browser async boundary only after deciding the error/result conversion law.

## Changes made

- Added an explicit browser runtime target to JavaScript emission.
- Added the tiny declared JavaScript-host contract seam with only primitive and explicit callable types.
- Added direct ESM host-call MIR/emission and native callback adaptation.
- Scoped external ESM imports to the owning source module. Previously aggregate JavaScript imports were copied into unrelated emitted modules.
- Added the static counter sample, import map, host fixture, focused compiler tests, and this report.

## Hacks consciously avoided

- No React, Blazor, Machina, JSX runtime, SSR, hydration, router, hooks, bundler, or dev-server product.
- No `any`, dynamic DOM member access in Copeland, raw DOM object leakage, or inline JavaScript.
- No Node sidecar, CLR interop path, JSDOM success claim, or custom browser application runtime.
- No claim that npm browser packages or source maps are solved.

## Recommended next milestone

**CTS-BROWSER-M1 — browser host-contract and state law.**

Keep the target/profile split. Specify a project-owned, versioned browser contract representation rather than leaving the C# discovery harness as product configuration. Decide the durable state model before broad DOM coverage. Include a bounded browser async contract and source maps if their backend ownership can remain local.

After M1, take a separate **TSPack browser materialization** milestone for npm resolution/import-map generation or bundling. Do not start a UI framework integration before those two seams are settled.

## Additional work performed

- Corrected external import scoping in the project ESM emitter because browser module loading made the aggregate-import leak observable; this changes general JavaScript module emission and is covered by focused tests.
- Added browser-only diagnostics for JavaScript and CLR backend selection.
- Added the browser sample and focused tests; no external framework or package manager behavior was changed.
