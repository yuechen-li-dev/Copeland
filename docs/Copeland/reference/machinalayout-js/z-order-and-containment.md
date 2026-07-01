# Z-order and Containment

## Z-order

- `z?: number` is node metadata.
- Explicit `z` must be an integer in `-5..5`.
- Omitted `z` defaults to effective `0`.
- `z` is sibling-local.
- Higher `z` paints in front.
- Same `z` uses sibling order as tie-break.
- `z` does not affect geometry.
- `z` does not affect stack placement.
- `z` does not reorder compiled/resolved/tree children.
- React adapter sorts sibling render order by effective `z` for paint only.

Rationale: if a UI needs more than eleven sibling-local paint layers, the layout structure should be reconsidered.

## Containment policy

Containment is adapter policy, not core layout semantics.

React adapter props:

- `nodeContainment?: "none" | "layout-paint" | "strict"`
- `nodeContentVisibility?: "none" | "auto"`
- `nodeContainIntrinsicSize?: string`

Defaults:

- `nodeContainment = "layout-paint"`
- `nodeContentVisibility = "none"`

Guidance:

- `layout-paint` is the default safe performance posture.
- `strict` is stronger and opt-in.
- `content-visibility: auto` is powerful for large/offscreen content but opt-in.
- `contain-intrinsic-size` can be supplied when using content visibility.
