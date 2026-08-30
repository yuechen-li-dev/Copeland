# Oblivion LLM-first product baseline — M19a

## Product hypothesis

Oblivion is a persistent technical-work workspace in which pages partition context and cards bind technical content, actions, artifacts, diagnostics, and provenance. The same semantic model should support a human visual projection and an LLM/code projection. M19a tested this hypothesis against the repository-owned `machina-sample` workspace rather than assuming the M18 extraction made the product usable.

The original result was **Outcome B: useful foundation, materially limited external surface**. M19b has now resolved the recorded artifact-identity, artifact-resolution, diagnostic-severity, and local open/copy capability limitations; the M19b artifact resolution and host capability documents define the current contract.

## Doctrine

### LLM-first

Stable workspace, section, page, card, action, and artifact IDs are authoritative. Agents inspect structured state and invoke typed product actions; they do not synthesize pointer events. Diagnostics name the semantic objects involved. JSON field order follows the declared snapshot records and collection order follows durable workspace order.

### Human-second

M19a does not replace or fork the visual UI. `OblivionWorkbench`, the inspector, expanded cards, and canonical Presenter playback continue to project the same model. The CLI is an additional projection over `OblivionApplication`, not a separate product.

### Code-first

Durable product content remains ordinary JSON, TOML, and referenced Markdown. The intended edit loop is inspect → locate source reference → edit with a normal source tool → validate/reload → inspect again. M19a does not add an editor.

## Semantic product surface

The standalone `Oblivion.App` executable now accepts:

```text
inspect
pages
cards [page-id]
show <card-id>
actions <card-id>
artifacts [card-id]
artifact show <card-id> <artifact-id>
invoke <card-id> <action-id> [artifact-id]
validate
```

All commands accept `--workspace <workspace.oblivion.json>` and `--json`. With no command, the executable performs human-readable inspection of the default workspace. The in-process `OblivionProductSurface` exposes the same operations to tests and future hosts.

`inspect` separates durable workspace state from initial session defaults:

```json
{
  "schemaVersion": "oblivion.product.v1",
  "workspace": {
    "id": "machina-sample",
    "defaultPageId": "cards",
    "pages": []
  },
  "session": {
    "kind": "initial-session-defaults",
    "selectedPageId": "cards",
    "selectedCardId": null
  }
}
```

Normal inspection deliberately excludes renderer nodes, row geometry, hit targets, and scroll offsets.

## Inspection and action model

Card inspection returns identity, page/workspace ownership, kind, status, title, tags, body format, content kind, full source text, body source reference, card provenance, declared persistence actions, handler-derived available actions, artifacts, and scoped diagnostics.

Available actions are obtained from `OblivionCardHandlerRegistry`. Invocation calls `OblivionApplication.Invoke`, which produces the existing typed `OblivionEffectRequest` and routes it through `OblivionCardEffectRouter`. There is no second action model. The CLI now supplies refresh plus the M19b local open/copy adapter. Open/copy targets are resolved and safety-checked in App before the platform host receives typed requests. Headless surfaces retain deterministic capability-unavailable diagnostics.

An action record states:

```text
id, label, intent, availability, effectKind,
requiresEffect, hostCapabilityRequired, semanticallyInvokable
```

Persistence-declared actions and runtime-available actions are separate because current kind handlers sometimes replace rather than preserve declared actions. That distinction is now observable instead of hidden.

### Current action audit

All inputs are the selected workspace/page/card identity plus its source context; none accepts free-form CLI payload. All are effect requests rather than pure model transitions.

| Action ID | Meaning | Availability rule | Effect | Host capability/result |
|---|---|---|---|---|
| `refresh-markdown` | Reload referenced note content | note cards | `RefreshMarkdown` | standalone `refresh-content`; completed reload/validation |
| `open-source` | Open the card source | note with source | `OpenSource` | `open-source`; deferred when unavailable |
| `copy-source-path` | Copy the card source reference | note with source | `CopySourcePath` | `copy-source-path`; deferred when unavailable |
| `render-preview` | Render a UI preview | UI-preview cards | `RenderPreview` | `render-preview`; deferred when unavailable |
| `open-artifact` | Open the selected artifact context | artifact cards | `OpenArtifact` | `open-artifact`; deferred when unavailable |
| `export` | Export a card representation | artifact cards | `ExportCard` | `export-card`; deferred when unavailable |
| `run` | Run a code fact | code-fact cards | `RunCodeFact` | execution runtime absent; typed deferred result |
| `inspect-source` | Inspect code source | code-fact or code-theory cards | `OpenSource` | `open-source`; deferred when unavailable |
| `run-theory` | Run a code theory | code-theory cards | `RunCodeTheory` | execution runtime absent; typed deferred result |

Persistence may declare older IDs such as `open-preview`, `run-fact`, or `capture-artifact`; current specialized handlers replace these with the runtime IDs above. M19a exposes the discrepancy and treats runtime descriptors as invokable authority rather than silently pretending the lists agree.

## Diagnostics

Machine diagnostics use stable fields where known:

```text
code, severity, message, workspaceId, pageId, cardId,
actionId, effectKind, sourceReference, line, column
```

Invalid page, card, action, or command IDs produce structured errors and nonzero exits. Missing actions explicitly direct the caller to `actions <card-id>`. Workspace Markdown diagnostics are correlated to cards by their durable card/body/artifact source references. Card commands do not repeat diagnostics owned by unrelated cards.

## Visual versus semantic debugging

Product state, available actions, provenance, and failures should be read semantically first. Screenshots are final visual proof. Layout debugging should consume Machina-known bounds, baselines, wrap/clip state, and semantic region IDs when a concrete layout failure requires them; an LLM should not estimate known geometry from pixels. M19a encountered no new layout failure, so it did not add speculative layout state or screenshot diffing.

## Non-goals

M19a adds no daemon, network service, RPC, MCP server, REST endpoint, agent runtime, execution engine, rich-text editor, Markdown editor, widget suite, native UI automation, or pixel-golden test system. It does not redesign persistence, Copeland TS, Machina layout, Aurelian, or the Oblivion project graph.
