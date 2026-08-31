# Oblivion stack mutation CLI — M19k

M19k adds the first durable stack mutations to the M19j `System.CommandLine` surface:

```text
oblivion card push <markdown-file> --workspace <vault> [--page <id>] [--id <id>] [--title <title>] [--subtitle <subtitle>]
oblivion card peek --workspace <vault> [--page <id>]
oblivion card pop --workspace <vault> [--page <id>]
```

The CLI parses arguments and formats results. `OblivionWorkspaceControl` and `OblivionApplication` own the typed product operations; `OblivionStackMutation` owns structured-vault reads, candidate construction, validation, and the bounded commit/rollback work. The CLI neither parses TOML nor copies or deletes vault files.

## Stack and Page semantics

A Page is an ordered stack whose top is the last Card ID in `pages/<page-id>.toml`. Push appends exactly one ID. Peek reads the last Card without mutation. Pop removes only the last Card and accepts no Card ID.

`--page` selects an exact durable Page ID. Without it, all three commands use `workspace.json.defaultPageId`. They never consult selection, expansion, or an active UI Page. A missing explicit Page reports `unknown-page`; a workspace without a default reports `OBLIVION-PAGE-TARGET-REQUIRED`.

An empty Page is valid. Peek and pop on it report `OBLIVION-STACK-EMPTY` as a product failure.

## Push import and identity

Push requires a readable `.md` file. It imports the exact source bytes into `content/<card-id>.md`; future loading uses only that vault-relative path. The original absolute path may be retained as diagnostic provenance, but is never a loading dependency.

Without `--id`, the filename stem is lowercased, every run of non-alphanumeric characters becomes one hyphen, and leading/trailing separators are removed. `Architecture Notes.md` therefore becomes `architecture-notes`. Explicit IDs must already be lowercase safe structured-vault IDs. Existing semantic IDs report `OBLIVION-CARD-ID-ALREADY-EXISTS`; existing canonical metadata or content files report `OBLIVION-CARD-IMPORT-DESTINATION-CONFLICT`. No suffix is invented.

Title precedence is deterministic:

1. a non-empty `--title`;
2. the first Markdown level-one heading beginning with `# `;
3. a humanized filename stem.

Imported Cards use `card_kind = "note"`, `status = "idle"`, the `imported-markdown` tag, and optional subtitle. Their durable provenance records `source_kind = "imported-markdown"`, the original source reference, and producer action `oblivion.card.push`.

## Transaction boundary

Push and pop copy the vault into a sibling staging directory, apply the complete candidate mutation there, and load/validate that staged structured vault through the normal persistence path. Only a valid candidate is committed.

Push creates content and metadata without overwrite and replaces Page metadata last. Pop replaces Page metadata and deletes canonical Card metadata plus safely owned content. Each commit retains the original affected bytes and restores them if any bounded file operation fails. Staging directories are removed after success or failure.

This prevents copied content without metadata, metadata without Page membership, a Page reference to a missing Card, and half-written TOML. Precondition and candidate-validation failures do not touch the live vault.

## Pop ownership

Card metadata is the canonical `cards/<card-id>.toml`. The body must resolve beneath the vault `content/` directory; unsafe or missing ownership is refused. Persistence counts body-path references across every materialized Card:

- one reference: delete the Markdown file;
- more than one reference: remove the popped Card metadata but retain content and emit `OBLIVION-CARD-CONTENT-RETAINED`.

Traversal, absolute paths, missing metadata/content, and other structural inconsistencies fail through existing validation diagnostics before mutation. Pop never guesses an alternate path.

## Session reconciliation

Push reloads the committed workspace while preserving a valid existing selection and expansion state; the new Card is not auto-selected. Pop removes stale expansion and raw-source state. If the popped Card was selected, the new top Card becomes selected. If the Page becomes empty, selection is empty. No session state is written to the vault.

## Output and diagnostics

Human output is line-oriented and reports the operation, Page, `oldCount → newCount`, and affected paths. Peek reports top ID, title, Markdown kind, and vault-relative source. `--json` emits deterministic typed fields including `operation`, `workspaceId`, `pageId`, `cardId`, counts, paths, `contentDeleted`, `success`, and diagnostics; no timestamp is present.

M19k-specific diagnostics are:

- `OBLIVION-STACK-EMPTY`
- `OBLIVION-PAGE-TARGET-REQUIRED`
- `OBLIVION-CARD-ID-ALREADY-EXISTS`
- `OBLIVION-CARD-IMPORT-SOURCE-MISSING`
- `OBLIVION-CARD-IMPORT-SOURCE-INVALID`
- `OBLIVION-CARD-IMPORT-DESTINATION-CONFLICT`
- `OBLIVION-CARD-POP-OWNERSHIP-AMBIGUOUS`
- `OBLIVION-CARD-CONTENT-RETAINED`

Equivalent existing diagnostics, including `unknown-page`, `path-traversal-not-allowed`, and missing structured-vault assets, remain authoritative where applicable.
