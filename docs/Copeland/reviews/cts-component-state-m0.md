# CTS-COMPONENT-STATE-M0 — bounded semantic substrate

The accepted everyday syntax is deliberately small:

```ts
record CounterState { count: int; }

function Counter(): ReactNode {
    state current: CounterState = { count: 0 };
    on Increment() => current with { count: current.count + 1 };

    return <span>{current.count}</span>;
}
```

`state` is one immutable, component-local value and each `on` arm is a typed,
fat-arrow expression producing the complete next value. `with` retains its
existing record-update meaning; enum state may use either existing `match` or
`switch` with the usual fat-arrow arms. This is separate from the existing
`flow` declaration: flow keeps explicit state-machine/stack control, whereas
ordinary component events describe state and presentation only.

The compiler records state, event, and transition facts on
`BoundComponentDefinition`. State identity is
`<canonical component instance>::state`, so repeated instances, parent/child
frames, renderer root replacement, and adapter-private roots cannot share or
rename application state. `component::States`, `component::Events`,
`component::Transitions`, and `component::Frames` project declarations and
canonical identities only; no runtime values or renderer roots are exposed.
The LSP exposes the local state in lexical completion, adds `state`, `on`, and
`switch` completion, and includes declared state/events in component hover.

`ComponentStateFrame<TState>` is the common runtime substrate. A typed
`ComponentEventBridge` delivers to exactly one frame, evaluates the transition
and presentation deterministically, then applies the resulting canonical
`HostAttachmentMir` snapshot through the existing
`RendererAttachmentRegistry`:

* compatible attachment + changed opaque payload: adapter update;
* new attachment: parent-before-child mount;
* removed attachment: child-before-parent unmount;
* adapter/host/payload-contract incompatibility: unmount then mount;
* frame destruction: canonical deepest-first cleanup and later events report
  `COPE-COMPONENT-STATE-0103`.

The frame never passes application state to an adapter and effects are not
introduced in this slice. Existing `flow` effects and push/pop/goto remain the
explicit lower-level control mechanism.

## Browser delivery M0

`component-frames.js` is a separate executable browser artifact. It imports
`registerComponentFrames` from the generated browser host and records one
deterministic definition per canonical stateful instance: component and parent
identity, `<instance>::state`, initial state, attachment IDs, typed event
contracts, generated transition functions, and a presentation-to-plan
projection. TSPack hashes and materializes this module, loads it before the
initial attachment-plan loader, and records it in `browser-materialization.json`.

For the bounded browser M0 surface, a frame is browser-emittable when it has a
Custom Element presentation, a string or nullary-enum initial state, and
zero-payload literal or state-match transitions. Unsupported stateful browser
components fail with `COPE-COMPONENT-STATE-BROWSER-0001`; unavailable branch
children fail with `COPE-COMPONENT-STATE-BROWSER-0002`. The browser registry
owns a frame's current state, validates the emitted event contract, runs the
generated transition, asks its generated projection for replacement plans and
child-frame definitions, and passes the merged canonical plan set to the
existing `registerAttachmentPlans` runtime.

## State-selected child presentation closure

`BoundPresentationBranch` is the compiler-owned projection of a `match` or
`switch` returned directly from a stateful component. It records the enum arm,
local presentation expression, direct child calls, child definitions, and
source-derived authored identities. `component::PresentationBranches` exposes
those static facts beside `component::Transitions` and `component::Frames`;
it never contains the currently selected runtime branch.

The identity law is intentionally branch-qualified:

`<parent instance>::branch-child::<branch identity>::call::<ordinal>`.

Thus repeated calls in one arm remain distinct and deterministic, while an
authored call in two different arms has two lifetimes. Leaving an arm destroys
its child frame; re-entering that same authored arm creates a new valid frame
lifecycle with the same deterministic authored identity after the old registry
ownership has been released. Identity is never a DOM position or runtime
object identity.

Generated projection functions return both retained parent plans and a child
frame set. The existing browser frame registry applies the structural delta:
removed children are destroyed deepest-first and their plans unmounted;
retained children receive compatible plan updates; newly selected children are
registered with `parentComponentInstanceId`, then mount their existing
attachment plans. Destroyed frames leave the live registry but retain a small
diagnostic tombstone, so later events report `COPE-COMPONENT-STATE-0103`.
Trace entries include `ChildFrameCreated`, `ChildFrameRetained`, and
`ChildFrameDestroyed` as well as attachment consequences.

The website's existing Custom Element badge is the dogfood case. Clicking it
uses the adapter's short-lived DOM event bridge to dispatch
`ConfirmStillWorks` to the generated frame. Its shadow text changes from
`Custom Elements work` to `Custom Elements still work` with the same frame,
attachment, semantic host, and one adapter update/no remount. The focused
browser proof also replaces the semantic React host, verifies recovery, then
dispatches the frame event successfully on the recovered attachment.

The website now includes `DialogHost`, an ordinary enum-state `match` fixture:
its Closed arm has no child frame and its Open arm projects `ConfirmDialog`
with a Custom Element attachment. A real Custom Element click reaches
`ToggleDialog`, selects Open/Closed, and creates/destroys the child frame and
attachment while retaining the parent frame. The browser proof also exercises
compatible retained-child payload update, post-destruction event rejection,
reopen, semantic-host recovery, the existing badge, and Desktop/Tablet/Mobile
profiles.

This closes CTS-COMPONENT-STATE-M0 and CTS-COMPONENT-STATE-BROWSER-M0 within
their stated bounds. The next bounded design question is whether an effect or
explicit-flow integration should observe a completed component transition at a
single compiler-defined boundary, without giving renderers state ownership or
adding async orchestration.

## CTS-COMPONENT-EFFECT-COMPLETION-M0 substrate

Component transitions may request ordinary typed `void` function calls as
ordered consequences:

```ts
on Save(draft: string) => "Saving" effect PersistDraft(draft) => Saved();
on Open() => "Open" after AttachmentsSettled effect FocusDialog();
```

The default phase is `PresentationCommitted`. The only explicit phases are
`StateCommitted`, `PresentationCommitted`, and `AttachmentsSettled`.
`component::Effects` records the transition, effect function, phase, authored
order, optional completion event, and source position. Completion events are
validated against the component's declared typed events.

`ComponentStateFrame<TState>` is the shared completion substrate. It commits
the immutable state, begins state-phase effects, applies presentation and the
attachment delta, begins presentation-phase effects, observes adapter results,
then begins attachment-settled effects. Within a phase effects start in source
order. A synchronous launch failure stops unstarted effects for that
transition; failures that occur after an asynchronous effect has started are
recorded in the bounded frame trace and cannot retroactively reorder work.

An effect completion is a typed `ComponentEventBridge` delivery, never a
direct state or renderer-payload write. Synchronous completions queue until
the originating transition has crossed every requested phase. Effects carry
the requesting frame lifetime token. Destroying a frame cancels that token and
discards later completions with `COPE-COMPONENT-EFFECT-0003`; no completion can
resurrect a destroyed frame. Current attachment adapters are synchronous, so
their `mount`/`update`/`unmount` return is the M0 settlement acknowledgement.
No adapter receives application state, an effect scheduler, or a renderer root.

The compiler facts and frame substrate are intentionally usable by a future
explicit-flow emitter, but legacy generated `flow` sessions have not yet been
rewired to this API in this bounded slice. Browser frame emission likewise
continues to reject no additional state shapes but does not materialize
component effects yet; a browser effect bridge must be completed before this
milestone can claim browser delivery or flow compatibility.
