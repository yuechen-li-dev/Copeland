# Oblivion CLI dogfood — M19l

## Workflow

The real dogfood used `src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion` and the external note `artifacts/m19l/real-note.md`. The vault began with two Cards, temporarily held three, and was restored to the same two-Card order.

```text
oblivion workspace show -w <vault>
oblivion card list -w <vault>
oblivion card peek -w <vault>
oblivion card content notebook-stack -w <vault>
oblivion card content physical-atom -w <vault> --json
oblivion card show notebook-stack -w <vault>
oblivion card push artifacts/m19l/real-note.md -w <vault>
oblivion card peek -w <vault>
oblivion card content real-note -w <vault>
oblivion card content real-note -w <vault> --json
oblivion workspace validate -w <vault>
oblivion card pop -w <vault>
oblivion workspace validate -w <vault>
```

The required final smoke also ran human and JSON content retrieval plus `show` for `physical-atom`, followed by `peek` on the restored vault.

Push reported `2 → 3`; peek identified `real-note`; raw content returned all 586 characters; JSON returned the same semantic text and `content/real-note.md` identity. The three-Card vault validated with zero errors and zero warnings. Pop reported `3 → 2`, removed the owned metadata and Markdown, and the restored vault again validated with zero errors and zero warnings.

Across six real `card content` calls, 4,147 Markdown characters were retrieved: four human/raw paths and two JSON paths.

## `show` and `content`

`card show notebook-stack` ended its preview after 400 characters plus an ellipsis, before the complete body tail. `card content notebook-stack` returned the full 809-character source. `card show` remained useful for title, status, tags, provenance, actions, and a bounded glance; `card content` supplied the complete payload without repeating that metadata.

Combined with `card peek`, these commands now provide enough read access for ordinary LLM notebook work. Codex can discover the top Card, inspect its metadata, and retrieve its complete Markdown without UI automation or implementation-source reading.

## Friction log

| Friction | Classification | Evidence | Action |
|---|---|---|---|
| Full Markdown was unavailable beyond the `card show` preview | REPEATED | M19k needed the tail repeatedly; M19l raw and JSON retrieval returned complete 722-, 809-, and 586-character payloads | Resolved by `card content` |
| `peek + show + content` read workflow | NO_FRICTION | The required base and pushed-Card reads needed no UI state, source spelunking, Page inference, or extra flags | No expansion |
| Page targeting | NO_FRICTION | Workspace-global Card identity resolved deterministically; explicit `--page notebook` returned the same Card | Preserve current identity rules |
| JSON payload ergonomics | NO_FRICTION | A standard JSON parse reconstructed embedded newlines and the full source exactly | No alternate JSON shape |
| Push/pop rewrites Page metadata line endings | MINOR | The LF-authored tracked `pages/notebook.toml` returned with the same semantic content and Card order but CRLF line endings after the real push/pop loop | Recorded for M19m; restored the dogfood fixture byte-clean after observation |
| Next missing CLI verb | NO_FRICTION | The bounded workflow completed without edit, append, search, reorder, or arbitrary removal pressure | Do not invent a command |

## Outcome and recommended M19m

Outcome A: full content retrieval closes the read-side gap. The motivating operation is semantic, complete, deterministic, shell-composable, and independent of UI or implementation source.

The next actual friction is a minor persistence-fidelity issue, not a missing CLI verb: push/pop preserves Page meaning and order but rewrites an LF-authored Page TOML file with platform CRLF line endings. The exact evidence-backed M19m scope is lossless structured Page metadata mutation: preserve the existing newline convention while changing only the `cards` array, prove push/pop restores the original Page bytes, and repeat real stack dogfood. Add no edit, append, search, reorder, or removal verb.
