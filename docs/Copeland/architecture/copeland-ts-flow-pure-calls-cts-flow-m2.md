# CTS-FLOW-M2 effect-qualified pure calls

## Decision

CTS-FLOW-M2 closes the FLOW-M1 composition gap without adding syntax, a new
effect system, or a FLOW-specific call node. A transition board update may
contain an ordinary `BoundCallExpression` when its statically resolved
`FunctionEffectSummary` is `StaticSafe`, has no `SafeEffects`, and every argument
expression independently satisfies the existing FLOW update expression law.

FLOW-M1 used a structural `IsFlowPure` predicate. It accepted literals,
variables, unary/binary computation, and board-field reads, but rejected every
call before MIR lowering. `COPE-FLOW-0024` was therefore reported even for a
compiler-resolved helper such as `nextSequence`.

## Existing effect model

The compiler already classifies ordinary bound functions by fixed point.
`FunctionStaticSafety` is `StaticSafe` or `RuntimeOnly`. Runtime effects are
`ReadsRuntimeState`, `WritesRuntimeState`, `IO`, `HostInterop`, `Suspension`, and
`UnknownCall`; `LocalMutation` is retained in `SafeEffects` because it is safe
for static evaluation but is not pure enough for a FLOW update helper.

M2 makes safe-effect propagation transitive in the general classifier. Thus a
wrapper around a mutable-array kernel retains `LocalMutation`, while nested pure
helpers remain `StaticSafe` with an empty safe-effect set. Recursive components
still converge through the classifier's existing fixed-point law.

The compiler rule is:

```text
FLOW_UPDATE_SAFE(call) iff
    the target is a statically resolved ordinary Copeland function
    and its FunctionEffectSummary is StaticSafe
    and its transitive SafeEffects set is empty
    and every argument is FLOW_UPDATE_SAFE
```

Missing summaries and indirect invocation fail closed. CLR and JavaScript host
interop, npm I/O, suspension, runtime state reads/writes, local mutation, and
unknown calls remain rejected. No name allowlist or purity annotation exists.
Imported source calls remain fail-closed at the current per-module binding
boundary; project-wide classification still runs later and M2 does not move
FLOW diagnostics into project orchestration.

## Semantics and lowering

Function calls retain the ordinary bound and MIR call nodes. Argument order and
exactly-once evaluation therefore follow ordinary call lowering: arguments are
lowered and emitted once in source order. FLOW still evaluates updates in
declaration order and publishes each intermediate `nextBoard` as the board seen
by the following update. Calls do not change guard, event, state, terminal,
revision, or reentrancy semantics. Guards keep their separate FLOW-M1 no-call
rule.

Generic bodies are now available before FLOW declarations are bound, allowing
the existing closed-specialization path to work without a generic FLOW rule.
Associated functions are ordinary resolved functions and qualify by the same
summary. Callable values and captures remain indirect invocation and therefore
`UnknownCall` in this milestone.

MIR is unchanged. The JavaScript FLOW expression emitter gained direct
`MirCallExpression` and numeric-conversion emission. The C# FLOW path reuses its
ordinary expression emitter and qualifies calls through `CopelandModule`, where
ordinary generated functions live. These are backend wiring changes, not new
runtime capabilities.

## Diagnostic

`COPE-FLOW-0024` points at the rejected update expression call and reports the
resolved call name and effect. Unknown or missing classification says purity
cannot be proven. One update produces one focused effect diagnostic.

## Effect matrix

| Call kind | Existing classification | FLOW update |
|---|---|---|
| local or nested pure function | `StaticSafe`, no safe effects | allowed |
| closed generic specialization | `StaticSafe`, no safe effects | allowed |
| pure associated function | `StaticSafe`, no safe effects | allowed |
| local/mutable-array mutation | `StaticSafe` + `LocalMutation` | rejected |
| CLR or JavaScript host | `RuntimeOnly` + `HostInterop` | rejected |
| npm transport | `RuntimeOnly` + `IO` | rejected |
| async/await | `RuntimeOnly` + `Suspension` | rejected |
| indirect or unclassified call | `RuntimeOnly` + `UnknownCall` | rejected |

## Non-goals

M2 does not change parser syntax, types, guards, terminals, callable-value
execution, imports, tables, async FLOW, termination, template forwarding, or
FLOW board semantics. It adds no effect annotation, inlining requirement,
host-purity heuristic, or special MIR.
