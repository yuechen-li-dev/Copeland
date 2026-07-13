# JTF-M5c — Aurelian Runtime public boundary

## Result

M5c makes ordinary Aurelian Runtime lifecycle and compositor operation Dominatus-neutral while retaining a small, explicit advanced Dominatus integration surface in the existing `Aurelian.Runtime` assembly.

## Changes

- Replaced default-session public `AiWorld`/`ActuatorHost` properties and Dominatus-shaped options with `Start`, `Stop`, `TickAsync`, Aurelian lifecycle results/diagnostics, and `GetDominatusAccess()` for deliberate opt-in.
- Moved caller-supplied world, actuator-host, and runner composition to `Aurelian.Runtime.Dominatus`.
- Restricted runtime and compositor actuation command types to internal implementation detail.
- Changed the normal compositor delegate to Aurelian-owned `CompositorDispatchRequest`/`CompositorDispatchResult` values; updated Core's bridge and frame path accordingly.
- Kept one explicit advanced compositor host entry point and the advanced world-runner seam.
- Added reflection-based compiled public-surface enforcement with an exact eight-symbol advanced allowlist and a nested-generic leak proof.
- Added ordinary-consumer lifecycle, repeated-stop, Core neutrality, and bridge-neutrality coverage.

## Compatibility

The restricted members were proof-era public surface with repository-test-only consumers. They are not retained as obsolete compatibility aliases because doing so would keep accidental Dominatus coupling in the ordinary namespace. Advanced consumers migrate to `Aurelian.Runtime.Dominatus`; normal consumers use the parameterless session and Aurelian-owned compositor delegate.

## Unchanged scope

M5c does not alter runtime scheduling, start/stop/tick semantics, frame pump/close handling, renderer/input/screen contracts, solution topology, package versions, or the deferred deterministic-transition and Machina component-lifecycle work.

See [Aurelian Runtime Dominatus public boundary](../Aurelian/architecture/aurelian-runtime-dominatus-boundary-jtf-m5c.md) for the complete inventory and enforcement details.
