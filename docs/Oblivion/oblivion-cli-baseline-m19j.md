# Oblivion CLI baseline (M19j)

## Ownership and topology

`src/Oblivion/Oblivion.Cli` is the first-class `oblivion` executable. It references
`Oblivion.App` and `System.CommandLine`; it does not directly reference
Oblivion persistence, UI, Standalone, Avalonia, Machina, Presenter, or Aurelian.

`Oblivion.App` owns structured-vault opening, semantic inspection, validation,
and transactional reload behavior. The CLI owns command declarations, argument
mapping, text/JSON formatting, and exit-code mapping only. Standalone and CLI
are sibling frontends over App behavior.

The package version is `System.CommandLine` 2.0.9, centrally declared in
`Directory.Packages.props`.

## Command tree

```text
oblivion
  workspace
    show
    validate
    reload
  page
    list
  card
    list [--page <page-id>]
    show <card-id>
```

The root options are recursive and therefore accepted by every leaf command:

- `--workspace <path>` / `-w <path>` is required and names one exact structured
  vault root.
- `--json` writes one machine-readable result to stdout.

There is no default workspace, current-directory search, or nearest-workspace
discovery.

## Output contract

Human mode writes successful command results to stdout and product diagnostics
to stderr. Normal vault failures do not print stack traces.

JSON mode writes one deterministic camel-case JSON value to stdout. Successful
commands expose command-specific semantic records rather than persistence DTOs.
Product failures expose structured diagnostics in JSON on stdout and leave
stderr empty. Record property order and workspace declaration order are stable.

`card show` includes a bounded 400-character Markdown preview. It does not dump
an unbounded body and M19j adds no speculative content flag.

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Command succeeded. |
| 1 | Product operation or validation failed. |
| 2 | Command-line usage error. |
| 3 | Workspace manifest was not found or the workspace was unavailable. |
| 4 | Unexpected internal failure. |

System.CommandLine supplies normal root, group, leaf, and argument help. M19j
does not add a custom help or completion engine.

## App reuse

`OblivionWorkspaceControl` is the small App-owned shell-facing projection. It
uses `OblivionApplication.OpenWorkspace`, the structured-vault loader, and the
App reload transaction. CLI sources contain no file reading, TOML parsing, vault
materialization, action routing, or session reconciliation.

`Oblivion.App` is now a library. Its previous executable entry point and
handwritten `OblivionCommandLine` parser were removed so the repository has one
CLI parser and one executable command surface.

## M19a/M19b command disposition

| Previous command | M19j disposition |
|---|---|
| `inspect` | `MIGRATE_NOW` as `workspace show`. |
| `pages` | `MIGRATE_NOW` as `page list`. |
| `cards [page]` | `MIGRATE_NOW` as `card list [--page]`. |
| `show <card>` | `MIGRATE_NOW` as `card show <card-id>`. |
| `validate` | `MIGRATE_NOW` as `workspace validate`. |
| `actions`, `artifacts`, `artifact show`, `invoke` | `KEEP_INTERNAL`: typed App product contracts remain; no M19j command expansion. |
| `presentation inspect`, `presentation realize-diagram` | `KEEP_INTERNAL`: presentation inspection/realization APIs and tests remain, but are not workspace CLI commands. |
| `refresh-markdown` action invocation | `DEFER`: App action semantics remain available; M19j uses transactional workspace reload. |

No compatibility parser remains. Scripts using the M19a experimental spelling
must migrate to the command tree above.

## Non-goals

M19j adds no GUI controls, watcher, daemon, IPC, named pipe, socket, HTTP, MCP,
networking, fuzzy discovery, database, search index, editor, execution surface,
or custom shell language. It does not control an already-running Standalone
process.
