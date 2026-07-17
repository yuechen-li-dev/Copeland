# Copeland TS suspension automata (CTS-AUTOMATA-M0a)

**Status:** architecture and audit decision only. This document adds no source syntax, MIR node, runtime, package, or backend behavior.

## 1. Problem statement

Copeland TS needs `async`/`await` to make a typed sidecar call without blocking a host thread. It will later need iterator and possibly generator progression. These are both resumable computations: they execute ordinary code until a named suspension, retain selected values, and later resume through a deterministic terminal outcome. They are not permission to turn all Copeland control flow into a general state-machine language.

```text
typed structured bound/MIR
        -> shared suspension analysis and lowering
        -> validated, backend-neutral suspension automaton
        -> C# or JavaScript realization
```

The canonical meaning stays Copeland-owned. C# `async`/`await`, `Task` cancellation, JavaScript Promise rejection, and generator objects may be useful scheduling or optimization mechanisms, but do not define the source law.

## 2. Why async and iterators require resumable automata

An async function has an entry state, ordinary synchronous regions, `await` suspension, a matching success/failure resume, and terminal completion/cancellation/invariant outcomes. A future iterator has initialization, ordinary regions, `yield` suspension with a value, `next` resume, completion, and early disposal. Both need explicit program counters, a retained frame, terminal lifecycle discipline, and a backend realization. Their public types and laws remain distinct: an async function produces an asynchronous computation; an iterator produces an iterator protocol value. One must not be disguised as the other.

## 3. Evidence: Dominatus

Dominatus is a current C# AI/control runtime, not a compiler lowering substrate. Its `HfsmGraph` owns a string-like `StateId` root and dictionary of state definitions. A `HfsmStateDef` has an `AiNode` plus ordered interrupt and normal transition lists. `HfsmTransition` carries a `Func<AiWorld, AiAgent, bool>` guard, target, reason, and optional blackboard dependencies. `HfsmInstance` maintains a hierarchical active stack, scans guards on ticks, applies the first enabled transition, and invokes node runners.

`AiNode` is an `IEnumerator<AiStep>`. `NodeRunner` owns the enumerator, per-node cancellation token source, wait/event state, and an explicit loop which advances an iterator until it must wait or emits a control step. It models `WaitSeconds`, `WaitUntil`, event/actuation waits, `Goto`, `Push`, `Pop`, `Succeed`, and `Fail`. Event consumption is deliberately ordered before a same-tick timeout; cancellation/disposal happens on exit; errors in predicates or iterator execution are converted to failed node completion. Tracing reports entry, exit, transitions, and yields.

This is useful evidence for explicit wait installation, matching response identity, event-before-timeout ordering, and disposal. It is not reusable as Copeland lowering: its state identity is caller-authored strings, guards/actions are host delegates over mutable `AiWorld`/`AiAgent`, state hierarchy/push-pop, utility arbitration, tick cadence, replay/persistence, actuators, clocks, and AI policies are application-runtime policy. Its dictionary graph, delegate model, and `IEnumerator`/CLR cancellation semantics cannot become compiler law or a NativeAOT proof.

## 4. Evidence: DeusMachina

DeusMachina in MachinaLayout.JS is a live TypeScript authoring/runtime control kernel for UI/workflow state, not generated TypeScript MIR. `DeusMachine` contains an initial path, state rows, and explicit transition rows. Paths are non-empty segment arrays; transitions have unique keys, source path, optional event discriminator, optional guard, score/utility/hysteresis policy, target/control operation, action, and reason. `defineDeusMachine` validates paths, duplicate states/transitions, target existence, finite scores, and control targets, then materializes missing hierarchy ancestors.

`stepDeusMachine` is a pure-ish explicit stepping operation over a caller-provided board/snapshot/event. It searches the active state then parent paths deterministically, preserves row order, evaluates eligibility, chooses by score/utility with deterministic tie behavior, performs exit/action/entry behavior, and returns both a new snapshot and a detailed trace. Snapshots retain state, board, stack, and step index; `goto`, `push`, `pop`, and `stay` are UI/workflow controls. Table, scoped-transition, and workflow helpers lower authoring conveniences into ordinary transition rows. React, React Native, and Vue hooks adapt this kernel to UI lifecycle.

It proves the value of data-driven transition tables, validation before execution, deterministic source-row ordering, explicit snapshot/trace provenance, and treating TypeScript as an implementation language rather than a semantic authority. It also demonstrates costs Copeland should avoid: hierarchical path resolution, parent fallbacks, UI event types, mutable board callbacks, user-authored function guards/effects, stack control, utility arbitration, and verbose table/workflow authoring are unrelated to a compiler-generated function continuation. Its async-task documentation is UI task orchestration, not a canonical compiler suspension machine.

## 5. Evidence: Octomata

Octomata is Oct's explicit user-facing behavioral/control-flow runtime. `flow Name(...) -> T` contains named `state` bodies, optional fixed-shape typed `board` memory, `goto`, `suspend`, `return`, ordered guard `when`, and a single remembered resume target. `Step(flow)` executes a bounded scheduling step; `Active`, `Complete`, `Result`, `ResumeTarget`, `StateHistory`, and `BoardSnapshot` expose observation. The compiler validates at least one state, unique state names, existing `goto` targets, state-only controls, return types, board shape/write scope, and Result handling. Runtime tests cover step/suspend/resume/return, state history, fixed board state, and deterministic first-true guards.

Oct's interpreter executes flows explicitly; its generated Go boundary compiles supported flow bodies while retaining a flow instance concept. The `make` facility additionally checkpoints suspended flows, retaining current state/history and board-compatible values. It uses explicit maximum steps for resource bounding. The source and tests distinguish completed results from an uncompleted/suspended flow rather than conflating them. Octomata is a mixture of user-facing automata, a control kernel, and compiler infrastructure; it is not specifically async/iterator machinery. Cancellation, async I/O, concurrent transition execution, general disposal, and unrestricted cross-state locals are not its model.

Its direct lessons are stable named states, fixed explicit persistent storage, transition-only control operations, validation, execution bounds, state history, and an interpreter/code-generation seam. Copeland must reject its user-authored board/flow syntax, controller/utility policy, one-slot resume feature, and Go-specific runtime shape. Copeland requires compiler-derived live values and typed sidecar outcomes rather than an author-controlled blackboard.

## 6. Cross-implementation comparison

| Concern | Dominatus | DeusMachina | Octomata | Copeland need |
| --- | --- | --- | --- | --- |
| State identity | caller `StateId` string | hierarchical path | source state name | stable compiler semantic path plus dense deterministic `StateId` |
| Transition representation | ordered delegate guards/targets | explicit rows, callbacks | source `goto`/ordered `when` | compiler-generated discriminants and targets |
| Guard/effect separation | guard separate from node steps | `when` and `do` fields | guards and bounded actions | decision separate from ordinary continuation block |
| Persisted locals/context | agent blackboard/world | board/snapshot/stack | fixed typed board | only liveness-selected frame slots |
| Suspension/resumption | iterator waits/events | event-driven steps, no compiler continuation | `suspend`, remembered target | await/yield resume protocol |
| Completion/failure | node success/fail | no universal terminal algebra | `return`, fallible `Result` query | success, declared err, transport, cancellation, invariant terminal outcomes |
| Cancellation/disposal | token cancel plus enumerator dispose | UI lifecycle external | not a general cancellation model | explicit compiler/runtime lifecycle contract |
| Determinism | ordered lists/tick policy | row order/trace | source ordered guards | source semantic order, dense IDs |
| Validation | graph lookups/runtime behavior | comprehensive definition checks | compiler diagnostics | shared lowering/automaton validator |
| Execution strategy | ticked hierarchical runtime | snapshot stepper | interpreter and Go generation | switch/worklist continuation driver |
| Backend/runtime assumptions | CLR iterators/tasks/world | TS callbacks/UI | Go/interpreter | no scheduler, host construct optional |
| Resource bounds | policy/tick configured | no compiler bounds | max steps and fixed board | explicit function/analysis bounds |
| Recursion avoidance | explicit loops/ticks | iterative row scans | stepping model | iterative CFG/worklist/liveness |
| Debug/provenance model | trace sink/replay | step trace/overlay | state history/snapshots | source span/path per state, slot, edge |
| NativeAOT suitability | not established; delegates/iterators | not applicable | Go-specific | design for direct generated carriers; publish proof later |

## 7. Current Copeland control-flow audit

Current Cope MIR is structured statements and expressions, not a CFG: `MirIfStatement`, `MirWhileStatement`, `MirForStatement`, `MirBreakStatement`, `MirContinueStatement`, and `MirReturnStatement` retain lexical shape. `MirValidator` validates boolean loop conditions and loop depth; `MirTextWriter` gives deterministic textual projection. Values survive normal branch/loop lowering as backend lexical locals and staged temporary expressions. This is sufficient only while control never leaves the host stack.

The existing Result path is explicit: `MirResultType`, `MirOkExpression`, `MirErrExpression`, `MirPropagateExpression` targeted at function return or a lexical `MirHandlerId`, `MirUnwrapExpression`, `MirResultMatchExpression`, and `MirTryExpression`. `try`/`except` accepts typed Result flow only. C# and JavaScript backend records and temporary staging help preserve evaluation order but are backend-private emission machinery, not an automaton frame. Callable environments are immutable explicit captured values; a suspension frame is mutable/immutable execution activation storage and must remain conceptually distinct. Class carriers and their private invariants remain unchanged.

There is no async syntax/binding, await/yield MIR, CFG, live-across-suspension analysis, cancellation outcome, sidecar call, response dispatcher, interpreter, or source-span/stable-node provenance sufficient for this feature. Existing structured control lowering is the input to a new shared pass; it must not be incrementally patched into either backend.

## 8. Selected representation

Choose **C, hybrid representation**. Retain typed structured async/iterator semantic MIR only long enough for binding, Result law, and authored evaluation order. A single verified suspension lowering creates a lower backend-neutral `SuspensionAutomaton` consumed by C# and JavaScript. Do not use A: it duplicates correctness-critical lowering in two backends. Do not use B as the public/high-level MIR: it loses feature-local law too early. Do not use D now: one compiler-local consumer does not meet graduation doctrine.

The automaton is bounded resumable function control only. It has no hierarchy, parallel regions, user-defined events, scores, arbitrary callbacks, scheduler, timer, or general-purpose data board.

## 9. State, transition, and frame-slot law

Proposed compiler-local concepts:

```text
AutomatonId(function stable semantic identity)
StateId(semantic-path-derived stable identity, dense emission ordinal)
EntryState
ExecutionState(ordinary continuation block)
AwaitSuspension(operation slot, success target, transport-failure target)
YieldSuspension(value slot, next target)           // future iterator only
Complete(value slot?)
DeclaredResultError(error slot)
Cancelled(reason slot?)
InvariantFailure(diagnostic/provenance)
FrameSlot(id, type, mutability, source local provenance)
```

An execution state contains a closed lower-level continuation block: existing validated MIR statements/expressions after explicit staging, plus terminators (`branch`, `jump`, `await`, future `yield`, `return`, Result transfer). It must not contain arbitrary backend callbacks. A transition is an ordered discriminant/condition, assignment list, target, and provenance. Dense state numbers are assigned after deterministic semantic-path sorting; paths use function identity plus structured child roles (`body/if-then/loop-test/.../await`) and a local ordinal only within that semantic parent. Dictionary iteration never chooses identity.

## 10. Structured-control lowering and live values

Lower sequential code into one state until a terminator or suspension boundary. Split before/after an `await` or future `yield`, at branch tests/joins, loop test/body/increment/exit, returns, and typed Result transfer targets. `break` and `continue` become explicit jumps to the lexical loop exit/increment/test. Nested matches retain their exactly-once staged scrutinee and branch to joins. Callable invocation remains ordinary code unless its invocation itself is awaitable. Class construction and capture construction remain ordinary staged operations; capture environments are values stored in frame slots only when live across suspension.

Use conventional backward liveness on the generated finite CFG with an iterative deterministic worklist ordered by dense state number. A local/temporary is framed only when defined before a suspension and used after a reachable resume. Values used solely before suspension remain state-local. Slot allocation follows declaration/staging semantic order, not hash order. This is narrower and less wasteful than storing all locals, and less fragile than ad-hoc continuation capture. It preserves authored evaluation order by staging each potentially effectful expression once before the split. Frame fields must not expose a hidden lexical closure.

## 11. Result, exception, and cancellation law

Ordinary Copeland failures remain typed `Result` transitions. Await resume distinguishes: success payload; declared sidecar `err`; compiler-owned transport failure; cancellation; and terminal invariant failure. `?` transfers a declared `err` to its precomputed function-return or lexical-except target after resume exactly as before suspension. Result match and typed `try`/`except` continue to examine declared `Result` values only. They do not catch cancellation, peer closure, malformed protocol data, or invariant failure.

Cancellation is a distinct async outcome with an explicit future language/runtime decision about surface spelling and token/value capability. M1 must not inherit `OperationCanceledException` or Promise rejection as source law. A cancelled machine runs accepted cleanup only if a later `finally`/disposal law explicitly specifies it. Postfix `!` reaching an err is a terminal invariant panic, not a typed catchable Result branch.

## 12. Backend realization

For C#, initially emit an inspectable sealed frame carrier and switch-driven resume/`MoveNext`-style method, with explicit completion source/continuation wiring owned by Copeland. `Task` may schedule/combine host operations but compiler-generated C# `async`/`await` must not be the canonical lowering. Do not claim NativeAOT support until a publish proof exists; avoid reflection, dynamic, and runtime code generation.

For JavaScript, emit a provenance-checked frame carrier plus switch-driven resume function. Promise is scheduling only; resolved values are decoded into canonical resume inputs and rejected Promise values are mapped explicitly to transport/cancellation/invariant policy. Never use Promise rejection for ordinary declared `Result` errors. Diagnostic and Symbolic profiles allocate deterministic state/helper names under their existing profile-local naming rules; Release remains a future profile.

## 13. Sidecar integration

A sidecar call stages request data, allocates a nonzero request identity, encodes/sends, records the awaited-operation slot, and suspends. The dispatcher resumes only the matching live request. Success and declared `err` are schema-decoded values; peer close, timeout, cancellation, malformed response, incompatible contract, and resource breach take explicit non-Result outcomes. Duplicate/unknown response IDs and impossible generated protocol states are invariant failures. The sidecar plan stays a small awaitable operation plan, not universal RPC MIR.

## 14. Future iterator/generator integration

The same substrate can represent `yield` as a suspension with a yielded-value slot and a `next` resume input, plus complete/dispose terminals. It does not impose async function type, Promise behavior, or sidecar transport on iterators. Early disposal and `finally`-equivalent cleanup require a separate accepted lifecycle law before implementation. `for...of` protocol and generator syntax remain future scope.

## 15. Interpreter/evaluator implications

The automaton is the correct future Cope interpreter seam: a stepper can execute a state, expose a deterministic suspension record, inject a typed resume input, record state/edge provenance, and enforce work limits. It simplifies deterministic testing and debugging without requiring an interpreter now. It does not make arbitrary compile-time evaluation safe; static execution needs its own capability and bound policy.

## 16. Validation and resource law

Initial proposed defaults, subject to M1 corpus calibration: 256 states/function, 1,024 transitions/function and 16/state, 64 suspensions, 128 frame slots, 8,192 liveness/worklist steps, 64 structured-control nesting, 64 awaited operations, 256 future yields, and 128 generated helper/state-name characters. Validator checks entry existence, unique IDs, known targets, reachability (warning/error policy chosen by M1), nonterminal dead ends, terminal outcome shape, frame read/write/type correctness, resume type compatibility, await operation presence, lexical Result-handler target validity, no-progress cycles only as diagnosable generated-machine errors, and backend capability. A valid authored infinite loop is not malformed automata.

## 17. Infrastructure ownership and graduation

Keep the first implementation compiler-local: `Copeland.TS.Mir` may define backend-neutral automaton data and validation only when M1 proves it. Do not reference Dominatus, copy its runtime, or create a BCL project yet. Historical similarity is not a compatible contract. Extraction requires a second live consumer with the same closed state/frame/outcome model and a BCL-only dependency direction that excludes frontend syntax, C#/JavaScript backends, Machina, Aurelian, UI, reflection/dynamic, and scheduler policy.

## 18. Explicit exclusions

M0a implements none of async/await syntax, yield/generators, automata runtime, MIR nodes, sidecar transport, scheduler, timers, cancellation tokens, browser/WebView integration, project/package references, interpreter execution, or production extraction.

## 19. Recommended one-shot implementation milestone

Use one coherent **CTS-ASYNC-M1** stride rather than a standalone AUTOMATA-M1 ceremony: shared validated suspension lowering, synthetic/manual MIR proofs, async syntax/binding, C# and JavaScript explicit realization, and typed Result/cancellation law. Follow with **CTS-SIDECAR-M1** using that proven awaitable path, then **CTS-ITER-M1** for iterator protocol and separately approved generator/disposal semantics. CTS-AUTOMATA-M0a is the design gate and routing authority.
