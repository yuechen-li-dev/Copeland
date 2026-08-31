# Oblivion transactional reload (M19j)

## Transaction model

`OblivionApplication.ReloadWorkspace` accepts the current valid
`OblivionWorkspaceSession`. It loads a complete candidate from the current
session's exact structured-vault root through `OblivionWorkspaceLoader.OpenVault`.
The loader performs the existing JSON/TOML/Markdown materialization and
validation before App considers a swap.

If candidate loading or validation returns any error, App returns the original
session object unchanged together with ordered diagnostics. Workspace, active
page, selected cards, expansion state, and scroll state are therefore preserved
as one failure-atomic unit. No candidate fragment is copied into the live
session.

If the candidate is valid, App constructs one replacement session and returns
it as the successful result. The swap is an immutable record replacement; no
durable session data is written to the vault.

## Session reconciliation

On success App preserves the active page when that page still exists. Otherwise
it chooses the candidate default page, then the first declared page.

For every remaining page, App:

- preserves the selected card when it still exists;
- otherwise selects the first card in declared order;
- preserves expansion/body view state only for cards that still exist;
- preserves page/card scroll offsets only for identities that still exist; and
- drops all stale page and card keys.

This avoids stale references while making fallback deterministic.

## CLI lifetime semantics

`oblivion workspace reload` is process-local. The one-shot CLI opens a real App
session for the supplied vault, invokes the same App reload transaction, reports
the resulting semantic state, and exits. This qualifies the transaction used by
any long-lived host without creating a resident process.

M19j qualifies transactional reload semantics, not cross-process live control.
It does not mutate an already-running Standalone instance. IPC, named pipes,
sockets, daemons, session servers, and live-host control are explicitly deferred
until concrete product pressure justifies a separate milestone.

## Proof

Focused App tests copy the real M19i structured vault, select and expand Card B,
introduce invalid card TOML, and invoke reload. They assert that reload fails,
the exact original session survives, Card B remains selected and expanded, and
the structured diagnostic is returned. After repairing the TOML and changing
Card B's title, reload succeeds and the replacement workspace exposes the new
title while retaining valid session state.

A second test removes Card B from the page and proves successful reconciliation
selects Card A and removes Card B's expansion state. CLI tests separately prove
that the command invokes this App-owned operation and emits deterministic JSON.
