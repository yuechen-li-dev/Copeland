# Oblivion explicit viewport layout — M20b

## Result

M20b establishes `OblivionViewportState` as process-local session state. It is a projection over the existing ordered Page/Card stack; no viewport field is written to workspace, Page, or Card persistence.

The supported modes are `Single`, `VerticalSplit`, and `HorizontalSplit`. Topology changes only through `layout.single`, `layout.vertical-split`, or `layout.horizontal-split`; resize recomputes rectangles without choosing a mode. `layout.focus-next` moves focus between A and B. There is one split level, a fixed 0.5/0.5 ratio, and no responsive topology heuristic.

## Assignment and focus

Slot A receives the selected Card. In a split, Slot B receives the next Card in Page order. If there is no next Card, B is a quiet empty slot; no Card is duplicated or synthesized. Focus is `A` or `B` and remains distinct from durable Page/Card semantics and Card selection. Focused-Card commands resolve slot, then Card.

`layout.single` projects the selected Card into A. Returning from a split does not mutate Page order, selection, push/pop meaning, or Card content.

## Geometry ownership

The 2560×1440 qualification has a 2384×1296 usable viewport after 88 px horizontal and 72 px vertical margins. Vertical split yields two 2384×636 slots separated by 24 px. Horizontal split yields two 1180×1296 slots separated by 24 px.

An expanded Card fills its slot; its semantic model owns no pixel height. A collapsed Card remains 174 px high at the top of the slot rather than stretching empty Card chrome. Page content height equals viewport height, so the old fixed-760-px expanded Card no longer leaves an Ishimura-sized void or creates overflow merely because two Cards are expanded.

The visual and JSON proofs are under `artifacts/m20b`. Each capture has a `.viewport.json` sidecar containing window size, usable viewport, mode, focus, slot rectangles, assignments, and Diagram camera metrics.

## Commands and human behavior

- `layout.single`
- `layout.vertical-split`
- `layout.horizontal-split`
- `layout.focus-next`
- Ctrl+1, Ctrl+2, Ctrl+3 choose the three layouts in Standalone.
- Ctrl+Tab moves slot focus.

Pointer selection inside a split focuses the slot instead of rewriting Card selection and shifting the projection.

## Non-goals

M20b adds no nested tiling, tabs, docking, floating windows, persisted window-manager state, resize handles, or automatic mode selection.

Verdict: `EXPLICIT_VIEWPORT_LAYOUT_ESTABLISHED`.
