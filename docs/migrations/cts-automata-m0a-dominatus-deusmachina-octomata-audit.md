# CTS-AUTOMATA-M0a Dominatus, DeusMachina, and Octomata audit

## Scope and repository evidence

This documentation-only audit made no change outside Copeland and did not browse the internet.

| Repository | Revision / branch | Worktree / upstream | Read-only inspected paths |
| --- | --- | --- | --- |
| Copeland | `e2e88e92b715fa243626c15fc16fbdb60ce6ca38`, `main` | clean, `origin/main...main` 0/0 | `src/Copeland/Copeland.TS.Mir/MirNodes.cs`, `MirValidator.cs`, both backend files, relevant docs/tests/tools |
| Dominatus | `9b43e7912332856e6095d62c530f58049b1b5150`, `master` | clean, synchronized | `src/Dominatus.Core/Hfsm/{HfsmGraph,HfsmStateDef,HfsmTransition,HfsmInstance,HfsmOptions}.cs`; `Nodes/{AiNode,AiStep,NodeRunner}.cs`; `Runtime`; `tests/Dominatus.Core.Tests` |
| MachinaLayout.JS | `603293d845afd53e70de9b936652bb267d565b33`, `main` | two pre-existing untracked files: `docs/tspack-dvt-m44a.md`, `manifest.tsx`; synchronized otherwise | `src/deus/{types,machine,utility,fromTable,scopedTransitions,workflow,templateTable,debugOverlay}.ts`; `src/*/useDeusMachine.ts`; `docs/{deusmachina,deus-async-tasks}.md`; `test/deus` |
| Oct | `b07c8849efa00fe0455e827e9a162856f389878f`, `main` | clean, synchronized | `Language/reference/runtime/21-octomata.md`; `Language/ControlFlow/Octomata*`; `Libraries/Octomata`; interpreter/compiler/backend locations and flow tests |

The two MachinaLayout.JS changes were observed and preserved. Dominatus and Oct current source/test paths are implementation evidence. Oct `Experiments`, old control-flow milestone folders, and supporting reports are historical/prototype evidence unless corroborated by the current runtime reference and valid/runtime fixtures.

## Exact inventory and findings

| System | Current inventory | What it actually is | Reusable law | Reject/adapt |
| --- | --- | --- | --- | --- |
| Dominatus | `HfsmGraph`, `HfsmInstance`, `HfsmStateDef`, `HfsmTransition`, `StateId`, `NodeRunner`, `AiStep`, wait/event/actuation tests | C# hierarchical ticked AI agent runtime with blackboard, events, actuators, replay and tracing | explicit wait state, matching event, event-before-timeout, cancellation/dispose, ordered transitions | all runtime policy, hierarchy, delegates, mutable world/agent, enumerator/CTS implementation |
| DeusMachina | `DeusMachine`, `DeusSnapshot`, transition/state rows, `defineDeusMachine`, `stepDeusMachine`, table/workflow lowering, hooks, machine/fromTable/scoped/hydration tests | TS authoring plus runtime UI/workflow state kernel | validated tables, deterministic ordering, state/edge traces, snapshot transparency | UI events, hierarchy, callbacks, board mutation, stack, utility/hysteresis, React/Vue lifecycle |
| Octomata | `flow/state/board/goto/suspend/resume` reference; CoreA/CoreB/Resume/Observability/CompiledBoundary fixtures; Octomata library | Oct user-facing behavioral control runtime, interpreted and partly Go-compiled | explicit state, fixed storage, bounded step, state history, compiler validation | source language/API, user boards, policy/arbitration, Go implementation, no async lifecycle model |

### Dominatus audit detail

State identity is non-empty `StateId`; graph insertion uses a dictionary, while transitions are ordered lists. Guards are delegate predicates over mutable runtime context; node effects are iterator steps. Entry creates an iterator and cancellation source; exit cancels/disposes it. `HfsmInstance` has hierarchical active frames, interrupt scanning, regular scan intervals, utility decision policy, tracing, replay/persistence integration, and no compiler graph analysis. It is synchronous tick execution with asynchronous-like event waiting. Tests exercise actuation completion, immediate/deferred waiting, transition ordering/rejection, persistence and runtime policy. Its runtime dependencies and delegate-heavy application model make a reusable package and NativeAOT claim inappropriate.

### DeusMachina audit detail

State identity is path identity, including implicit ancestors. Transition identity is a unique key. Events enter through a typed discriminator passed to `stepDeusMachine`; action callbacks mutate caller board and entry/exit callbacks run around state change. The runtime is deterministic by source array order and utility tie policy and exposes a trace. It validates definition shape/targets but has no compiler CFG/liveness, await continuation, cancellation protocol, or concurrency semantics. `deus-async-tasks.md` is a task/UI pattern, not generated compiler lowering. Its best lesson is that opaque generated transition machinery is poor authoring material: Copeland should generate compact switch cases with source provenance, not expose or author a giant table.

### Octomata audit detail

State identity is a declared source name. `goto` targets and ordered `when` actions are compiled/validated. Board fields carry fixed typed control memory; ordinary state locals are intentionally not cross-state storage. `suspend` leaves flow active until later `Step`; `remember`/`resume` offer one control-resume slot. `Result(flow)` is fallible until completion. Fixtures prove validation and deterministic execution, while compiled-boundary cases prove supported Go realization. Octomata has max-step/resource concepts and checkpointing consumers, but no general cancellation, disposal, transport, or parallel async contract. It is the closest semantic evidence for an explicit compiler/interpreter seam, not a library to import.

## Copeland audit

`MirNodes.cs` confirms structured MIR rather than a CFG. `MirValidator.cs` validates structured loops and lexical Result handlers. `MirTextWriter.cs` is deterministic text. Existing `MirFunction.Locals` and backend emission temporaries stage ordinary values but cannot survive host-stack suspension. `MirPropagationTarget.FunctionReturn` and `.LexicalExcept(MirHandlerId)` define typed transfers. C# emits structured constructs directly; JavaScript emits Diagnostic/Symbolic structured constructs through backend-local name/flow records. Callable capture environments are explicit immutable values; frame carriers must not reuse them as semantic identity. No present node or test represents await/yield/sidecar/resume.

## Decision record

1. Adopt the hybrid suspension-lowering model in the paired architecture document.
2. Keep state IDs semantic-path-derived and dense numbering deterministically sorted; never use dictionary iteration or merely traversal position as durable provenance.
3. Use iterative CFG/liveness worklists and bounds.
4. Keep declared Result error separate from transport failure, cancellation, and terminal invariant panic.
5. Keep the implementation compiler-local until a compatible second consumer proves extraction.
6. Fold synthetic automaton proof and first async implementation into CTS-ASYNC-M1; sidecar follows it; iterator follows sidecar/async after lifecycle approval.

## Risks and unresolved owner choices

- Ratify the async return/cancellation surface and whether a cancellation token/value is source-visible.
- Decide the public observation shape separating declared sidecar `err` from transport failure.
- Approve cleanup/finally/disposal semantics before generators.
- Calibrate proposed limits with M1 corpus; unreachable-state severity and no-progress-cycle analysis must not reject legal infinite loops.
- NativeAOT is only a design constraint until an actual publish artifact is proven.

## Validation performed

- Repository revisions, branches, status, and upstream divergence recorded before edits.
- Targeted `rg` inventories searched Dominatus, DeusMachina, Octomata, automata/state/transition/control, suspend/resume, async/await, yield/iterator, dispatch/event, guard/effect, cancellation/timeout.
- Cross-repository paths above were verified as existing read-only evidence paths.
- Copeland links and Markdown checks are completed with the final documentation validation recorded in the change handoff.
