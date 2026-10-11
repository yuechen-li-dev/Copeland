# Copeland TS and Visual TS: case arms and explicit execution flows

Status: implementation milestone, 2026-10-10. Core authoring and execution are
implemented; the complete research acceptance list below is not yet qualified.

The follow-up [Visual TS static loop milestone](visual-ts-static-loops.md) adds
finite shader schedule expansion and closed payload/FLOW evaluation before DXC.
The closed nested replay emits a literal result. The subsequent
[static GPU FLOW composition](visual-ts-flow-tape.md) adds `run` and resolves
the former dynamic nested compile-time blocker with a flat state graph.

## Implemented surface and evidence

Runtime and static `match`/`switch` accept optional `case`. Old and new runtime
spellings emit byte-identical C#/JavaScript for the payload specimen; static
template artifacts have identical paths and hashes. A variant named `case`
continues to work. Existing event FLOW and its frozen semantic hash are preserved.

Execution FLOW supports typed immutable parameters, explicitly initialized
board fields, named states, `transition`, bare `yield`, `finish`, `fail`, and
bounded host child `push` and statically composed GPU `run` with typed
success/error entry payloads. CPU and GPU share
an AST projection into ordinary typed functions, records, and payload enums.
The ordinary binder continues to own types/effects. Cope FLOW MIR retains the
frame interface, state bodies, control edges, and generated function bodies;
its semantic hash changes when an execution body changes. MIR validation checks
the frame interface and control references. No scheduler or interpreter was added.

The concrete value API is:

```typescript
// Copeland host profile: int and mutable let.
flow Countdown(limit: int) -> int {
    board { remaining: int = limit; }
    state Counting initial {
        if (board.remaining == 0) { finish limit; }
        board.remaining = board.remaining - 1;
        yield;
    }
}
function tick(frame: CountdownFrame): CountdownFrame {
    return Countdown.step(frame);
}
```

Use `Countdown.start(3)` and assign `frame = Countdown.step(frame)` explicitly.
The emitted C# facade offers `Countdown.Start`/`Countdown.Step`; JavaScript uses
`start`/`step`. `CountdownFrame`, `CountdownBoard`, `CountdownState`, and
`CountdownOutcome` are ordinary generated types. `flow Done()` explicitly opts
a parameter-free, terminal-only declaration into execution semantics; old
reaction declarations without parentheses retain their existing semantics.

A simple flow exposes `Active`, `Yielded`, `Completed(value)` and optional
`Failed(error)` outcomes. A flow with children exposes `Active`, `Completed`,
optional `Failed`, and `Fault(code)`, plus a separate `StepKind` enum. Fault codes
are 1 for overflow, 2 for invalid active depth/empty slot, and 3 for invalid
continuation entry. A completed/faulted frame is idempotent under further steps.
Each step advances one active state body; entering even a terminal execution
state runs that body on the next step. Reaction-only terminal normalization is
unchanged.

Host child graphs are closed over statically named flows in the same source module,
including recursive calls; default capacity is 8, explicit capacities are 1–64,
and at most 16 different flow declarations inhabit a root stack. Capacity is
checked before evaluating child arguments. Every activation owns its board,
parameters and retained continuation payload. Older value-only frames remain
independent snapshots; C#/JS records/sums still use their existing allocation
representations. This is not a no-allocation host stack or a guarantee that
arbitrary mutable references/host objects become deep snapshots.

Generated compute frames admit certified finite records, sums and fixed arrays.
Nested aggregate payloads now have explicit layouts, typed zero construction,
cycle/size diagnostics and dependency-ordered HLSL emission. GPU resources are
excluded from frame storage. Existing CPU numeric/mutability policy remains
distinct from Visual TS `u32`/`f32`, immutable `let`, and mutable `var`. GPU payload
match retains its existing single payload-record binding, whereas CPU match
retains positional payload bindings. Compute owns this increment; graphics FLOW
gets an explicit profile diagnostic. No GPU `goto`, loop capability, submission
or barrier is inferred from a state change.

C#/Node execute countdown, transition, two nested children, yielding
continuations, typed failure, void child, overflow, invalid depth, and completed
frame idempotence specimens. Countdown also runs under a custom C# module name.
Shader tests qualify countdown and a typed child through DXC/SPIR-V validation.
`dotnet run --project tools/Aurelian.GraphicsProof -- --flow-proof` submits real
Vulkan work: yielding child completion, typed failure, active incomplete work,
two-level nested completion, record delivery, re-entry and local replay produced
exact expected results twice on an RTX 3070. Recursive overflow remains a host
test; GPU recursion now requires the explicit state-transition repair.
Current evidence: `artifacts/local/flow-tape/flow-evidence.json` plus source-derived
VD-MIR/HLSL/SPIR-V artifacts. These frames are invocation-local; this does not
qualify serialized frame restore or persistent typed buffers across dispatches.

Cloth now has named `ClothPhase3D` host dispatches and a typed shader phase decoded
once from the existing scalar header. Exhaustive `case` arms select phase facts
and contact payloads. Unknown phase values are rejected by the host header and
ignored by the shader. Numerical loops and Vulkan barriers retain their owners.
SurfaceLighting's existing payload/word matches also use the preferred prefix.

## Concrete remaining boundary

The earlier wider nested GPU experiment stopped at DXC's existing 15-second budget.
`--flow-proof --flow-nested-experiment --output artifacts/local/vts-flow-nested`
isolates the three-variant, three-slot value frame in `FlowReplayProof.v.ts`;
DXC reports `DXC subprocess timed out after 15 seconds` before native submission.
The ten-step unrolled nested specimen also exceeded that budget. Static `run`
composition now removes that activation carrier and both specimens compile and
execute through the real Vulkan path. The timeout was not enlarged and debug
optimization was not substituted for production. See the flat composition
milestone for current source, storage laws and native timings.

Reaction/execution sibling adapters are diagnosed with `COPE-FLOW-EXEC-0001`.
Cross-module child pushes, automatic Dominatus trace/checkpoint adapters,
definition/shape-checked restore, invocation resource forwarding, typed-buffer
frame round trips, and an asynchronous cloth submit/wait controller remain
unimplemented. Existing scalar compute storage admits only `StorageBuffer<f32>`;
the language cannot currently persist a frame containing typed sums through
that resource ABI. These are remaining acceptance work, not features implied
by passing local-value shaders. This milestone is meaningful progression toward
the complete design below, not completion of every proposed acceptance gate.

Validation commands (Windows .NET 10, Naga already available in the repository):

```powershell
dotnet test Copeland.TS.slnx -m:1
$env:AURELIAN_NAGA = (Resolve-Path artifacts/aurelian-beacon3d/toolchain/bin/naga.exe).Path
dotnet test Aurelian.slnx -m:1
dotnet run --project tools/Aurelian.GraphicsProof -- --flow-proof
dotnet run --project tools/Aurelian.GraphicsProof -c Release -- --cloth-foundation --output artifacts/local/vts-flow-cloth-native
```

The Copeland TS solution passed 1,946 tests. The final full Aurelian solution
passed 1,140 tests across 30 test projects, including 213 shader and 14 cloth
tests. The first broader Aurelian run hit the same compile-budget
boundary in an oversized five-step, three-slot shader test. The test now checks
two explicit steps with the necessary two slots; the separate native specimen
retains five-step completion/failure qualification. No compiler timeout changed.
Native cloth passed 180 ticks on each 9/17/33 grid, contact/stretch/pin budgets,
early CPU/GPU tolerance, triangle interior contact, and exact same-device rewind.
Its early agreement is a tolerance check; long-horizon CPU/GPU bit identity and
GPU speedup are not claimed. Native logs/artifacts live under `artifacts/local/`.

## Original approved design

The following sections preserve the original plan. The implementation inventory
and explicit differences above describe current behavior. In particular, the
original GPU stack proposal below is superseded by statically composed `run`;
bounded `push` remains the host policy.

## Decision

Keep the existing Copeland `match` and `flow` constructs. Add optional `case`
arm prefixes, then extend FLOW with ordinary execution-state bodies, named
transitions, explicit yield, and typed child continuations. Share the language
semantics between Copeland TS and Visual TS; keep scheduling and GPU submission
in their existing runtime owners.

## Source audit before implementation

- `src/Copeland/Copeland.TS/Syntax/Parser.cs` already parses `match` and `switch`
  as the same expression shape. Arms currently begin directly with a variant;
  neither runtime nor static match has the proposed optional prefix.
- `FlowM1Tests`, `FlowM2Tests`, and the C#/JavaScript backend runtime tests cover
  real Copeland flows: one initial state, persistent board, declared events,
  guarded event transitions, board updates, and typed `finish`/`fail`.
  Board updates may call effect-qualified pure helpers; guards retain the
  separate M1 restriction. Execution bodies and child flow calls are the gap.
- Visual TS uses the Copeland parser and GPU binders. There is no admitted FLOW
  execution path there. The older `SdslvFlowDecl` AST is not an implementation:
  the legacy parser and VD-MIR M0 lowerer explicitly reject flow declarations.
- Concept's current reference is `docs/spec/CONCEPT_EVT1_LANGUAGE_SPEC.md`,
  `language/evt1/automata`, and `language/evt1/machine-stack`, together with
  `libraries/DragonGod/Machine/Execution.concept`, `Trace/Core.concept`, and
  `Replay/Checkpoint.concept`. The language owns execution and storage laws;
  DragonGod owns explicit policy, trace, checkpoints, and scheduling adapters.
- Cloth currently has a numeric dispatch selector (`ClothDispatch3D.Phase` and
  `Header[0]`), plus nested host numerical loops. It does not have a hand-written
  child-machine stack. The selector needs a typed enum; a future asynchronous
  submit/wait lifecycle is a suitable FLOW motivating case. These are separate
  changes, and replacing the numerical loops is not required.

## Optional case prefixes

Preferred authoring:

```typescript
return match contact {
    case ContactShape.None => position,
    case ContactShape.Plane(nx, ny, nz, offset) =>
        ProjectPlane(position, nx, ny, nz, offset),
    case ContactShape.Sphere(cx, cy, cz, radius) =>
        ProjectSphere(position, cx, cy, cz, radius),
};
```

`case Pattern => expression` and the existing `Pattern => expression` have
identical meaning. Qualified and inferred variant names remain accepted.
`switch` remains an exact alias of exhaustive `match`, including payload
binding, result typing, exactly-once subject evaluation, and missing/duplicate
arm diagnostics. A parenthesized subject uses the ordinary expression grammar.
Apply the prefix consistently to static match as well.

Do not introduce `case X:`, fallthrough, or a requirement to write `break`.
This first increment does not add new wildcard/default arms, pattern guards,
literal patterns, or statementful runtime arms; existing static patterns retain
their current laws. Preserve source tokens/spans
for formatting and diagnostics; normalize both spellings to the same bound/MIR
operation. Existing sources continue to compile unchanged.

## Existing reaction states remain unchanged

```typescript
flow Door {
    board { opens: u32 = 0; }
    event Open();

    state Closed initial {
        on Open() -> Opened {
            board.opens = board.opens + 1;
        };
    }

    state Opened { }
}
```

Keep existing event declarations, guards, ambiguity rules, update order,
revision behavior, terminal normalization, and generated `start`/`sendOpen`
entry points. A reaction state consists of `on` rules; an execution state
consists of ordinary statements. Reject mixing both forms in the same state.
An empty state stays a passive reaction state. Sibling states may use different
forms, allowing an event transition to enter an explicitly stepped workflow.

## Execution states

Original design specimen (the implemented Visual TS compute profile admits it;
the CPU profile uses `int`):

```typescript
flow Countdown(limit: u32) -> u32 {
    board { remaining: u32 = limit; }

    state Counting initial {
        if (board.remaining == 0) {
            finish limit;
        }

        board.remaining = board.remaining - 1;
        yield;
    }
}
```

Construction evaluates arguments once and initializes owned persistent values;
it executes no state body. `Countdown.start(3)` produces an explicitly owned
instance. `instance.step()` advances the active frame once. Host-side adapter
allocation must be documented; the semantic frame has a fixed layout and does
not require a callback registry or reflection.

Parameters are immutable frame fields. `board` fields are explicit mutable
frame storage. Ordinary `let`/`const` and `var` bindings retain the target
language's existing mutability rules and are transient to one step. Nothing
becomes persistent because it happens to be live near a yield.

| Control | Meaning |
|---|---|
| `transition Next;` | Update the current frame's named state and end this step. |
| `yield;` | Keep the state and board, end this step, and re-enter from the state's beginning next time. |
| `finish value;` / `finish;` | Complete the frame with its declared result. |
| `fail error;` | Complete the frame with its declared domain error. |
| `push Child(args) -> Resume ! Failed;` | Suspend the parent at named continuations and activate an initialized child frame. |

Controls are terminal on their path: reject following unreachable statements
and execution paths that fall through without a control. Ordinary `return`
inside a flow state is diagnosed with the `finish` repair. A transition does
not execute an arbitrary destination body in the same step. Preserve the
existing eager normalization of a pure terminal-only destination; this may
publish completion, but cannot start another ordinary execution body.

Re-entering a yielded state repeats statements above `yield`; authors must
store progress in the board or transition to a different state for one-time
work. Language-level yield is distinct from both JS generators and existing
compiler-derived async/await suspension. Do not change the async frame model.

Execution bodies use the ordinary target's expression, statement, and effect
checking. This does not relax the existing pure reaction-update/guard law.
Host functions can execute effects only where the ordinary host profile admits
them. In an Aurelian agent adapter, world changes still travel through the
existing typed intents/actuation authority rather than arbitrary callbacks.

## Typed child continuation

Proposed cloth controller fragment; referenced domain types/child flow are
illustrative and are not implemented by this document:

```typescript
flow ClothSolve(options: SolveOptions) -> SolveReport ! SolveError {
    stack 8;

    state Start initial {
        push SolveConstraints(options) -> Accept ! Reject;
    }

    state Accept(report: SolveReport) {
        finish report;
    }

    state Reject(error: SolveError) {
        fail error;
    }
}
```

`stack N;` is a compile-time positive capacity for the entire root instance's
active frame stack, including the root; default capacity is eight. CPU adapters
allocate the fixed storage once. GPU specializations require a static capacity.
Closed child call graphs generate a tagged union of concrete frame layouts.
Each pushed activation has independent parameters and board, including repeated
or recursive activations. Overflow is an explicit execution fault and never
causes a resize or partially committed push.

The success destination declares one parameter of exactly the child's result
type, or no parameter for `void`. The failure destination declares one parameter
of exactly the child's error type. These are explicit state-entry payload fields,
retained across yield in that continuation state. Require a failure destination
for a fallible child; allow no failure destination for an infallible child.
Reject ordinary event/transition entries that cannot initialize required state
payloads. General parameterized `transition` is deferred.

Only the top frame advances. On child completion, remove its frame and publish
the selected payload into the named parent continuation. Its ordinary body runs
on the next explicit step, subject to terminal-only normalization above. The
parent does not poll an untyped last-child result or read a result before it
exists. A root completion remains observable after completion; stepping it again
returns an explicit already-completed status. No implicit error conversion,
event forwarding, nested scheduler, or arbitrary instruction-label jump exists.

The initial surface uses `finish`/`fail` rather than introducing Concept's separate
neutral `pop` outcome. Root/child completion and step status are distinct types.
Cancellation and external event routing to a suspended parent need a separate
contract and are not silently inferred in this increment.

## Visual TS profile

Use `transition` on both targets. Do not add `goto` to Visual TS or a general
label mechanism to Copeland TS. Source cycles are permitted because each step
returns; they do not create an unbounded shader loop.

A generated GPU step takes an explicit typed frame and produces its updated
frame plus step outcome. Initially support compute; graphics support waits for
a motivating case. Each invocation advances its owned machine once. Persistent
frames across dispatches require explicit buffer load/store by the caller.
Opaque resources cannot be persistent board/parameter/payload fields; any
resource capabilities needed while stepping must be explicit invocation inputs
validated by the existing resource/effect rules.

Frames contain only admitted fixed-layout GPU values and statically bounded
storage. No hidden heap, closures, function-pointer dispatch, or inferred
workgroup-shared ownership. A source state transition does not imply a Vulkan
dispatch, storage barrier, workgroup barrier, or queue submission. In particular,
cross-color cloth work must remain separate ordered dispatches. Numerical state
updates cannot use language transitions to evade GPU synchronization rules.

Do not broaden compute loop support as a side effect of FLOW. Existing bounded
graphics loops and deferred compute loops retain their current profile gates.
An optional fixed-budget multi-step driver can be considered later; the first
GPU increment is one explicitly called step and no run-until-complete helper.

## Ownership, inspection, and replay

The parser/binder owns legality, state identities, entry payload types, frame
layouts, and the control graph. Extend the existing Cope FLOW MIR with reaction
and execution state kinds and explicit transition/yield/push/completion nodes.
Use those validated semantics for C#/JS emission and GPU projection to VD-MIR.
Do not teach a second legacy shader parser the new grammar or make emitters
reconstruct control semantics from source strings.

The snapshot contains the definition identity, active depth, frame tags, named
states, parameters, boards, continuation destinations, and pending entry
payloads. Validate identity and shape before restore. Resource handles and
external effects need existing adapter ownership and cannot be reproduced by
restoring frame bytes alone.

Expose transition facts with stable source/state identity and typed causes:
step, transition, yield, push, completion, failure, and execution fault.
Adapters feed the existing Dominatus trace/checkpoint owner. GPU observability
requires explicitly requested outcome/trace readback; it is not free CPU-visible
rewind, and floating-point or external-effect replay needs its own qualification.
Language stepping does not create another scheduler or persistence service.

## Implementation order and acceptance

1. Add optional `case` to runtime/static arms through the shared parser. Prove
   old/new spellings have identical semantic artifacts, payload behavior, static
   evaluation, and validated SPIR-V. Include `match` and `switch` aliases.
2. Add execution states, `transition`, and `yield` to existing Cope FLOW MIR and
   C#/JS backends. Exercise an actual countdown over repeated steps, immutable
   local diagnostics, board persistence, no transient escape, exact snapshot
   replay, and every pre-existing FLOW regression.
3. Add typed child continuations and bounded frames. Exercise two nested calls,
   independent boards, a yielding child, success/failure delivery, recursive
   overflow without partial push, malformed snapshot rejection, and result
   visibility only after completion.
4. Project the same semantics to Visual TS compute. Qualify CPU/GPU agreement
   on integer-only specimens, compiler/DXC/SPIR-V validation, fixed-layout frame
   round trips, actual Vulkan stepping, and exact same-device rewind. Reject
   opaque captures and unsupported effects with one actionable diagnostic.
5. Replace cloth numeric phase tags with a typed dispatch enum and case arms.
   Use execution FLOW for a genuine retained submit/wait controller once the
   existing synchronous Vulkan adapter admits that lifecycle. Preserve current
   numerical ordering, barrier ownership, contact tests, and measured behavior.

The first implementation should not add hierarchy fallback, enter/exit hooks,
general async scheduling, cancellation, or shared outer automata boards. Scored
transition selection is a follow-on that should reuse Concept's deterministic
argmax law and existing Dominatus/GPU utility seams rather than create a new
policy runtime. If added, guards and scores evaluate once in source order, ties
choose the first maximum, and invalid/no-enabled scores have explicit outcomes.

This design pass is complete when the source audit, syntax, storage/control
laws, compatibility boundary, GPU boundary, and executable acceptance specimens
are reviewable. No build/test or native qualification is claimed for proposed
syntax until implementation.

Current audit validation: rebuilt and ran the existing FLOW M1/M2 frontend tests
(14 passed), C# FLOW runtime tests (2 passed), and JavaScript/Node FLOW runtime
tests (2 passed). These 18 passing tests verify the existing substrate only.
Those 18 checks were the design pass baseline. Current implementation evidence
and boundaries are recorded at the beginning of this document.
