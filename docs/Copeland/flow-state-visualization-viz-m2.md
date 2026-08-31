# Flow/state semantic visualization — VIZ-M2

VIZ-M2 projects the compiler's existing `flow` semantics directly. It does not
parse source a second time and it does not reconstruct a state machine from
generic control flow:

```text
flow source -> parser/binder -> BoundFlowDefinition -> StateMachineSemanticView
                                                    -> Diagram -> Mermaid
```

The opt-in tool path is:

```powershell
tscl flow visualize VehicleFlow.ts --name VehicleFlow --output vehicle.mmd
```

This direct named-flow selection is intentional. Copeland flows do not yet have
a source-level value model, and direct visualization does not need
`statesOf<F>()`, `transitionsOf<F>()`, or any other reflection query.

## Existing flow law

A flow is a module-level, compiler-owned event automaton. It declares a fixed
typed board, named payload events, and named states. Exactly one state carries
the explicit `initial` marker. A transition has this existing form:

```ts
on Event(payload) when guard -> Target {
    board.field = value;
};
```

The optional guard is bound as an ordinary typed `BoundExpression`. FLOW-M1
already restricts it to pure locals and board reads. The optional body contains
only explicit board updates. `finish` and `fail` are explicit terminal outcomes;
a state with no outgoing transition is not terminal merely because it is a
leaf. Transitions retain state and source declaration order. FLOW-M1 rejects
two transitions for the same event in one state, while distinct events may
legitimately produce parallel source/target edges.

## Semantic view and identities

`BoundFlowDefinition` remains the canonical bound state-machine
representation. `StateMachineSemanticView` is a bounded, syntax-free view over
that representation:

```text
identity, name, states, transitions, initial-state identity,
final-state identities, source correlation
```

State IDs reuse the compiler's existing form
`flow:<flow>.state:<state>`. Transition IDs are now compiler-owned and stable
within authored transition order:
`flow:<flow>.state:<state>.transition:<state-local-index>`. Display labels and
Mermaid IDs are separate. Mermaid IDs are deterministic `s0`, `s1`, ... values
derived from normalized semantic state identity; source positions and random
GUIDs are never identities.

Every transition view retains its source state, target state, event, bound
guard, global semantic order, full transition correlation, and guard
correlation. Flow and state correlations are also retained as path plus
one-based start/end line and column. No syntax node or source string escapes
into the visualization model.

## Initial, final, guards, and actions

The initial identity comes from the binder's explicit initial-state law. Final
identities are exactly states with a bound `finish` or `fail` terminal. Mermaid
therefore emits `[*] --> Initial` and `Terminal --> [*]` without leaf inference.

Guard display is produced from the bound pure expression subset: literals,
locals, board-field reads, unary/binary operators, and compiler numeric
conversions. It is not sliced from source. Displays up to 120 UTF-8 bytes are
complete; longer displays are deterministically shortened with `...`, while
the full bound guard and source correlation remain in the semantic view. A
semantic guard display above 4,096 UTF-8 bytes or an unsupported bound form is
an explicit diagnostic, never a silently missing label.

Events are already first-class, so edges use `Event [guard]`. Board updates are
also already first-class transition action/effect data, but VIZ-M2 classifies
them as `PRESENT_NOT_VISUALIZED`: they remain in `BoundFlowTransition.Updates`
and runtime MIR, but are not added to the state edge label.

## Projection and runtime boundary

The two lowerings are sibling consumers of semantic truth:

```text
                 BoundFlowDefinition
                    /          \
             MIR/runtime     StateMachineSemanticView
                                    |
                                  Diagram
                                    |
                                 Mermaid
```

Visualization is compiler/tool-time only. It adds no runtime metadata lookup,
`System.Reflection`, state-machine introspection, or NativeAOT dependency. MIR
continues to receive states, guards, transitions, and board updates through the
existing lowering.

## Bounds and diagnostics

VIZ-M2 permits at most 256 states, 1,024 transitions, 4,096 UTF-8 bytes for a
full semantic guard display, 120 UTF-8 bytes for the visible guard display, and
1,048,576 emitted Mermaid bytes. State/transition collections are never
silently truncated. Focused diagnostics cover unknown flow selection, missing
targets or initial state, missing identities, state/transition bounds,
unrepresentable guards, emitted-size bounds, and invalid Diagram references.
Ordinary invalid flow source continues to use the existing `COPE-FLOW-*`
diagnostics rather than duplicating flow validation in the visualizer.

## Dogfood result and non-goals

`samples/copeland-ts/visualization/VehicleFlow.ts` proves initial, terminal,
self, bidirectional, parallel, and guarded transitions without duplicating any
state or transition list. The existing `PantryRun` flow is also materialized as
a repository dogfood target.

The rendered view matches the authored topology and guards add the essential
reason each edge is available. No important M2 state topology is missing.
Board-update actions could become future pressure only if real diagrams need
them; this milestone does not schedule that work. Mermaid `stateDiagram-v2` is
adequate, Diagram IR required only a backend kind plus initial/final metadata,
and direct flow visualization confirms that reflection is unnecessary.

VIZ-M2 adds no CFG, SSA, dominators, dataflow, runtime tracing, flow reflection,
effects reflection, native SVG, graph layout, simulator, or editor.
