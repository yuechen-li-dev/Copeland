# Reference alignment (M7b runtime)

`GuideFrame` provides narrow reference-based placement without changing parent ownership.

- **Parent owns coordinates**.
- **Reference target is read-only geometry input**.

## GuideFrame
- Two-of-three per axis (`left/right/width`, `top/bottom/height`).
- `width`/`height` are `UiLength` only.
- `left/right/top/bottom` can be `UiLength` or `EdgeRef`.

## Edge refs
- Horizontal fields allow `left/right/centerX`.
- Vertical fields allow `top/bottom/centerY`.
- Max one reference per axis.

## Coordinate semantics
- Ui lengths resolve against parent axis.
- Ui positional values convert to root-space with parent origin.
- Edge refs read target resolved rect edge in root-space.
- Edge ref offset resolves against referencing parent axis.
- Node `offset` applies after guide placement.

## Dependency model
- Non-guide nodes resolve first.
- Guide nodes are queued until parent + all referenced targets are resolved.
- Remaining unresolved guides classify as `GuideReferenceCycle` or `GuideTargetUnresolved`.

## Errors
`GuideTargetNotFound`, `GuideSelfReference`, `GuideReferenceCycle`, `GuideInvalidEdgeForAxis`, `GuideTooManyReferencesPerAxis`, `InvalidGuideFrame`, `GuideTargetUnresolved`.

## Examples
- Start after inspector edge (`left: { ref: "inspector", edge: "right", offset: 8 }`).
- Tooltip below button (`top: { ref: "button", edge: "bottom", offset: 6 }`).
- Badge at card corner (`left` from `card.right`, `top` from `card.top`).
- Responsive variant swaps anchor to guide frame.

## Limitations
- No refs in `AnchorFrame`.
- No `AttachFrame`.
- No portals/reparenting or clipping escape.
- No general solver.
- No width/height refs.
