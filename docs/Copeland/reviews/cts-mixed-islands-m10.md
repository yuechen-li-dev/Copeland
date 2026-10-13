# CTS-MIXED-ISLANDS-M10: simultaneous React and Vue controls

Status: **Success for the bounded mixed-island milestone.** One unchanged
Copeland application source mounts genuine React and Vue controls together.
Both React and Vue outer projections work, in Diagnostic, Symbolic, and
Production. A shared state owner can be edited from either framework and waits
for both foreign commits before releasing its presentation effect.

## Authoring and authority

`samples/copeland-ts/mixed-islands-m10/src/Application.tsx` contains Copeland
records/enums, `state`/`on`, explicit capture, HTTP effects, `ownChild`,
contribution streams, and Machina layout/style. It imports typed control
contracts rather than foreign node factories. The React control uses Base UI;
the Vue control is a genuine Vue 3 component. Foreign local counters make root
preservation and fresh state after reinsertion observable.

The compiler still rejects a raw Vue component in a React presentation. An
explicit presentation host has the outer renderer identity and receives data
props and void event endpoints. Its contract sets `PresentationHost: true`,
requires a string `hostKey`, and disallows slots, compound members, reserved
`__cope_` props, nested callbacks, and renderer values inside props. Invalid
contracts report `COPE-VIEW-HOST-0001`; dynamic empty/duplicate host keys report
`0002`/`0003`. A host outside a canonical state owner reports `0004` at the
physical boundary. The host flag survives npm JSON/manifest projection, binding,
module MIR, and emission.

The emitter registers contracted host values in a weak side table. The canonical
component projection assigns a host identity/generation, reconciles it by
component contract plus authored domain key, and injects an immutable revision
ticket. Retirement belongs to that projection, alongside existing child and
effect retirement. Application state and transitions continue to use the
canonical browser frame registry.

`src/Copeland/Copeland.TS.Backend.JavaScript/Browser/PresentationIsland.ts`
contains the reusable executor protocol. An executor has an open identity and
mount/update/unmount methods with explicit commit callbacks. It retains opaque
roots and props and delegates physical lifecycle counting to the existing
`registerRendererAdapter`/`attachRenderer`/`updateRenderer`/`detachRenderer`
API. It introduces no alternative application state or effect engine. Private
props are addressed by opaque payload tokens through that API's existing string
payload boundary; framework node values never pass between renderers.

Each updated connection receives a lease bound to its owner generation, exact
revision, and physical connection. Event callbacks and commit acknowledgements
from superseded leases are ignored. The projection releases
`PresentationCommitted` effects only after its outer projection and all its
direct foreign hosts acknowledge the current revision. This is per-owner
presentation acknowledgement; it does not implement subtree
`AttachmentsSettled`, DOM painting, or arbitrary asynchronous bridge guarantees.

## Defects found through the real path

The first mounted run found an enum-match emission defect: an XML property
reading `state.draft` required record-access setup statements, but the inline
match path omitted them and raised an undefined receiver error. The backend
now detects element/prop/child setup and uses its existing structured match
emission. Executed compiler tests cover both match arms in all six target/profile
combinations.

The first reorder test also found a lost React foreign counter. React StrictMode
replayed an unchanged outer host connection during movement. Physical host
leases now defer final release to the microtask boundary, preserving a root
through a same-commit reconnect. Genuine removal still revokes its canonical
owner immediately and releases the foreign root exactly once. React root
unmount is deferred to its safe microtask boundary when retirement happens
during an outer commit. This qualifies synchronous StrictMode replay, not an
arbitrary keep-alive policy.

## Qualification

- **270 mounted mixed-island observations**, with zero warning/error/page/network
  diagnostics: two outer targets, three emission profiles, and 1280/768/390 px.
  Checks cover shared state edits, joining two foreign commits, independent
  drafts, foreign local state, stable DOM/root identity on reorder, typed prop
  updates, stale callbacks, held/superseded commit acknowledgements, exact focus
  effect release, balanced removal, fresh reinsertion, late HTTP completion
  rejection, retired generation acknowledgements, typography/no overflow, and
  complete root teardown. React outer projection uses StrictMode.
- Preserved shared-source qualification: **192 mounted observations**.
- Preserved React regression: **55 compiler expectations**, seven state/on and
  twelve effects/children cases per profile, 74 paired page observations, and
  four semantic library assertions.
- Fourteen new backend tests cover match/property execution in every target and
  profile, required host identity, and rejection of foreign node/callback props.
- Full solution build: **zero warnings/errors**. **2,256 tests passed**, zero
  failed/skipped. Results are recorded in `evidence/mixed-islands-m10/validation.json`.
  Full logs are under `artifacts/validation/mixed-islands-m10-{build,tests}.log`.
- Frozen Zig suites are unchanged and were not run.

The runner preserves the historical M8/M9 evidence and writes new regression
evidence below `mixed-islands-m10`. Source/emission hashes, mounted reports,
screenshots, and validation hashes are retained there. Reproduction instructions
are in the fixture README.

## Supported boundary and remaining work

The compiler chooses an outer `View` projection per target. Physical host
bindings connect declared foreign executors inside Copeland-assigned layout
hosts. Source-level foreign framework selection remains an explicit component
contract; this milestone does not add a grammar construct or a production
TSPack target for island packages.

The tested controls exchange string data and typed events in light DOM. Machina
owns their host surfaces; actual computed typography and overflow are checked.
Foreign internal layout and widget style remain framework-owned. Aggregate data
types still need adapter-specific representation qualification; renderer values
and nested callbacks are rejected rather than silently forwarded.

Svelte/Lit/Preact/Solid and managed Blazor bridges can implement this open executor
protocol, but no adapter for them is claimed here. Managed/remote bridges need
their own serialization, failure/cancellation, stale revision/generation, and
release evidence. Named/scoped slots, Shadow DOM style propagation, SSR/hydration,
HMR, Suspense/KeepAlive/Teleport/portals, and attachment settling remain deferred.
