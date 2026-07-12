# Machina Runtime Hit Testing (M0a)

## Purpose

M0a introduces a pure runtime input seam that resolves pointer coordinates into `UiAction` values by combining:

- resolved layout geometry (`ResolvedLayoutDocument`), and
- lowered action metadata (`UiLoweringResult.Actions`).

This milestone is intentionally independent from rendering and from Dominatus integration.

## Coordinate space

Pointer coordinates are root-local and must be in the same coordinate space used to resolve `ResolvedLayoutDocument` (the layout root `Rect`).

Presenter/window coordinate transforms are deferred to later milestones.

## Action source of truth

Only nodes present in `UiLoweringResult.Actions` are actionable.

- Nodes with semantics but without action metadata are not hit-test targets.
- Disabled standard controls naturally do not hit because lowering omits their action entries.

## Hit-test policy in M0a

`UiHitTestIndex` builds a deterministic candidate list from resolved-tree pre-order traversal, then evaluates candidates in reverse traversal order.

In effect, **last actionable node in pre-order wins**, which gives later siblings/descendants priority in overlaps.

### Bounds rule

A hit uses half-open bounds:

- `x >= rect.X`
- `x < rect.X + rect.Width`
- `y >= rect.Y`
- `y < rect.Y + rect.Height`

Nodes with zero or negative width/height are excluded from candidates.

## Explicitly deferred

M0a does **not** include:

- clip-stack-aware hit testing
- painter/z sorting integration
- focus, keyboard, text editing, drag/drop
- hover/pressed styling
- event bubbling/capture/routing
- presenter event wiring
- Dominatus mailbox/actuator ingress

A separate Dominatus input bridge can be added in a follow-up milestone without changing this pure hit-test layer.
