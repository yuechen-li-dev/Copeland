# JTF-M5a Dominatus UI authoring ergonomics

## Status and decision

> Historical-status note: M5a's package/adoption recommendation remains a record of its audit. JTF-M5b later moved the existing counter proof to `src/Integrations/Machina.Dominatus`, removed the two exceptions, and deferred transition consumption because the new surface remains unpublished. See [JTF-M5b Dominatus ownership consolidation](jtf-dominatus-ownership-consolidation.md).

JTF-M5a is a documentation-only architecture audit. It does not implement an API, change a package version, alter runtime behavior, migrate the presenter scrollbar, remove `Machina.Dominatus`, or remove the two dependency exceptions that still protect that project.

The audit reaches four decisions:

1. Dominatus is not missing utility arbitration. `Dominatus.OptFlow.Ai.Decide` and `Ai.Option` already expose option scoring, named decision slots, hysteresis, minimum commitment, stable current-option tie behavior, and trace reports.
2. The presenter scrollbar is deterministic event dispatch, not utility arbitration. Its handwritten reducer remains the better production implementation today.
3. Dominatus does have an authoring and hosting gap between direct C# dispatch and its agent-oriented HFSM runtime. There is no independent typed operation with the shape `(state, event, context) -> next state + ordered effects`.
4. The smallest justified addition is an additive, pure, generic transition definition in `Dominatus.Core`, with inspectable results and an optional adapter into the existing node/runtime model. It should not be a new package, should not replace `Ai.Decide`, and should not require `AiWorld`, `AiAgent`, a blackboard, an iterator node, or a tick for direct use.

This is a design recommendation, not an adoption decision. The proposed surface must pass the adoption threshold in [Ergonomics evaluation](#ergonomics-evaluation) before any JTF production behavior is retained on it.

## Audit identity and evidence

### Joint Task Force baseline

- Repository commit: `8cb9e48b3a16807b1112319c86fbd947e003525b`.
- The worktree was clean when the audit began.
- `Directory.Packages.props` pins `Dominatus.Core` and `Dominatus.OptFlow` to `0.4.0`.
- The restored `0.4.0` NuGet nuspecs identify source commit `0d60cba322dfb4e4f5f61c72867d24d4da2fe33d` for both packages.
- M7b2 later removed the source-inspection submodule. Active source integrations now
  resolve the standalone sibling Dominatus repository; package-only consumers retain
  centrally pinned packages.

### Current Dominatus checkout

The separately supplied checkout at `C:\Users\yuech\source\repos\Dominatus` was audited at commit `e3654bcc81a3029bae90a4ee695a6a8fc58d411d` (`demo-346-ge3654bc-dirty`). The checkout already contained unrelated dirty Godot sample files; M5a did not modify them.

The changes between published-source commit `0d60cba` and current commit `e3654bc` are confined to Godot TinyTown/SpriteForge-adjacent assets, sample code, and `Dominatus.GodotConn` sprite loading. The audited `Dominatus.Core`, `Dominatus.OptFlow`, `Dominatus.UtilityLite`, their relevant tests, and their authoring documentation are unchanged across those two commits. Consequently:

- package behavior consumed by JTF is established from the `0.4.0` packages and their recorded commit;
- current upstream direction is established from the local `e3654bc` checkout;
- this audit does not infer an API from older JTF copies or names.

### Evidence read

The audit inspected the JTF M0 through M4d topology, semantic-boundary, architecture, and migration records; all active Dominatus package/project references and exception entries; Machina input contracts, frontend routing, presenter routing, screen/layer composition, scrollbar interaction, playback, integration hosts, and `Aurelian.Machina`; Aurelian runtime session, compositor policy, frame loop, frame pump, and close acceptance; and their representative tests.

In Dominatus it inspected the implementation and tests for `Dominatus.Core` blackboards, runtime, event bus, nodes, steps, HFSM, decisions, actuation, tracing, persistence/replay; the `Dominatus.OptFlow.Ai` facade; `Dominatus.UtilityLite.Utility` and `When`; package metadata; the README, architecture overview, authoring guide, orchestration ladder, primer, release notes, and representative samples/tests.

## Current JTF architecture and Dominatus use

### Foundational input, presentation, and host flow

The current route is explicit and ordered:

```text
platform callback
  -> integration-owned collector
  -> immutable UiInputBatch in callback order
  -> Machina frontend lifecycle routing + presenter input routing
  -> Machina UI actions/local interaction state
  -> Machina presentation frame
  -> Aurelian.Machina translation
  -> Aurelian resolved-2D contracts/backend
```

`UiInputBatch` is an immutable batch of neutral `UiInputEvent` records. It validates finite pointer/wheel data and carries resize and close observations without assigning UI or engine policy. `PresenterUiInputRouter` walks that order, recomposes immediately after resize when a callback is supplied, and preserves the scrollbar interaction state between events. Playback generates the same `UiInputEvent` values and records emitted input, capture requests, actions, state deltas, and before/after snapshots.

Generic screen identity, visibility, layers, and deterministic composition order live in `Machina.Presentation.Screens`. `Aurelian.Machina` translates presentation operations and frontend lifecycle messages; it does not own either subsystem's semantic contracts. Aurelian accepts a translated close command before another frame begins and owns the resulting engine state transition and stop reason.

None of those contracts should change for this proposal.

### Active package and source inventory

| Consumer | Reference | Actual use | Disposition |
| --- | --- | --- | --- |
| `src/Machina.UI/Machina.Dominatus` | `Dominatus.Core` 0.4.0 | Counter proof: world, agent, blackboard, event bus, HFSM, node, tick | Temporary JTF-M5 debt |
| `src/Machina.UI/Machina.Dominatus` | `Dominatus.OptFlow` 0.4.0 | `Ai.Event` authoring in the counter node | Temporary JTF-M5 debt |
| `tests/Machina.UI/Machina.Dominatus.Tests` | Core and OptFlow 0.4.0 plus project reference | Counter proof coverage; direct package references are redundant with the production reference but retained | Remove or relocate with the proof, not in M5a |
| `src/Aurelian/Aurelian.Runtime` | `Dominatus.Core` 0.4.0 | Runtime sessions, compositor policy actuation, smoke runtime, world runner seams | Legitimate Aurelian-owned use |
| `src/Aurelian/Aurelian.Core` | transitive Core surface through `Aurelian.Runtime` | `ActuatorHost` in frame-pump composition | Legitimate high-level Aurelian composition; review directness later, not in M5a |
| Aurelian tests | transitive Core surface | Runtime, compositor, actuation, and frame integration coverage | Legitimate owner/integration coverage |
| Presenter and component-gallery samples | project reference to `Machina.Dominatus` | No Dominatus source usage found in those samples | Stale graph debt for JTF-M5 consolidation |

`tools/dependency-boundary-exceptions.json` contains exactly two active exceptions: the `Machina.Dominatus` references to Core and OptFlow. M5a retains both.

> M5b status: this table is the pre-M5b inventory. The adapter and its tests now live under `src/Integrations` and `tests/Integrations`; the sample references and exception manifest are removed. The retained proof is a narrow coarse-lifecycle-hosting seam, not a Machina-core dependency.

## Current Dominatus authoring layers

The existing layers are coherent at their intended agent/runtime scale. The ergonomics problem appears when that scale is imposed on a local event reducer.

### Runtime context: `AiWorld`, `AiAgent`, `AiCtx`, and blackboards

| Property | Current behavior |
| --- | --- |
| Required setup | `AiWorld`; at least one `AiAgent`; an `HfsmInstance`; usually an `ActuatorHost`; typed `BbKey<T>` values for durable state |
| Execution | `world.Tick(dt)` advances the simulation clock, expires state, ticks actuators, then ticks agents in insertion order |
| State ownership | World/session state in `world.Bb`; agent state in `agent.Bb`; public snapshots and in-flight actuations on runtime objects |
| Event/input | Per-agent typed append-only `AiEventBus`; world mailbox sends typed messages to agent event buses |
| Effects | `IAiActuator`, normally `ActuatorHost`, dispatches typed `IActuationCommand` values to handlers |
| Inspection/trace | Blackboard revision/dirty keys, world/agent properties, checkpoints and replay; HFSM trace is attached separately |
| Determinism | Explicit simulation time, stable sequential agent order, typed event cursors, and deterministic expiry boundaries; host handlers can still introduce nondeterminism and must be replayed/controlled |
| Agent/world/tick dependency | This layer is the agent/world/tick runtime |
| Independent use | Blackboard and event bus types can be instantiated separately, but normal orchestration expects the full context |
| Intended scale | Long-lived agents, simulations, game/runtime policy, durable workflows, and coordinated effects |

### HFSM: `HfsmGraph`, `HfsmStateDef`, `HfsmTransition`, `HfsmInstance`

| Property | Current behavior |
| --- | --- |
| Required setup | String-backed `StateId` catalog, graph root, registered node per state, instance options, world and agent |
| Execution | Stack-oriented node execution; transitions/interrupts are predicate scans before node tick; interrupts win, then the first matching transition in list order wins |
| State ownership | Active state path and decision memory are private to `HfsmInstance`; durable behavior data is expected in blackboards |
| Event/input | HFSM transitions are `Func<AiWorld,AiAgent,bool>` predicates, optionally dirty-key filtered; typed events are consumed inside iterator nodes rather than passed to transitions |
| Effects | Stack changes and yielded steps; external effects go through actuation |
| Inspection/trace | `GetActivePath`; `IAiTraceSink` enter/exit/transition/yield callbacks; decision reports; persistence captures active path but not live iterator locals |
| Determinism | Stable top-to-bottom frame scan, interrupt-before-transition priority, list-order first match, and explicit cadence; guard exceptions are swallowed as non-matches |
| Agent/world/tick dependency | Yes |
| Independent use | No public event-dispatch operation independent of runtime exists |
| Intended scale | Hierarchical, interruptible, tick-oriented behavior with waits, subroutine-like push/pop, decisions, and persistence |

Graph construction rejects duplicate state keys through `Dictionary.Add`, and missing states fail when reached. There is no definition-wide `Validate()` that reports missing targets, unreachable rules, duplicate transition identities, or a graph visualization before execution.

### Nodes, steps, event waits, and actuation

| Property | Current behavior |
| --- | --- |
| Required setup | `IEnumerator<AiStep>` node, `NodeRunner` through an HFSM, and runtime context |
| Execution | Iterator advances until it waits, emits a control step, dispatches/awaits actuation, completes, or fails |
| State ownership | Iterator locals are transient; durable state belongs in blackboards; wait cursors live in `NodeRunner` |
| Event/input | `WaitEvent<T>` / `Ai.Event<T>` consumes typed per-agent events with `FutureOnly` default and optional `IncludeExisting` |
| Effects | `Act`, `AwaitActuation`, and registered typed handlers; immediate and deferred completion are explicit |
| Inspection/trace | Yielded steps are traceable; pending actuations and replay events are persisted |
| Determinism | Node order and step interpretation are explicit; effects depend on handler policy and replay discipline |
| Agent/world/tick dependency | Yes for normal execution |
| Independent use | A `NodeRunner` can be constructed directly but still requires world and agent on enter/tick; this is not a local reducer |
| Intended scale | Sequential behavior, waits, timeouts, external work, cancellation, and resumable workflows |

### `Dominatus.OptFlow`

| Property | Current behavior |
| --- | --- |
| Required setup | Same graph/node/runtime objects as Core |
| Execution | Static `Ai.*` factories return existing Core step/option values; OptFlow does not provide a second executor |
| State ownership | Unchanged from Core |
| Event/input | `Ai.Event<T>` is a concise `WaitEvent<T>` factory |
| Effects | `Ai.Act`, `Ai.Await`, navigation, waits, completion, and utility-decision step factories |
| Inspection/trace | Unchanged from Core |
| Determinism | Unchanged from Core |
| Agent/world/tick dependency | Yes, because its values are executed by Core |
| Independent use | Factories can be called independently; the returned steps are not independently dispatched |
| Intended scale | Readable iterator-node authoring over the Core runtime |

OptFlow reduces spelling, not activation ceremony. That is appropriate for its current role.

### `Ai.Decide` / `Ai.Option`

`Ai.Decide` returns the existing Core `Decide` step. `Ai.Option` creates a `UtilityOption` with an ID, target `StateId`, and `Consideration`.

The HFSM implementation:

- evaluates options in authored order and retains the first strict maximum;
- keeps decision memory per `DecisionSlot`;
- recomputes the current option's score each decision;
- blocks switching during `MinCommitSeconds`;
- requires the challenger to clear `Hysteresis`;
- prefers the current option within `TieEpsilon`;
- emits a `DecisionReport` containing all scores, selected/current IDs, switch status, and reason;
- changes the active state path through the existing HFSM semantics.

This is the utility capability requested by ordinary Dominatus authors. It should not be renamed or reimplemented. Its ergonomic limitation for a small UI choice is hosting: it is a yielded agent-runtime step, not a standalone one-shot call.

### `Dominatus.UtilityLite`

| Property | Current behavior |
| --- | --- |
| Required setup | `Consideration` delegates; blackboard helpers require an `AiAgent`; package references Core and OptFlow |
| Execution | Pure score evaluation/composition through `Utility` and the readable `When` facade |
| State ownership | None; decision memory remains in the HFSM's `Ai.Decide` execution |
| Event/input | No event dispatch; considerations read `AiWorld`/`AiAgent` and blackboard values |
| Effects | None |
| Inspection/trace | Individual scores can be evaluated; full reports come from `Ai.Decide` |
| Determinism | Deterministic when scorer delegates and inputs are deterministic; scores clamp to `0..1` |
| Agent/world/tick dependency | Score delegates use `AiWorld`/`AiAgent`, though constant/composed considerations can be built without ticking |
| Independent use | Combinators are independently usable; arbitration and memory are not implemented here |
| Intended scale | Readable construction of utility considerations for existing decisions |

UtilityLite is not a state-transition library and should not become one.

## Friction and root causes

The problem is a combination, with different weights:

1. **Missing capability:** Core has no typed, synchronous deterministic transition result with ordered effects. Its deterministic HFSM transition scanner selects state targets, but it does not accept a typed event or return an inspectable local result.
2. **Excessive setup ceremony:** local use currently requires a graph, node(s), HFSM instance, agent, world, event publication, and tick. Durable payloads also push authors toward blackboard keys.
3. **Awkward composition:** HFSM state is an internal stack while ordinary UI state is already immutable application data. Pointer capture and action output do not naturally return from one dispatch; they must be actuations, blackboard outputs, or host callbacks.
4. **Type/DSL ergonomics:** Core state targets are string-backed `StateId`s, transition predicates receive world/agent, and event types live inside nodes. OptFlow improves step spelling but cannot infer an application state/event/effect model that Core does not have.
5. **Missing event-to-runtime adapter:** `Ai.Event<T>` waits inside a node; there is no adapter from typed event dispatch into an independently testable reducer and then back into ordered runtime actuations.
6. **Documentation/discoverability:** upstream documentation is strong about the orchestration ladder and `Ai.Decide`, but it documents the gap as “use direct code or a dispatch table” rather than offering a reusable typed table with validation/trace. Older JTF Dominatus documents also described now-retired render integration and source vendoring as current.
7. **Misuse risk:** utility language is tempting when a library is branded around decisions. Scrollbar dragging, close acceptance, and exact breakpoint routing have one valid transition and should not be scored.

This is not evidence that Dominatus should absorb all reducers. A switch remains the baseline competitor.

## Semantic doctrine

### Deterministic transition

Use deterministic transition when an event and current state identify the first or only valid rule:

```text
(state, event, context) -> next state + ordered effects
```

Guards answer validity. Authored rule order resolves the rare case where multiple guarded rules match. There is no score, hysteresis, commitment window, randomness, or “best” option.

Examples: begin/end drag, apply pointer movement, accept/reject close, start/stop a runtime session, exact resize breakpoint, and parse/dispatch a UI action.

### Utility arbitration

Use utility arbitration when several options are simultaneously valid and the system must express preference:

```csharp
yield return Ai.Decide(
    slot,
    [
        Ai.Option("up", upScore, PopupStates.OpenUp),
        Ai.Option("down", downScore, PopupStates.OpenDown),
    ],
    hysteresis: 0.10f,
    minCommitSeconds: 0.50f);
```

Examples: a soft adaptive presentation policy when wide and compact are both viable, or contextual placement when both upward and downward popup placement fit but one is preferable.

### Composition

The abstractions compose in sequence, not by pretending they are one algorithm:

1. deterministic logic validates a request and records that a preference decision is required;
2. an effect supplies decision facts to an existing Dominatus runtime;
3. `Ai.Decide` arbitrates among the valid options using its existing policy and memory;
4. the chosen state/effect is delivered back as a typed deterministic event;
5. deterministic logic applies the chosen value and emits ordered host effects.

Hard validity should be decided before scoring. Utility should never turn an invalid option into a merely low-scoring option unless a valid fallback is guaranteed.

## Use case 1: scrollbar dragging

### Current end-to-end behavior

The production sample route is:

```text
UiInputBatch event
  -> PresenterNavigationInputRouter / OblivionPageInteractionMap
  -> hit part + current PresenterScrollbarInteractionState
  -> PresenterScrollbarInteractionStateMachine.Reduce
  -> next interaction state + optional UiActionId + capture request + suppression flag
  -> host performs capture/release
  -> presenter dispatch applies SetScrollOffset
  -> cached layers recompose
```

The current state machine implements:

- `Idle + primary press on visible thumb -> ThumbDragging + Capture + suppress`;
- `ThumbDragging + pointer move -> ThumbDragging + optional SetScrollOffset + suppress`;
- `ThumbDragging + any pointer-button release -> Idle + Release + suppress`;
- `Idle + track press -> Idle + page SetScrollOffset + suppress`;
- `Idle + wheel over scrollable viewport -> Idle + SetScrollOffset + suppress`;
- while dragging, unrelated pointer events remain suppressed.

Drag movement uses the geometry captured at press time. If the current scrollbar becomes invisible or the target no longer matches, movement emits no action but remains in `ThumbDragging`. The current foundational input contract has no pointer-capture-lost or pointer-cancel event. Keyboard input also bypasses this reducer. Therefore explicit cancellation is not current behavior; a future presenter-level `DragCancelled` event could be adapted without changing the generic transition API, but M5a does not add it to `UiInputBatch`.

The host currently applies capture/release before it dispatches the returned action. Any replacement must preserve that observable order.

### Direct switch/reducer baseline

This is the semantic baseline, expressed with the same application-owned event/effect vocabulary used by the proposed definition:

```csharp
static ScrollbarDispatchResult Reduce(
    ScrollbarState state,
    ScrollbarEvent input,
    ScrollbarContext context)
{
    return (state, input) switch
    {
        (ScrollbarState.Idle, ScrollbarEvent.ThumbPressed pressed)
            when context.Geometry.IsVisible =>
            BeginDrag(pressed, context),

        (ScrollbarState.Idle, ScrollbarEvent.TrackPressed pressed)
            when context.Geometry.IsVisible =>
            ScrollbarDispatchResult.Stay(
                state,
                [
                    new ScrollbarEffect.SetScrollOffset(
                        context.Target,
                        PageOffset(context, pressed.PointerY)),
                    new ScrollbarEffect.SuppressFurtherRouting(),
                ]),

        (ScrollbarState.Idle, ScrollbarEvent.WheelScrolled scrolled)
            when context.Geometry.IsVisible =>
            ScrollbarDispatchResult.Stay(
                state,
                [
                    new ScrollbarEffect.SetScrollOffset(
                        context.Target,
                        WheelOffset(context, scrolled.DeltaY)),
                    new ScrollbarEffect.SuppressFurtherRouting(),
                ]),

        (ScrollbarState.ThumbDragging dragging, ScrollbarEvent.PointerMoved moved)
            when IsValid(dragging, context) =>
            MoveThumb(dragging, moved, context),

        (ScrollbarState.ThumbDragging dragging, ScrollbarEvent.PointerMoved) =>
            ScrollbarDispatchResult.Stay(
                dragging,
                [new ScrollbarEffect.SuppressFurtherRouting()]),

        (ScrollbarState.ThumbDragging, ScrollbarEvent.PointerReleased) =>
            ScrollbarDispatchResult.To(
                new ScrollbarState.Idle(),
                [
                    new ScrollbarEffect.ReleasePointer(),
                    new ScrollbarEffect.SuppressFurtherRouting(),
                ]),

        (ScrollbarState.ThumbDragging, ScrollbarEvent.DragCancelled) =>
            ScrollbarDispatchResult.To(
                new ScrollbarState.Idle(),
                [
                    new ScrollbarEffect.ReleasePointer(),
                    new ScrollbarEffect.SuppressFurtherRouting(),
                ]),

        (ScrollbarState.ThumbDragging dragging, _) =>
            ScrollbarDispatchResult.Stay(
                dragging,
                [new ScrollbarEffect.SuppressFurtherRouting()]),

        _ => ScrollbarDispatchResult.Unmatched(state),
    };
}
```

This is locally excellent: the control flow is visible, types are application-native, dispatch is synchronous, and tests need no host.

### Least ceremonial current Dominatus expression

Current Dominatus can host the interaction, but it does not replace the reducer. The least ceremonial honest expression still needs approximately this shape:

```csharp
var graph = new HfsmGraph { Root = ScrollStates.Idle };
graph.Add(ScrollStates.Idle, IdleNode);
graph.Add(ScrollStates.Dragging, DraggingNode);

var actuator = new ActuatorHost();
actuator.Register(new ScrollbarEffectHandler());

var brain = new HfsmInstance(graph);
var agent = new AiAgent(brain);
var world = new AiWorld(actuator);
world.Add(agent);

agent.Bb.Set(Keys.Context, context);
agent.Events.Publish(inputEvent);
world.Tick(0f);

static IEnumerator<AiStep> IdleNode(AiCtx ctx)
{
    while (true)
    {
        ScrollbarEvent? input = null;
        yield return Ai.Event<ScrollbarEvent>(
            onConsumed: (_, value) => input = value,
            cursorStart: EventCursorStart.IncludeExisting);

        if (input is ScrollbarEvent.ThumbPressed pressed)
        {
            ScrollbarContext context = ctx.Bb.GetOrDefault(Keys.Context, default!);
            ctx.Bb.Set(Keys.Drag, CreateDrag(pressed, context));
            yield return Ai.Act(new CapturePointerCommand());
            yield return Ai.Goto(ScrollStates.Dragging);
            yield break;
        }

        // Track and wheel branches still have to be authored here.
    }
}
```

`DraggingNode` must repeat the event wait, recover drag/context data, branch on movement/release/invalid input, dispatch effects, and navigate back to idle. Durable/replay-safe payloads should be moved from iterator locals into blackboard keys. This buys full runtime actuation and tracing, but for this use case it obscures the direct transition table and creates a tick-shaped API around callback-shaped input.

### Proposed transition definition

The following is a compile-shaped design sketch. These types do not exist in M5a.

Application state, events, context, and effects remain application-owned:

```csharp
public abstract record ScrollbarState
{
    public sealed record Idle : ScrollbarState;

    public sealed record ThumbDragging(
        PresenterScrollbarTarget Target,
        float StartPointerY,
        float StartScrollOffset,
        ScrollbarGeometry StartGeometry) : ScrollbarState;
}

public abstract record ScrollbarEvent
{
    public sealed record ThumbPressed(float PointerY) : ScrollbarEvent;
    public sealed record TrackPressed(float PointerY) : ScrollbarEvent;
    public sealed record WheelScrolled(double DeltaY) : ScrollbarEvent;
    public sealed record PointerMoved(float PointerY) : ScrollbarEvent;
    public sealed record PointerReleased : ScrollbarEvent;
    public sealed record DragCancelled : ScrollbarEvent;
}

public sealed record ScrollbarContext(
    PresenterScrollbarTarget Target,
    ScrollbarGeometry Geometry,
    double ViewportHeight);

public abstract record ScrollbarEffect
{
    public sealed record CapturePointer : ScrollbarEffect;
    public sealed record ReleasePointer : ScrollbarEffect;
    public sealed record SetScrollOffset(
        PresenterScrollbarTarget Target,
        double Offset) : ScrollbarEffect;
    public sealed record SuppressFurtherRouting : ScrollbarEffect;
}
```

The definition is explicit about source state type, event type, next state type, guard, transition identity, and ordered effects:

```csharp
using Dominatus.Core.Transitions;

private static readonly TransitionDefinition<
    ScrollbarState,
    ScrollbarEvent,
    ScrollbarContext,
    ScrollbarEffect> ScrollbarTransitions = BuildScrollbarTransitions();

private static TransitionDefinition<
    ScrollbarState,
    ScrollbarEvent,
    ScrollbarContext,
    ScrollbarEffect> BuildScrollbarTransitions()
{
    var definition = Transition.Define<
        ScrollbarState,
        ScrollbarEvent,
        ScrollbarContext,
        ScrollbarEffect>(UnmatchedEventBehavior.Stay);

    definition.From<ScrollbarState.Idle>()
        .On<ScrollbarEvent.ThumbPressed, ScrollbarState.ThumbDragging>(
            id: "scrollbar.begin-thumb-drag",
            when: static (_, _, context) => context.Geometry.IsVisible,
            reduce: static (_, pressed, context) => Transition.Next(
                new ScrollbarState.ThumbDragging(
                    context.Target,
                    pressed.PointerY,
                    checked((float)context.Geometry.ScrollOffset),
                    context.Geometry),
                new ScrollbarEffect.CapturePointer(),
                new ScrollbarEffect.SuppressFurtherRouting()));

    definition.From<ScrollbarState.Idle>()
        .On<ScrollbarEvent.TrackPressed, ScrollbarState.Idle>(
            id: "scrollbar.page-track",
            when: static (_, _, context) => context.Geometry.IsVisible,
            reduce: static (idle, pressed, context) => Transition.Next(
                idle,
                new ScrollbarEffect.SetScrollOffset(
                    context.Target,
                    PageOffset(context, pressed.PointerY)),
                new ScrollbarEffect.SuppressFurtherRouting()))
        .On<ScrollbarEvent.WheelScrolled, ScrollbarState.Idle>(
            id: "scrollbar.apply-wheel",
            when: static (_, _, context) => context.Geometry.IsVisible,
            reduce: static (idle, scrolled, context) => Transition.Next(
                idle,
                new ScrollbarEffect.SetScrollOffset(
                    context.Target,
                    WheelOffset(context, scrolled.DeltaY)),
                new ScrollbarEffect.SuppressFurtherRouting()));

    definition.From<ScrollbarState.ThumbDragging>()
        .On<ScrollbarEvent.PointerMoved, ScrollbarState.ThumbDragging>(
            id: "scrollbar.move-valid-thumb-drag",
            when: static (dragging, _, context) =>
                context.Geometry.IsVisible &&
                Equals(context.Target, dragging.Target),
            reduce: static (dragging, moved, _) => Transition.Next(
                dragging,
                new ScrollbarEffect.SetScrollOffset(
                    dragging.Target,
                    CalculateOffset(dragging, moved.PointerY)),
                new ScrollbarEffect.SuppressFurtherRouting()))
        .On<ScrollbarEvent.PointerMoved, ScrollbarState.ThumbDragging>(
            id: "scrollbar.ignore-invalid-thumb-drag",
            reduce: static (dragging, _, _) => Transition.Next(
                dragging,
                new ScrollbarEffect.SuppressFurtherRouting()))
        .On<ScrollbarEvent.PointerReleased, ScrollbarState.Idle>(
            id: "scrollbar.end-thumb-drag",
            reduce: static (_, _, _) => Transition.Next(
                new ScrollbarState.Idle(),
                new ScrollbarEffect.ReleasePointer(),
                new ScrollbarEffect.SuppressFurtherRouting()))
        .On<ScrollbarEvent.DragCancelled, ScrollbarState.Idle>(
            id: "scrollbar.cancel-thumb-drag",
            reduce: static (_, _, _) => Transition.Next(
                new ScrollbarState.Idle(),
                new ScrollbarEffect.ReleasePointer(),
                new ScrollbarEffect.SuppressFurtherRouting()))
        .On<ScrollbarEvent, ScrollbarState.ThumbDragging>(
            id: "scrollbar.suppress-other-drag-input",
            reduce: static (dragging, _, _) => Transition.Next(
                dragging,
                new ScrollbarEffect.SuppressFurtherRouting()));

    return definition.Build();
}
```

This is intentionally not shorter than the switch. Its value is reusable metadata and tooling, not line count.

### Dispatch, state update, and ordered effects

```csharp
TransitionResult<ScrollbarState, ScrollbarEvent, ScrollbarEffect> result =
    ScrollbarTransitions.Dispatch(currentState, inputEvent, context);

currentState = result.NextState;

foreach (ScrollbarEffect effect in result.Effects)
{
    ApplyEffect(effect);
}
```

`Effects` is an immutable ordered collection. The release result returns `ReleasePointer` before `SuppressFurtherRouting`; a move returns `SetScrollOffset` before suppression. The application owns effect interpretation and may translate `SetScrollOffset` into its existing `UiActionId`.

`UnmatchedEventBehavior.Stay` returns the original state, no effects, and an inspection marked as unmatched. `Reject` returns a typed rejected result with the same inspection data; it does not throw unless the caller deliberately converts rejection into an exception. This keeps unmatched behavior explicit and replayable.

### Validation

```csharp
TransitionValidationReport validation = ScrollbarTransitions.Validate();

if (!validation.IsValid)
{
    throw new InvalidOperationException(validation.Format());
}
```

Definition-time validation should report, without reflection-based discovery:

- null IDs/delegates and duplicate transition IDs;
- source/event/next types not assignable to the definition's declared base types;
- an unguarded base-event rule followed by unreachable rules for the same source state;
- duplicate unguarded source/event rules;
- invalid unmatched-event policy configuration;
- optional deterministic DOT export from the explicitly registered generic rule metadata.

It cannot prove arbitrary guard predicates mutually exclusive. First-match order remains part of the definition and result trace.

### Trace and inspection

```csharp
TransitionInspection inspection = result.Inspection;

traceSink.Record(new ScrollbarTransitionTrace(
    inspection.TransitionId,
    inspection.Matched,
    inspection.SourceStateType,
    inspection.EventType,
    inspection.NextStateType,
    inspection.GuardsEvaluated,
    result.Effects.Count));
```

The result should contain previous state, event, next state, matched rule ID/index, ordered guard outcomes, unmatched policy outcome, and ordered effects. Tracing is pull-based data, not an ambient callback requirement. Tests can replay a recorded event/context sequence and compare states, rule IDs, and effects without executing the effects.

### Optional existing-runtime adapter

Direct UI use should stop at `Dispatch`. A behavior that already needs Dominatus waits, actuation, persistence, or world coordination may adapt the same definition into a node:

```csharp
AiNode node = TransitionNode.Adapt(
    definition: ScrollbarTransitions,
    stateKey: Keys.ScrollbarState,
    initialState: new ScrollbarState.Idle(),
    getContext: static ctx => ctx.Bb.GetOrDefault(Keys.ScrollbarContext, default!),
    toCommand: static effect => effect switch
    {
        ScrollbarEffect.CapturePointer => new CapturePointerCommand(),
        ScrollbarEffect.ReleasePointer => new ReleasePointerCommand(),
        ScrollbarEffect.SetScrollOffset set =>
            new SetScrollOffsetCommand(set.Target, set.Offset),
        ScrollbarEffect.SuppressFurtherRouting => null,
        _ => throw new ArgumentOutOfRangeException(nameof(effect)),
    });

graph.Add(ScrollStates.Interaction, node);
```

The adapter should use the existing typed event bus, store state through the supplied `BbKey<TState>`, emit mapped commands in result order, and expose each `TransitionInspection` through the existing trace sink. It should not define a second event bus, actuator, persistence system, or utility engine. Persistence semantics must be explicit: transition state is blackboard-backed; replay re-dispatches recorded external events; effect idempotency remains the actuator's responsibility.

The adapter is appropriate in Core because both the transition primitive and runtime types are Core semantics. UI callers do not use it.

### Scrollbar verdict

Keep the current handwritten reducer. The proposed definition adds validation, rule identity, generic replay, deterministic graph export, and a runtime adapter, but the JTF scrollbar already has straightforward local control flow and playback tracing. Migrating it before another behavior demonstrates material reuse would trade clarity for framework adoption.

If a later scrollbar migration is attempted, it must preserve exact pointer-capture/action ordering and either preserve the current lack of cancellation or introduce cancellation in a separately approved foundational-input milestone.

## Use case 2: adaptive shell preference

JTF has two real presentation strategies, `PresenterShellMode.Wide` and `PresenterShellMode.Compact`. The current resolver is an exact rule: width `>= 1120` selects wide; otherwise compact. That current requirement is deterministic and should remain a direct comparison.

A genuine utility case would arise only if product policy defines a band in which both modes are valid and preference depends on several signals—for example width headroom, whether the inspector is active, user density preference, and recent mode stability. In that policy, `Ai.Decide` is already the correct arbitration surface:

```csharp
static IEnumerator<AiStep> ChooseShellMode(AiCtx ctx)
{
    while (true)
    {
        yield return Ai.Decide(
            new DecisionSlot("presenter.shell-mode"),
            [
                Ai.Option(
                    "wide",
                    new Consideration((_, agent) =>
                        ScoreWide(agent.Bb.GetOrDefault(Keys.ShellFacts, default!))),
                    ShellStates.Wide),
                Ai.Option(
                    "compact",
                    new Consideration((_, agent) =>
                        ScoreCompact(agent.Bb.GetOrDefault(Keys.ShellFacts, default!))),
                    ShellStates.Compact),
            ],
            hysteresis: 0.10f,
            minCommitSeconds: 0.50f,
            tieEpsilon: 0.0001f);

        yield return Ai.Wait(0.10f);
    }
}
```

The hard minimum width for wide mode should be checked before this decision or encoded so compact always remains a valid positive fallback. The existing decision report provides the diagnostic evidence.

Nothing is missing in scoring, selection memory, or tie policy. The friction is that using this for ordinary UI still requires an HFSM host and target states. M5a does not recommend a duplicate standalone `DecideBest` API. If a real JTF UI preference policy is approved later, first try a small host that executes the existing `Decide` step/decision engine rather than cloning its algorithm or vocabulary.

## Use case 3: Aurelian close and lifecycle workflow

The close path is deterministic and already end to end:

```text
UiCloseRequested
  -> MachinaFrontendCloseRequested
  -> Aurelian.Machina creates AurelianCloseRequest
  -> frame input carries the command
  -> frame loop checks close before runtime tick/acquire/new frame
  -> AurelianFramePump.AcceptCloseRequest
  -> AurelianEngine.AcceptCloseRequest
  -> Started -> Stopped + CloseRequestAccepted
  -> close stop reason and diagnostic
```

Current acceptance has three ordered branches:

1. already accepted and stopped: idempotent success;
2. not started: reject with `CloseRequestRejected`;
3. started: set accepted, transition to stopped, return success.

That is naturally expressible through the proposed generic surface:

```csharp
var definition = Transition.Define<
    EngineLifecycleState,
    EngineLifecycleEvent,
    EngineLifecycleContext,
    EngineLifecycleEffect>(UnmatchedEventBehavior.Reject);

definition.From<EngineLifecycleState.Started>()
    .On<EngineLifecycleEvent.CloseRequested, EngineLifecycleState.Stopped>(
        id: "engine.accept-close",
        reduce: static (_, _, _) => Transition.Next(
            new EngineLifecycleState.Stopped(CloseAccepted: true),
            new EngineLifecycleEffect.ReportCloseAccepted()));

definition.From<EngineLifecycleState.NotStarted>()
    .On<EngineLifecycleEvent.CloseRequested, EngineLifecycleState.NotStarted>(
        id: "engine.reject-close-before-start",
        reduce: static (state, _, _) => Transition.Next(
            state,
            new EngineLifecycleEffect.ReportCloseRejected()));

definition.From<EngineLifecycleState.Stopped>()
    .On<EngineLifecycleEvent.CloseRequested, EngineLifecycleState.Stopped>(
        id: "engine.accept-close-idempotently",
        when: static (state, _, _) => state.CloseAccepted,
        reduce: static (state, _, _) => Transition.Next(state));

TransitionDefinition<
    EngineLifecycleState,
    EngineLifecycleEvent,
    EngineLifecycleContext,
    EngineLifecycleEffect> closeTransitions = definition.Build();
```

This proves the abstraction is not scrollbar-specific. It does not prove adoption value: the current method is shorter, explicit, well tested, and owns precise diagnostics. Close acceptance should not be migrated merely to dogfood the API.

The stronger future Aurelian dogfood candidate is `AurelianRuntimeSession` start/stop lifecycle, whose current state is split between `IsStarted` and `_hasStopped` and has more event/status combinations. That candidate already lives in the legitimate Dominatus-owning subsystem and can test definition validation without adding a Machina dependency.

## Proposed API boundary

### Core concepts

The minimum surface is:

- `TransitionDefinition<TState,TEvent,TContext,TEffect>`: immutable built definition;
- `TransitionDefinitionBuilder<...>` reached through `Transition.Define`;
- typed `From<TSource>().On<TEvent,TNext>()` rule registration;
- `Transition.Next` outcome factory with ordered effects;
- `Dispatch` returning `TransitionResult` plus `TransitionInspection`;
- explicit `UnmatchedEventBehavior.Stay` or `.Reject`;
- `Validate` and deterministic metadata/DOT export;
- optional `TransitionNode.Adapt` for existing Core runtime hosting.

No entry/exit callbacks are proposed for the first release. Pointer capture belongs to the transition that begins a drag and release belongs to the transition that ends it. Hidden entry/exit effects would make replay and rehydration ambiguous. If a later real use case requires entry/exit behavior, it should use the same explicit ordered-effect model and define whether restore re-emits those effects.

### Package location

The API belongs in `Dominatus.Core`, under a neutral namespace such as `Dominatus.Core.Transitions`.

Reasons:

- dispatch order, validation, results, and tracing are execution semantics rather than spelling aliases;
- Core already owns HFSM transitions, trace data, replay, and the optional runtime adapter types;
- `Dominatus.OptFlow` should remain a thin set of factories over Core values and should not become a second execution kernel;
- `Dominatus.UtilityLite` owns score construction, not deterministic state transition;
- a new package would add versioning and discovery cost for a small additive primitive;
- placing it in Machina would prevent general Aurelian/application use and invert the intended ownership.

OptFlow convenience methods may be considered only after real use shows repeated syntax that can be reduced without hiding rule order. They are not part of the M5b recommendation.

### Type and runtime assumptions

The implementation should use closed generics, ordinary delegates, arrays/read-only collections, and explicit registration. It should not scan assemblies, activate types through reflection, emit code dynamically, require expression compilation, or use a source generator. Generic `is` checks and explicitly captured type metadata are compatible with NativeAOT; deterministic AOT smoke tests remain required.

Generic Dominatus assemblies must not reference Machina, Aurelian, Avalonia, Silk.NET, Vulkan, renderer, or windowing types. Consumers supply their own state/event/context/effect records and effect adapters.

## Ergonomics evaluation

Scores are 1 (poor) through 5 (excellent) for the small deterministic application/UI use case. OptFlow is scored as current Core hosting with its available authoring helpers; `Ai.Decide` is evaluated separately as the utility baseline.

| Criterion | Handwritten switch/reducer | Raw Core/HFSM | Current OptFlow | Proposed transition surface |
| --- | ---: | ---: | ---: | ---: |
| Setup ceremony | 5 | 1 | 2 | 4 |
| Local readability | 5 | 2 | 3 | 4 |
| Type safety | 5 | 3 | 3 | 5 |
| Testability | 5 | 3 | 3 | 5 |
| Determinism | 5 | 5 | 5 | 5 |
| Debuggability | 5 | 3 | 4 | 5 |
| Traceability | 3 | 5 | 5 | 5 |
| Reuse | 3 | 4 | 4 | 5 |
| Existing runtime integration | 1 | 5 | 5 | 4 |
| NativeAOT suitability | 5 | 5 | 5 | 5 |
| Small UI appropriateness | 5 | 1 | 2 | 4 |
| **Total / 55** | **47** | **37** | **41** | **51** |

The score does not automatically select the proposal. A switch wins when validation/trace/reuse are not needed. The proposal's four-point margin comes entirely from reusable diagnostics and integration, not simpler local control flow.

For genuine utility choice, current `Ai.Decide`/`Ai.Option` score 5 for determinism, policy, traceability, and runtime integration; their small-UI setup score is 1. The missing item is a thinner host, not a new decision API.

### Adoption threshold

A JTF migration may be retained only if all gates pass:

1. exact behavioral parity, including unmatched and invalid input, effect order, and diagnostics;
2. no change to `UiInputBatch`, screen/layer contracts, rendering contracts, or backend ownership;
3. direct dispatch creates no world, agent, blackboard, iterator, or tick;
4. validation catches at least duplicate IDs, unreachable unguarded fallbacks, invalid generic state/event targets, and invalid unmatched configuration;
5. result traces replay deterministically and graph export is stable;
6. NativeAOT analysis/tests pass without reflection/dynamic-code annotations;
7. at least two real behaviors reuse the surface, or one behavior has a demonstrated trace/replay/validation requirement that a switch does not meet;
8. reviewers judge at least three of validation, diagnostics, replay, reuse, or runtime adaptation materially better without judging local control flow worse.

On current evidence, the scrollbar and close method do not clear gates 7 and 8. The proposed layer is justified for upstream implementation/prototyping, but neither current behavior should be migrated in M5b merely for adoption optics.

## Rejected alternatives

### Rename or duplicate `Ai.Decide`

Rejected. The capability exists and has the required policy/memory/trace semantics. A duplicate would split doctrine and risk behavioral drift.

### Express dragging as utility scoring

Rejected. Press, move, release, and cancellation are valid/invalid transitions, not preferences.

### Force small reducers into the current HFSM

Rejected. It keeps effects and persistence but imposes agent-scale activation and separates one callback into publish/tick/actuation phases.

### Put deterministic transitions in `Dominatus.UtilityLite`

Rejected. UtilityLite builds considerations and score math; deterministic dispatch has no score.

### Put the whole engine in `Dominatus.OptFlow`

Rejected. OptFlow currently creates Core values and should remain an authoring facade. The proposed result and validation semantics belong beside the runtime kernel. A future thin OptFlow spelling layer can depend on Core if evidence warrants it.

### Add `Dominatus.Transitions` as a new package

Rejected for the first release. The implementation is small, has a natural Core runtime adapter, and does not justify another package/version lane.

### Make Machina own a generic reducer framework

Rejected. Machina owns UI policy and may keep direct reducers, but a reusable Dominatus runtime authoring primitive should not depend on or be defined by Machina types.

### Reflection-heavy fluent DSL, attributes, or source generation

Rejected. They obscure rule order, complicate NativeAOT, and add build/runtime magic without improving the motivating case.

### Automatic entry/exit effects

Deferred. Explicit transition effects are clearer for capture/release, replay, and restore. Add entry/exit only after a real case defines restore behavior.

### Documentation only

Insufficient as the long-term answer if Dominatus intends to support small stateful application workflows: the event-dispatch operation genuinely does not exist. Documentation is sufficient for utility arbitration today and should explicitly direct simple field updates to switches/tables.

## Ownership and dependency direction

The corrected doctrine is:

- generic deterministic transition and utility authoring belongs to Dominatus;
- Dominatus generic packages know nothing about Machina, Aurelian, Avalonia, rendering, or platform input;
- Machina owns input, UI actions, local UI policy, screens, and presentation, and may consume a generic Dominatus authoring package only after the adoption threshold is met;
- a Machina production dependency is not automatically justified merely because the generic package exists;
- Aurelian owns game/world/runtime use of Dominatus and is the preferred first JTF dogfood owner;
- `Aurelian.Machina` or another explicitly integration-owned package owns cross-system adaptation;
- backend choice, rendering contracts, foundational input, screen composition, and engine lifecycle ownership do not move.

The M5 end state should remove project-specific exceptions. An approved generic package edge in a deliberately selected application/runtime layer is a normal reviewed dependency, not a waiver. The current `Machina.Dominatus` proof and its two broad exceptions do not satisfy that standard and remain scheduled for consolidation.

## Compatibility and release strategy

The transition API is additive. Existing graphs, nodes, steps, decisions, blackboards, trace sinks, persistence, and package namespaces remain valid. `Ai.Decide` behavior and signatures do not change.

A future release must flow in this order:

1. implement the pure definition/result/validation surface and tests in the Dominatus repository;
2. implement the optional Core runtime adapter against the same result semantics;
3. document deterministic-versus-utility selection in the upstream authoring guide and orchestration ladder;
4. publish a versioned package (a prerelease is acceptable for dogfood); do not source-copy it or add a permanent cross-repository project reference;
5. update JTF package pins only after publication;
6. dogfood one Aurelian-owned lifecycle behavior with full before/after parity tests;
7. retain the migration only if it clears the adoption threshold; otherwise keep the direct code and record why;
8. proceed with Machina Dominatus proof retirement and exception removal only after the authoring decision is stable.

## Recommendation for JTF-M5b

JTF-M5b should be narrowly scoped to delivering and evaluating the proposed layer, not consolidating all Dominatus ownership at once:

1. In Dominatus, add `Dominatus.Core.Transitions`, validation/trace/DOT metadata, deterministic replay tests, NativeAOT readiness coverage, and the optional `TransitionNode` adapter.
2. Do not change `Ai.Decide`, `Ai.Option`, `DecisionPolicy`, or UtilityLite semantics. Add only documentation that makes the deterministic/utility choice obvious.
3. Publish a prerelease or normal package version from a recorded commit.
4. In JTF, update the package pin and dogfood only the `AurelianRuntimeSession` start/stop lifecycle, because it is already Aurelian-owned, already Dominatus-backed, and has an implicit two-boolean state worth validating.
5. Do not migrate the scrollbar or close acceptance method in M5b. Keep them as comparison controls.
6. If the dogfood fails the threshold, revert it and conclude that the upstream surface is not yet justified for JTF production.

After a successful M5b, JTF-M5c may consolidate Dominatus ownership: retire the obsolete `Machina.Dominatus` counter proof and stale sample references, relocate/retain only owner-appropriate tests, remove the two dependency exceptions, and verify that any remaining package edge is a deliberate normal dependency rather than a waiver.
