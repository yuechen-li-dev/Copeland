# COPELAND-FLOW-TSX-M0 — optional TSX authoring for `flow`

## Outcome

Outcome A: TSX is a useful optional authoring surface over the existing `flow`
semantic feature. Native `flow` remains canonical and unchanged as the low-level
language reference.

Copeland does not seek full JavaScript compatibility. It seeks TypeScript
familiarity with deliberate language improvements. If a developer knows
traditional TypeScript, new Copeland constructs should mostly feel like features
TypeScript should have had.

TSX is used here because it makes the state graph structurally visible. This is
not React compatibility and it does not hide a state machine behind lifecycle
behavior. It exposes states, event patterns, guards, transitions, updates, and
terminal outcomes directly.

## Architecture audit

Before M0, the compiler already had the required single semantic path:

1. `FlowDeclarationSyntax` owns the native source shape: board fields, local
   event declarations, states, transitions, and terminal outcomes.
2. `Binder.BindFlow` creates `BoundFlowDefinition`, the syntax-free semantic IR.
   It owns the board record type, typed event payloads, initial-state law,
   target resolution, guards, payload bindings, board updates, and terminal
   result/failure checks.
3. `MirLowerer.LowerFlow` creates `MirFlowDefinition`, the backend-neutral event
   automaton consumed by both C# and JavaScript backends.
4. Both backends emit the same existing durable session model. M0 also closes an
   existing observation gap: completed transition results now expose the typed
   `finish` value as `Value`/`value`, alongside the existing failure error.

The TS-XML parser already recognizes nested/self-closing elements, static string
attributes, braced ordinary expressions, fragments, exact source positions, and
`.tsx` gating. It produces neutral `TsXmlElementExpressionSyntax` nodes; those
nodes have no intrinsic component or UI meaning.

Payload enums already use `EnumDeclarationSyntax`, `EnumCaseSyntax`, and typed
`EnumPayloadFieldSyntax`. Match arms already establish the language mental model
of a variant plus positional payload bindings. Native `flow` uses an equivalent
event-plus-binding shape, although its M1 parser has a smaller dedicated syntax
node rather than sharing `MatchPatternSyntax` directly.

M0 reuses these parts as follows:

```text
.tsx parser
  -> neutral TS-XML AST plus ordinary expression AST
  -> FlowAuthoring profile recognizes one exported Flow root
  -> compiler lowers it to existing FlowDeclarationSyntax
  -> existing BindFlow
  -> existing BoundFlowDefinition
  -> existing MirFlowDefinition
  -> existing C# / JavaScript flow runtime
```

There is no TSX-flow IR and no second binder or runtime.

## Syntax candidates

### Chosen: exported static semantic root plus explicit payload enum

```tsx
enum DoorEvent {
    Open(key: string),
    Cancel,
}

export default (
    <Flow
        name="Door"
        events={DoorEvent}
        result="int"
        failure="string"
        board={{ attempts: 0 }}
    >

        <State name="Closed" initial>
            {Open(key) when key.length > 0 => Opened {
                board.attempts = board.attempts + 1;
            }}
            {Cancel => Cancelled}
        </State>

        <State name="Opened">
            <Finish value={board.attempts} />
        </State>

        <State name="Cancelled">
            <Fail error="cancelled" />
        </State>
    </Flow>
);
```

The event enum is ordinary Copeland payload-enum syntax. Each braced State child
is a match-shaped transition arm: payload pattern, optional `when` guard, `=>`
target, and optional ordinary statement block. `Open(key)` is a typed sum-case
pattern, not a callback or record destructure. `Cancel` is the zero-payload
pattern. The update block lowers to the same restricted transactional board
updates as a native transition body.

### Rejected: component-shaped function returning `Flow<Result, Error>`

A `function Delivery(): Flow<int, string>` wrapper resembles a React component,
but adds a fake call boundary, suggests runtime construction, and requires a
special nominal return type that has no runtime value. The exported static root
states the compile-time law more honestly and with less ceremony.

### Rejected: decomposed `<On event=... when=... to=...>`

The first implementation made the relationship obvious: those three props were
merely a verbose encoding of one payload-enum match arm, and `to` made the target
stringly. The accepted convention is the direct arm
`{Start(amount) when amount > 0 => Staging { ... }}`. The parser reuses
`MatchPatternSyntax`, the target is an identifier, and the existing flow binder
assigns enum payload types to positional bindings.

### Rejected: `<Board><Field ... /></Board>`

Field elements repeated object-property structure without adding semantic
information. The accepted `board={{ attempts: 0, ... }}` spelling is an ordinary
object literal. The existing binder infers each fixed field type from its
initializer, then creates the same machine-owned board record used by native
syntax. Developers who need explicit type control can use an already typed
ordinary expression as the initializer; M0 does not add a TSX field-type system.

### Rejected: record-per-event declarations

Record-per-event modeling contradicts the language law. A flow event set is an
algebraic sum eliminated by state-dependent pattern matching.

### Rejected: callback props for guards and effects

Spellings such as `guard={() => ...}` and `effect={() => ...}` imply runtime
closures and React-style APIs. Guards remain ordinary Copeland expressions and
updates remain ordinary assignments.

### Rejected: JSX ceremony for every expression

Separate `<Guard>`, `<Set>`, `<Add>`, or lifecycle-like elements make simple
machine logic more verbose and create a second expression language. M0 keeps
expressions and updates in Copeland TypeScript.

## Native equivalence

The chosen example maps directly to:

```ts
flow Door -> int ! string {
    board {
        attempts: int = 0;
    }

    event Open(key: string);
    event Cancel();

    state Closed initial {
        on Open(key) when key.length > 0 -> Opened {
            board.attempts = board.attempts + 1;
        };
        on Cancel() -> Cancelled;
    }

    state Opened {
        finish board.attempts;
    }

    state Cancelled {
        fail "cancelled";
    }
}
```

The maintained `Delivery.flow.ts` and `Delivery.flow.tsx` fixtures cover four
board fields, seven payload-enum variants, eight states, guards, pure helper
calls, self-transitions, normal transitions, reset, finish, and failure.
`MirFlowSemanticHash` hashes normalized FLOW MIR with all source identity already
removed. The two Delivery fixtures have exact hash equality:
`1e0515b8d0d02c7ce1645a7841443690586a41c6dee3f181d9d05a1a81730fd8`.

## Semantic laws

- Board: `board={{ ... }}` is an ordinary object literal. Static identifier keys
  and their initializer expressions become the same fixed board record fields,
  inferred types, and defaults as native syntax. FLOW-M1 requires a fixed board,
  so a machine with no fields writes `board={{}}`. Only `board.field = expression`
  is legal in an arm update block.
- Events: `events={DeliveryEvent}` must name a module-local payload enum. Cases
  become the flow's event sum in source order. No nominal record events exist.
- Patterns: `{Start(amount) => Target}` and `{Cancel => Target}` become the same variant
  name and positional bindings used by native transitions. Unknown variants and
  wrong arity use native flow diagnostics.
- Guards: `when expression` contains an ordinary Copeland expression. It sees typed
  event bindings and board reads and must be boolean and FLOW-pure.
- Effects: an arm's optional block is an ordinary Copeland statement block
  lowered into the existing transition body. The native binder enforces explicit board-field
  assignment, typing, sequential snapshot updates, and effect-qualified pure
  helper calls.
- Finish/fail: `<Finish value={...} />` and `<Fail error={...} />` lower to the
  existing terminal node. The attributes may be omitted for `void` completion
  or the native default failure rule. Result and failure types are the static
  `result="..."` and `failure="..."` Copeland type attributes.
- State symbols: declarations use readable static string literals; transition
  targets are identifiers after `=>`. Both become compile-time symbols. Dynamic
  declarations are rejected; duplicates, missing or multiple initial states,
  and unknown targets use native validation.
- Terminal law: terminal states may not declare outgoing transitions in either
  syntax. `COPE-FLOW-0031` makes the previously implicit runtime terminal law
  explicit.

The string tradeoff is deliberate. JSX tag names cannot naturally declare an
open set of state identifiers, and `{States.Idle}` would require an otherwise
useless state enum. Static string literals are shorter and more recognizable;
the compiler interns and resolves them before bound semantics exist. Events do
not make that tradeoff because symbolic payload patterns are both readable and
strictly stronger.

## Profile and file convention

Use a `.flow.tsx` filename by convention and enable the `FlowAuthoring` Copeland
project type. `.tsx` enables parsing only. The project type grants semantic
authority; an exported `<Flow>` root without it is `COPE-FLOW-TSX-0001`.
`FlowAuthoring` is carried through workspace, project-context, MSBuild, and
legacy compilation-option boundaries. A TSX file has one default exported flow;
native syntax remains appropriate for multiple flows in one module.

## Diagnostics

Structure diagnostics use `COPE-FLOW-TSX-*` and retain TSX token spans. They
cover a missing required attribute, dynamic/non-constant symbol, unknown event
enum, unsupported element/attribute/child, illegal nesting, non-self-closing
declarations, duplicate board/terminal, invalid event pattern, invalid static
type, and duplicate attributes.

After syntax lowering, native diagnostics cover duplicate states, initial-state
law, unknown event variants, payload arity and types, guard boolean/purity,
unknown targets, board assignment fields/types, finish/fail types, and terminal
outgoing transitions. This is intentional evidence that TSX does not own a
parallel semantic validator.

## Runtime and dependency audit

The compiler does not bind the Flow root as a general TS-XML expression. It
never resolves `Flow` or `State` as functions or components. Generated
JavaScript contains no React, JSX runtime, `createElement`, component call, or
TSX object-tree allocation. Generated C# likewise receives only normalized FLOW
MIR. No package dependency was added and the existing flow session is the only
runtime.

## Ergonomics and future fit

For a two-state flow, payload transition, guard, mutation, success, and failure,
TSX makes nesting and terminal states immediately scannable while the ordinary
board object and match-style arms avoid element-per-field/transition ceremony.
The native syntax remains compact and remains the
documentation authority. The TSX form earns its place for developers and LLMs
that already read JSX trees, while payload patterns and assignment expressions
avoid learning a component API.

An isolated model given only traditional TypeScript knowledge and the short M0
law correctly inferred the Flow/State hierarchy and persistent typed board,
presence-only initial marker, boolean guards, event payload bindings, explicit
async completion events, and terminal Finish/Fail meaning. It produced traffic
light, login, and retry shapes without introducing hooks or lifecycle behavior.
The experiment also exposed the remaining ceremony in the preliminary
`<On event=... when=... to=...>` spelling: it understood the semantics but had
to reconstruct match arms from props. That evidence led to the final arrow-arm
convention. Its other errors were informative: it invented a `payload enum`
keyword instead of Copeland's ordinary payload-bearing `enum`, used multiple
guarded handlers for the same event even though FLOW-M1 rejects that ambiguity,
and updated one undeclared board field. The remaining mistakes are semantic laws that
the compiler diagnoses, not evidence for more JSX machinery; examples must show
the exact enum syntax and one-transition-per-event rule.

The shape can later host a separate compiler-owned `<Profile>` semantic root
whose children represent semantic geometry operations. Nothing in M0 generalizes
FLOW around geometry, shares runtime state, or treats `<Profile>` as a UI
component. Machina UI is unchanged.

## M0 manifest

```json
{
  "milestone": "COPELAND-FLOW-TSX-M0",
  "kind": "optional-tsx-authoring-for-flow",
  "nativeFlowStillCanonical": true,
  "tsxOptional": true,
  "sameFlowSemanticIr": true,
  "eventsRemainPayloadEnums": true,
  "reactDependencyAdded": false,
  "jsxRuntimeAdded": false,
  "secondFlowRuntimeAdded": false,
  "nativeFlowSyntaxChanged": false,
  "geometryProfileAdded": false
}
```

## Qualification

The focused M0 suite proves parsing, inferred board initialization, payload binding,
guards, self/normal transitions, initial/finish/fail/result/failure typing,
symbol resolution, normalized semantic hash equality, five-path JavaScript
execution parity, source spans, profile gating, and absence of React/runtime
TSX output.

Final local validation:

- `FlowTsxM0Tests`: 22 passed.
- Focused flow/TS-XML/match/enum compiler tests: 166 passed.
- Focused C# and JavaScript flow-runtime tests: 2 passed in each backend.
- `dotnet test Copeland.TS.slnx -m:1`: 1,625 passed.
- `dotnet test JointTaskForce.slnx -m:1`: 3,325 passed.
- `git diff --check`: passed; only line-ending conversion warnings were
  reported.
