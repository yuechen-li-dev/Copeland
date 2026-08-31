# Oblivion command registry — M19m

## Descriptor and identity model

`OblivionCommandRegistry` is an App-owned registry, not the `System.CommandLine` tree and not a generic command bus. Each `OblivionCommandDescriptor` has a typed `OblivionCommandId` plus stable external `id`, `title`, `description`, `scope`, and `available` fields. Registry order is stable.

| ID | Scope | Semantics |
| --- | --- | --- |
| `workspace.reload` | `workspace` | calls the existing transactional `OblivionApplication.ReloadWorkspace` operation |
| `cards.expand-all` | `active-page` | expands every Card in process-local active-Page session state |
| `cards.collapse-all` | `active-page` | collapses every Card in process-local active-Page session state |

All three descriptors are available because each has a real in-process implementation. `view.reset` is not registered because its target state is still vague.

## CLI and execution

`oblivion command list` needs no Workspace and prints stable IDs/titles; JSON additionally exposes description, scope, and availability. `oblivion command run <command-id> -w <vault>` maps the external ID once at the App control edge, opens a process-local App session, and dispatches the typed identity through the registry. Unknown IDs fail with `OBLIVION-COMMAND-UNKNOWN`.

Expansion is session-only and writes no vault files. Each CLI invocation is a new process, so running `cards.expand-all` does not affect a separately running Standalone GUI or a subsequent CLI invocation. `cards.collapse-all` therefore reports zero affected Cards when invoked in a fresh default-collapsed process. This is intentional M19m process-local behavior; there is no IPC, daemon, watcher, or networking.

A future command palette, ribbon, menu, or shortcut layer can discover the same descriptors and submit typed IDs against its live App session. M19m adds none of those UI surfaces.
