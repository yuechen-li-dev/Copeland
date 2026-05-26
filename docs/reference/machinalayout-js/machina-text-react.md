# MachinaText React Renderer (M2c)

`MachinaTextView` renders MachinaText content inside a rectangle already owned by a parent view. It does not participate in Machina layout resolution, sizing, or measurement.

## Component

- `MachinaTextView`
- `MachinaTextViewProps`

### `text` input forms

- `string`: parsed as `{ kind: "machina-text", text }`
- `MachinaTextSource`: parsed directly (`plain` stays literal)
- `MachinaTextSpec`: parses `spec.source` and applies text policy
- `MachinaTextDocument`: rendered directly with default policy

## Policy fields

Supported policy fields:

- `variant`: `body | label | caption | title | mono`
- `wrap`: `word | none`
- `overflow`: `clip | ellipsis | scroll`
- `align`: `start | center | end`
- `leading`: `tight | normal | loose | number`
- `blockGap`: non-negative number (pixels)
- `listGap`: non-negative number (pixels)
- `valign`: `top | center | bottom`

Defaults are `body`, `word`, `clip`, `start`, `normal`, `8`, `2`, `top`.

Vertical rhythm policy is internal to the text renderer inside an already-owned rectangle. It does not change outer Machina layout, resolver behavior, or sizing.

- `leading` maps to `line-height` (`tight=1.15`, `loose=1.6`, `normal=variant default`, numeric uses provided value when valid).
- `blockGap` controls spacing between top-level text blocks (paragraph/list blocks).
- `listGap` controls spacing between list items (including nested list items).
- `valign` controls vertical placement of content in the wrapper rectangle.

`MachinaTextView` may use internal flex layout for this vertical positioning only. This does not imply flex/grid-based Machina node placement.

## Link behavior boundary

Links render as standard `<a>` tags.

- optional `linkTarget`
- if `_blank`, renderer sets `rel="noreferrer noopener"`
- optional `onLinkClick(href, event)` callback

No routing, dispatch, or action semantics are implemented.

## Diagnostics

Parsing diagnostics never throw. Best-effort content still renders.

Set `showDiagnostics` to render parser diagnostics for development.


## Overflow ellipsis policy (M3a)

`overflow: "ellipsis"` is defined as **single-line ellipsis** in M3.

When `overflow` is `ellipsis`, renderer normalization forces:

- `white-space: nowrap`
- `overflow: hidden`
- `text-overflow: ellipsis`

This overrides `wrap` for deterministic behavior. Multi-line ellipsis / line clamp is not supported.
