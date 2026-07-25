# CTS-ASYNC-M1 typed async/await and automata migration record

**Status:** in progress; not a milestone-closeout record.

The first implementation increment adds the compiler-local MIR contract required for a single shared lowering: `MirAsyncType`, `MirAsyncCallableType`, `MirSuspensionAutomaton`, deterministic explicit state/frame identities, typed suspension/resume/cancellation/completion transitions, bounded resource defaults, and shared malformed-automaton validation. It also adds initial named-function frontend support: `async function`, `Async<T>`, and typed `await`; `await operation?` parses as `(await operation)?`.

The validator is intentionally backend-neutral. It prevents either backend from receiving an automaton with an unknown edge, duplicate identity, invalid entry, non-Async await operand, incompatible resume/completion type, invalid cancellation target, dead nonterminal state, unreachable generated state, or a breached state/transition/suspension/slot/worklist bound. Focused tests prove a valid machine and representative malformed cases.

This increment does not claim source async support or runtime behavior. In particular it does not emit C# `async`, JavaScript `async function`, generators, Promise/Task source interop, sidecar transport, or a user cancellation API. The remaining work is still the accepted CTS-ASYNC-M1 implementation path, not another design milestone.
