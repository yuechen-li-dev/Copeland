# JTF-M5b Dominatus ownership consolidation

## Status

JTF-M5b restores the original Joint Task Force ownership ladder. Dominatus remains an external, versioned package dependency. JTF consumes the recorded `Dominatus.Core` and `Dominatus.OptFlow` `0.4.0` packages and does not consume the separately stabilized, unpublished deterministic-transition surface.

## Graph before M5b

```text
src/Machina.UI/Machina.Dominatus
  -> Machina UI contracts
  -> Dominatus.Core 0.4.0
  -> Dominatus.OptFlow 0.4.0

tests/Machina.UI/Machina.Dominatus.Tests
  -> Machina.Dominatus and redundant Dominatus packages

presenter and component-gallery samples
  -> Machina.Dominatus (unused)

src/Aurelian/Aurelian.Runtime
  -> Dominatus.Core 0.4.0

src/Aurelian/Aurelian.Core
  -> Aurelian.Runtime transitively exposed Dominatus actuator types in frame-pump composition
```

M7b2 cleanup supersedes the inspection-submodule arrangement: `reference/dominatus`
was removed. Package-only consumers retain centrally pinned packages, while active
source integrations resolve the standalone sibling Dominatus repository.

## Graph after M5b

```text
src/Machina.UI/*
  -> Machina-only UI declarations, local state, input, screens, and presentation
  -> no Dominatus package or source dependency

src/Integrations/Machina.Dominatus
  -> selected Machina contracts
  -> Dominatus.Core 0.4.0 and Dominatus.OptFlow 0.4.0

src/Aurelian/Aurelian.Runtime
  -> Dominatus.Core 0.4.0

src/Integrations/Aurelian.Machina
  -> Machina-to-Aurelian presentation and lifecycle translation only
  -> no Dominatus dependency
```

The adapter keeps its `Machina.Dominatus` assembly and namespace for compatibility, but physical ownership and test ownership are integration-owned. It is excluded from `Machina.UI.slnx` and is exercised in `JointTaskForce.Integration.slnx`.

## UI lifecycle boundary

Machina core is Dominatus-free; that is not a prohibition on all UI participation by Dominatus. The optional integration adapter reserves the correct future host for coarse behavioral scopes:

```text
push declaration or behavior frame
  -> remain active across events, waits, and effects
  -> actuate requests
  -> pop frame
```

Appropriate future pressure is a screen, page, dialog, modal, temporary interaction-capture scope, or other workflow that genuinely spans events or time. It is not a widget mount/patch/unmount model, renderer clip or transform stack, dense component-tree traversal, simple field update, or scrollbar reducer. Ordinary component state stays direct C# or existing Machina runtime behavior; the current scrollbar remains a local deterministic state machine.

The retained `CounterUiRuntime` is a bounded smoke proof of typed Machina action ingress, Dominatus event hosting, deterministic tick progression, and UI reconstruction. It is not a finished component-lifecycle API and neither current sample uses it.

## Aurelian and public-boundary decisions

`Aurelian.Runtime` remains the deliberate owner of game/runtime Dominatus use: runtime sessions, world runners, compositor-policy actuation, and smoke proofs. Its public runtime APIs still include historical Dominatus concrete types where changing the boundary would require a material runtime API redesign; that is a bounded later review, not a reason to retain a Machina-core dependency.

M5b removes `Aurelian.Core`'s accidental transitive compile use of `ActuatorHost` and `IActuationHandler`. `CompositorPolicySession` now provides an Aurelian-owned dispatch delegate overload, retaining the existing public overload and runtime behavior while keeping the concrete actuator composition in `Aurelian.Runtime`.

`Aurelian.Machina` remains the narrow presentation-frame, lifecycle-fact, and close-command translator. It does not own a Dominatus runtime, transition table, blackboard, agent, or UI behavior.

## Package and source doctrine

The only production owners of Dominatus packages are `Aurelian.Runtime` and the explicit `Machina.Dominatus` integration adapter. `Dominatus.OptFlow` remains centrally versioned because the adapter’s event proof uses `Ai.Event`. No package version changed. No Dominatus source was copied, vendored, or referenced as a project.

The separately stabilized deterministic-transition work remains deferred: it is unpublished, no JTF runtime adapter has been evaluated, and M5b is an ownership milestone. Later adoption requires a published package, an approved integration-owner design, a demonstrated use beyond direct switches, and evaluation after the JTF organizational ladder closes.
