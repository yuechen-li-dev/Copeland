# Oblivion structured vault — M19i

## Design goals

M19i establishes one predictable, explicit, human-authored vault root. Semantic identities map to one metadata location, bodies remain ordinary Markdown files, and every lookup starts from the root supplied to `OblivionWorkspaceLoader.OpenVault`. The loader never searches parents, children, titles, or nearby filenames.

The design borrows structural discipline from pnpm/tspack-style stores: a known root, stable identities, fixed subdirectories, manifest-driven order, separation of identity from realization, and repeatable materialization. It explicitly does not adopt content-addressable storage, hashes, deduplication, symlinks, package graphs, install lifecycles, or package-manager semantics.

## Exact fixture tree

```text
M19iNotebook.oblivion/
├── workspace.json
├── pages/
│   └── notebook.toml
├── cards/
│   ├── physical-atom.toml
│   └── notebook-stack.toml
└── content/
    ├── physical-atom.md
    └── notebook-stack.md
```

The product proof lives at `src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion`. It contains exactly one workspace, one Page, two Card records, and two Markdown objects.

## Workspace manifest

`<root>/workspace.json` is the only structured-vault workspace manifest location. It uses format 1 JSON and declares only the workspace ID, title, default Page ID, and ordered Page IDs:

```json
{
  "format": 1,
  "kind": "oblivion-workspace",
  "workspaceId": "m19i-notebook",
  "title": "Oblivion",
  "defaultPageId": "notebook",
  "pages": ["notebook"]
}
```

The older embedded `sections/pages/cards` format remains supported by the legacy manifest-path loader for existing Presenter/dogfood workspaces. It is deliberately rejected by the structured-vault entry so the two contracts cannot be mixed ambiguously.

## Page metadata

Page ID `P` maps to `<root>/pages/P.toml`. The record owns semantic Page metadata and ordered Card IDs. For M19i, `pages/notebook.toml` declares `cards = ["physical-atom", "notebook-stack"]`. It contains no selection, expansion, scrolling, host, or layout state.

## Card metadata and Markdown objects

Card ID `C` maps to `<root>/cards/C.toml`. Each Card record declares its ID, semantic kind/status, title, subtitle, tags, and one explicit vault-relative Markdown reference. The fixture references `content/physical-atom.md` and `content/notebook-stack.md`.

Markdown is never duplicated into TOML. Explicit references were chosen over inference: the loader resolves exactly `<root> + body.path`, verifies the result remains inside the vault, and requires the declared file to exist. It does not infer content from Card title, Card ID, nearest Markdown file, or directory scanning.

## IDs and paths

`physical-atom` is a semantic Card ID; `cards/physical-atom.toml` is its deterministic realization. IDs permit ASCII letters, digits, `.`, `_`, and `-`, cannot be `.` or `..`, and cannot contain directory separators. Absolute paths are not identities and are not accepted as structured content references.

Exact lookup rules are:

1. Workspace manifest: `<root>/workspace.json`.
2. Page metadata for Page ID `P`: `<root>/pages/P.toml`.
3. Card metadata for Card ID `C`: `<root>/cards/C.toml`.
4. Markdown: the single explicit vault-relative `body.path` in Card `C`, resolved beneath `<root>`.

There are no fallback candidates or intent-path heuristics.

## Validation

The structured loader diagnoses a missing manifest, unsupported format, invalid or duplicate Page IDs, invalid or duplicate Card IDs, invalid default Page, missing or mismatched Page metadata, missing or mismatched Card metadata, unknown Card kind, absent Markdown reference, missing Markdown file, absolute reference, and path traversal. Diagnostics retain a stable code and source path; structured context adds workspace, Page, and Card identities when available. Invalid objects are not silently skipped into a successful session.

## Editing and inspection workflow

A human or LLM can inspect the root tree, read `workspace.json`, follow the Page ID to `pages/notebook.toml`, follow its ordered Card IDs to `cards/*.toml`, and follow each Card's explicit body reference to `content/*.md` without repository-source reading or grep.

The M19i reload contract is explicit process/session recreation, not watching:

1. Edit a `content/*.md` file and restart the standalone app; the expanded Card reads the new body.
2. Edit a Card title in `cards/*.toml` and restart; the shell reads the new title.

Temporary-vault tests prove both edits are absent from an already loaded immutable model and present after a new vault/session open.
