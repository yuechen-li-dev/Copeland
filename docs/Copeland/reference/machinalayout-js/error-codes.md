# Error code reference

This document summarizes public layout and text diagnostic codes.

## MachinaLayoutErrorCode

### Row/compile structure

- `EmptyRows` — no rows were provided. Typical cause: resolver/compile called with an empty array.
- `MissingRoot` — no root row exists. Typical cause: no `RootFrame` row in the selected row set.
- `MultipleRoots` — more than one root row exists. Typical cause: duplicate root definitions.
- `DuplicateId` — two rows share one id. Typical cause: authoring collision during merge/generation.
- `InvalidId` — id is blank/invalid. Typical cause: malformed row data.
- `MissingParent` — non-root row omitted `parent`. Typical cause: incomplete row authoring.
- `UnknownParent` — parent id does not exist. Typical cause: typo or variant mismatch.
- `SelfParent` — row points to itself as parent. Typical cause: authoring mistake.
- `Cycle` — parent graph contains a loop. Typical cause: circular parent references.
- `UnreachableNode` — node cannot be reached from root. Typical cause: disconnected subtree.

### Numeric/length validation

- `NonFiniteNumber` — numeric field is `NaN`/`Infinity`. Typical cause: bad arithmetic or unvalidated input.
- `InvalidLengthUnit` — unsupported `UiLength` unit. Typical cause: unit typo.
- `InvalidZ` — z metadata is invalid/out of bounds. Typical cause: non-integer or unsupported range.
- `NegativeSize` — width/height input is negative. Typical cause: invalid frame dimensions.
- `NegativeGap` — arranger gap is negative. Typical cause: invalid stack/grid config.
- `NegativePadding` — padding is negative. Typical cause: invalid arranger padding config.

### Root/frame context

- `RootFrameNotRoot` — `RootFrame` was used on a non-root node. Typical cause: wrong frame kind for child row.
- `RootFrameWithoutRoot` — root frame resolution lacked root context. Typical cause: incorrect direct frame resolution call.
- `FixedFrameWithoutArranger` — `FixedFrame` used where parent does not arrange children. Typical cause: fixed child under non-arranger parent.
- `FillFrameWithoutArranger` — `FillFrame` used where parent does not arrange children. Typical cause: fill child under non-arranger parent.
- `CellFrameWithoutGrid` — `CellFrame` used under non-grid arranger parent. Typical cause: cell child not placed in a grid container.

### Anchor/size resolution

- `InvalidAnchorHorizontal` — horizontal anchor constraints are inconsistent. Typical cause: missing/overconstrained left/right/width combination.
- `InvalidAnchorVertical` — vertical anchor constraints are inconsistent. Typical cause: missing/overconstrained top/bottom/height combination.
- `NegativeResolvedSize` — resolved width/height became negative. Typical cause: conflicting constraints relative to parent rect.

### Stack

- `InvalidFillWeight` — fill weight is invalid. Typical cause: zero/negative/non-finite fill weight.
- `StackChildMustBeFixed` — stack direct child frame kind is unsupported. Typical cause: child is neither `FixedFrame` nor `FillFrame`.
  - Note: this code name is historical and stable; do not rename it.
- `StackContentNegative` — stack content space became negative. Typical cause: padding/gaps exceed container space.
- `StackOverflow` — stack children exceed available axis space. Typical cause: fixed sizes + gaps exceed container.

### Grid

- `GridChildMustBeCell` — direct grid child is not `CellFrame`. Typical cause: wrong frame kind under grid arranger.
- `InvalidGridTrack` — track definition is invalid. Typical cause: malformed `GridArrange` track settings.
- `InvalidGridCell` — cell coordinates/span are invalid. Typical cause: out-of-range row/col/span.
- `GridContentNegative` — grid content space became negative. Typical cause: padding/gaps exceed container space.
- `GridOverflow` — grid placement overflows available tracks/space. Typical cause: tracks/cells incompatible with container.

### Guide/reference alignment

- `GuideTargetNotFound` — guide target id is missing. Typical cause: typo or missing target row.
- `GuideSelfReference` — guide references its own node. Typical cause: self-targeting guide config.
- `GuideReferenceCycle` — guide references create a cycle. Typical cause: mutually dependent guide nodes.
- `GuideInvalidEdgeForAxis` — requested reference edge is invalid for chosen axis. Typical cause: axis/edge mismatch.
- `GuideTooManyReferencesPerAxis` — too many references were provided for one axis. Typical cause: overconstrained guide input.
- `InvalidGuideFrame` — guide frame declaration is malformed. Typical cause: incomplete or conflicting guide spec.
- `GuideTargetUnresolved` — guide target exists but was not resolved when needed. Typical cause: invalid dependency order/cycle.

### Variants

- `InvalidVariantCondition` — variant condition is invalid. Typical cause: unsupported operator/value shape.

### Interpolation

- `IncompatibleLayouts` — two resolved layouts cannot be interpolated safely. Typical cause: node identity/topology mismatch during `lerpResolvedLayouts`.

## MachinaText diagnostics

- `unsupported_syntax` — source includes unsupported markdown-like construct. Typical cause: blockquotes, task lists, ordered lists, fences, HTML, or unsupported source kind.
- `heading_forbidden` — heading syntax was used. Typical cause: `#`, `##`, etc. in MachinaText content.
- `max_list_depth_exceeded` — bullet nesting exceeds supported depth. Typical cause: lists nested deeper than policy allows.
- `malformed_link` — link syntax is invalid. Typical cause: missing delimiters or empty label/href shape.
- `unclosed_inline` — inline marker was not closed. Typical cause: unmatched `*`, `**`, or backtick markers.
- `invalid_escape` — escape sequence is unsupported or dangling. Typical cause: unknown escape like `\q` or trailing `\`.
