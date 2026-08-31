# Oblivion stack mutation dogfood — M19k

## Real task

The dogfood task added a technical note about the M19k transaction boundary to the real `M19iNotebook.oblivion` structured vault. The note originated at `artifacts/m19k/m19k-stack-dogfood-note.md`, outside the vault, and was manipulated only through the CLI. The source was edited after push to prove capture independence.

## Commands and state transitions

The starting Page order was:

```text
physical-atom
notebook-stack
```

The real commands were:

```text
oblivion card push artifacts/m19k/m19k-stack-dogfood-note.md -w src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion
oblivion card peek -w src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion
oblivion card list -w src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion
oblivion card show m19k-stack-dogfood-note -w src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion
oblivion workspace validate -w src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion
oblivion card pop -w src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion
oblivion workspace validate -w src/Oblivion/Oblivion.Standalone/M19iNotebook.oblivion
```

Push reported `2 → 3`; peek and list agreed that `m19k-stack-dogfood-note` was the top Card. Its title was derived from `# Stack mutation transaction boundary`, and its body reference was `content/m19k-stack-dogfood-note.md`. The vault validated with one Page, three Cards, zero errors, and zero warnings.

The imported content SHA-256 remained `3509E56AB28851B4BA1900D0200F3B3053B9220FA2A73E1FCD46C668D6CA22DA` after the external source gained an additional sentence. `card show` continued to load the captured vault copy. This proves the external path is provenance rather than a loading dependency.

Pop reported `3 → 2`, removed both canonical metadata and uniquely owned content, and restored the exact Page order:

```text
physical-atom
notebook-stack
```

The restored vault again validated with one Page, two Cards, zero errors, and zero warnings. Focused tests also proved that push after pop derives the same ID and recreates the same `2 → 3` order.

## Visual result

The first push-state launch exposed an obsolete Standalone assertion requiring exactly two Cards. This was a genuine M19k blocker because a valid three-Card vault could not be projected. The local fix removed only the cardinality special cases from the existing stack surface/renderer and retained the Markdown-only contract and existing layout.

After that fix, `artifacts/m19k/standalone-push-three-card.png` visibly shows the third Card at the bottom of the ordinary vertical stack. `artifacts/m19k/standalone-pop-restored-two-card.png` shows the original two-Card state after pop. No Card was hard-coded into the UI or fixture.

## Friction log

| Friction | Classification | Evidence | M19k action |
|---|---|---|---|
| Standalone rejected any Card count other than two | BLOCKING | First real three-Card launch threw from `AssertTwoMarkdownCards`; renderer had a matching two-only guard | Removed the obsolete cardinality guards and added a real three-Card product-path test |
| `card show` exposes only a bounded preview, not the complete Markdown body | REPEATED | Full content inspection was needed during ordinary note review and again for the external-source independence proof; the dogfood had to use a file hash for the tail beyond the preview | Recorded only; no new command added in M19k |
| Default-Page push/peek/pop and top-only ordering | NO_FRICTION | The real authoring sequence needed no arbitrary insert, move, rename, or remove | No expansion |
| ID and heading-derived title | NO_FRICTION | The filename and first heading produced the intended stable identity/title | No expansion |
| Pop ownership | NO_FRICTION | Unique content deleted in dogfood; controlled shared-content test retained the shared Markdown | No expansion |

## Outcome and next milestone

Outcome A: the stack mutation model survives dogfood. Top-only push/peek/pop was natural for the bounded notebook-authoring task, ownership remained safe, and the final vault returned to its two-Card baseline.

The evidence-backed M19l scope is one read-only command: expose the complete vault-owned Markdown body for an exact Card through App/Persistence, with raw shell-friendly human output and deterministic JSON. A suitable shape is `oblivion card content <card-id>`. Do not add edit, rename, insert, move, reorder, or arbitrary remove based on this dogfood; none was needed.

## Qualification

The final two-Card vault passed `Oblivion.slnx`, `JointTaskForce.slnx`, `Copeland.slnx`, `Machina.UI.slnx`, `Machina.UI.Slow.slnx`, the no-restore Machina build, and the no-build Aurelian tests. Canonical Presenter playback passed 14 of 14 scenarios with zero failures and zero skips; its report is `artifacts/m19k/playback-final/playback-suite-report.json`. Final whitespace validation passed with `git diff --check`.
