# JTF-M5b — Dominatus ownership consolidation

## Result

M5b removes Dominatus from Machina core while retaining the intended optional UI lifecycle integration lane under `src/Integrations`.

## Changes

- Moved `src/Machina.UI/Machina.Dominatus` to `src/Integrations/Machina.Dominatus` without changing the assembly name or namespace.
- Moved `tests/Machina.UI/Machina.Dominatus.Tests` to `tests/Integrations/Machina.Dominatus.Tests` and removed its redundant direct package references.
- Kept `CounterUiRuntime` as a small integration smoke proof; it does not serve either general sample and does not establish a lifecycle framework.
- Removed unused `Machina.Dominatus` references from the presenter and component-gallery samples.
- Removed the two former Machina dependency exceptions and deleted the exception-manifest mechanism rather than retaining an empty ceremonial file.
- Added normal validator rules for the two approved production owners: `Aurelian.Runtime` and `Integrations/Machina.Dominatus`; Machina core and all other production projects are rejected.
- Added `Machina.Dominatus` and its test project to `JointTaskForce.Integration.slnx`; kept both out of the Machina fast lane and `JointTaskForce.slnx`.
- Moved the Aurelian frame-pump's concrete Dominatus actuator composition into `Aurelian.Runtime` behind an Aurelian-owned dispatch delegate, preserving the existing runtime path.

## Historical correction

An initial M5b interpretation classified the counter project as disposable proof debt. The correction preserves the semantic lane but places it where the architecture always intended: an optional integration host for coarse event-spanning UI behavior. M5b does not add Push/Pop/Goto APIs, a component lifecycle system, modal/navigation framework, or transition adapter.

## Behavior evidence

The counter proof still verifies typed action ingress, ordered event consumption, no repeated historical increment on subsequent ticks, generated UI state, and Machina hit-test compatibility. Existing presenter, input, screen, close, frame-lifecycle, and renderer tests remain in their established owners and lanes.

See [JTF-M5b Dominatus ownership consolidation](../architecture/jtf-dominatus-ownership-consolidation.md) for the complete pre/post graph and package doctrine.

## Recommended follow-up

JTF-M5c should be a closeout, not another UI or renderer redesign: record the bounded decision on whether Aurelian Runtime's existing public Dominatus concrete types merit a future Aurelian-owned facade, then close the organizational ladder if no further owner boundary is identified.

A later, separately approved Machina-Dominatus lifecycle milestone may define one coarse screen, dialog, modal, or temporary capture scope in the integration adapter. It must prove Push/Pop lifetime, event/wait/effect behavior, and teardown without introducing a per-widget lifecycle model or changing ordinary local state, input, presentation, renderer, or transition ownership.
