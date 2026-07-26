# CTS-FLOW-M1 — explicit typed flow/state automata

`flow` is Copeland TS's synchronous, event-driven application automaton. It is
not a generator or an async function: a flow owns a durable session state, a
fixed board, and a visible event-to-transition graph.

```ts
flow Door -> number ! string {
    board {
        attempts: number = 0;
    }

    event Open();
    event Reset();

    state Closed initial {
        on Open() -> Opened {
            board.attempts = board.attempts + 1;
        };
    }

    state Opened {
        on Reset() -> Closed;
        on Open() -> Completed;
    }

    state Completed {
        finish board.attempts;
    }
}
```

The M1 authored surface is module-level only. A flow has exactly one `board`,
one `initial` state, explicitly declared typed events, and flat named states.
Board fields require explicit initializers. Event payload declarations use the
normal parameter syntax (`event Retry(attempt: number);`), and an event handler
binds those values positionally (`on Retry(attempt) -> Waiting { ... };`).

Every handler names its target state. A state/event pair has at most one
transition in M1; duplicate declarations are rejected rather than acquiring an
implicit source-order priority. A false guard returns the same inspectable
`Unhandled` outcome as a missing handler, leaving state, board, and revision
unchanged. Guards and board-update expressions are deliberately limited to
pure literals, local/event bindings, primitive operators, and board reads.
Calls, async/npm/CLR operations, batch, and inline C# are rejected in flow
logic.

`flow Name -> ResultType` declares the type required by `finish value`.
`flow Name -> ResultType ! ErrorType` also declares the type required by
`fail value`; `flow Name -> void` permits a bare `finish;`. These contracts are
validated by the binder and retained in MIR.

Transitions stage updates in a local board snapshot. The backend commits the
new board and target state together only after all updates have been evaluated,
then increments the session revision. External inspection exposes a read-only
board value. Generated CLR sessions currently have `Door.Start()` and one typed
`Send<Event>(...)` method per declared event; generated JavaScript has
`Door.start()` and `session.send<Event>(...)`. A send returns a transition
result with `kind`, source/target state, event, revision, terminal status, and
an optional failure message. `Transitioned`, `Unhandled`, `Terminal`,
`Completed`, and `Failed` are the current result kinds. Reentrant sends throw
deterministically; terminal sends return `Terminal` without mutation.

The compiler owns `FlowDeclarationSyntax`, bound flow definitions, and
`MirFlowDefinition`/state/event/transition/board identities. Backends consume
that normalized graph directly; they do not recover a flow from a source-order
switch table or a dictionary-driven workflow definition. CLR emits a direct
session class and typed per-event methods. JavaScript emits a direct closure
session with the same externally observable progression law.

M1 deliberately defers flow construction parameters, source-level
`Flow.start()` calls, first-class event values, uniform `session.send(event)`,
compiler-owned flow/session/event types, source-level typed inspection, and a
rich public transition-result algebra. The current backend-facing session API
is provisional and will later be wrapped by the authored Copeland model. M1
also defers persistence/replay, eventless transitions, hierarchy, stacks,
parallelism, timers/retries, utility arbitration, and async suspension. These
are extensions to the explicit graph rather than alternative encodings of it.
