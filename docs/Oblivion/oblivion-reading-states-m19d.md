# Oblivion reading states — M19d

Oblivion has exactly two reading states: `Collapsed` and `Expanded`. They are held in `OblivionSessionState` and never persisted into workspace truth. Both states retain the same card/content identity.

## Collapsed

Collapsed is a compact semantic projection, not an expanded view behind a smaller clip rectangle.

Every collapsed card communicates:

- title and subtitle;
- first useful paragraph or heading, bounded to 180 characters in the presenter plan;
- an explicit content type label such as `Markdown + Mermaid`, `Code · csharp`, `PNG`, `Text`, or `Artifact`;
- source filename where present;
- status/tags and artifact/action badges.

Collapsed bodies never request internal scrolling. Markdown uses the first authored heading/paragraph summary. Code retains language/source and a compact excerpt. Diagram source announces Mermaid even when no renderer exists. PNG/artifact cards retain kind, source, existence facts, and external-open behavior.

## Expanded

Expanded is the reading surface. The card header and collapse action remain Machina-owned. The card-body rectangle may host an Avalonia control in the interactive host; the native renderer remains the headless fallback.

Per content kind:

| Content | Expanded behavior |
| --- | --- |
| Markdown | selectable Avalonia document, wrapped prose, bounded vertical scroll |
| Code | selectable monospace text, local horizontal and bounded vertical scroll |
| Mermaid | externally derived fitted PNG, or source plus typed diagnostic |
| PNG | fitted inline image with preserved aspect ratio and bounded vertical scroll |
| Plain text | native readable text; presenter plan permits bounded vertical scroll |
| Artifact metadata | native semantic facts, no independent scroll |

Expansion uses `ExpandedPreferredHeight` for code and artifact cards so the mature host has a useful body region. Collapse/expand does not alter source, artifact identity, provenance, or durable order.

## Scroll ownership

- Main page/card pane: Machina owns vertical navigation through the card stream.
- Collapsed content: no local scroll.
- Expanded Markdown/image/diagram: the Avalonia `ScrollViewer` owns wheel input inside the body; vertical bars appear only when content exceeds the body bound.
- Expanded code: the Avalonia `ScrollViewer` owns local horizontal overflow and bounded vertical overflow.
- Headless expanded Markdown: the existing Machina expanded-body offset and scrollbar remain the deterministic fallback/playback contract.
- Inspector: its pane-level Machina scroll remains unchanged; the raw Markdown body uses a local mature selectable code surface.

The body overlay occupies only the resolved body rectangle. Header clicks therefore continue to select/collapse through Machina. Tests retain nested-scroll precedence for the native fallback and assert the mature host scrollbar dispositions.

## Focus and input ownership

- Machina owns card selection, collapse/expand, product actions, keyboard navigation, and durable/session state mutation.
- The hosted presenter owns text selection, copy, local wheel input, and its local scroll offset.
- Images and diagrams do not take product action ownership.
- Links are currently displayed as label plus target. Link activation is deliberately not implemented until it can emit a semantic event outward.
- Hosted controls never call `OblivionSessionState` or mutate cards.

When focus is inside selectable text, input remains a presentation concern. Product-level actions continue through the surrounding shell.
