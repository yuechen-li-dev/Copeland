# Machina Authoring Model (M2a reset)

## Canonical model

Machina layout remains canonical as flat `LayoutRow[]` with explicit parent ids, explicit frames, and optional arrangers.

Nested `UiNode` declarations are authoring convenience only.

## Placement-first rule

- **Placement uses frames**: `UI.At` (absolute) and `UI.Anchor` (edge constraints).
- **Flow uses stack**: `UI.Row` and `UI.Column` only for true sequential layout.
- **Nesting is optional sugar**.

Do not use `HSpace`/`VSpace` to place major panels on the screen.

## New M2a primitives

- `UI.Surface(...)`: root/layer-like container with independently positioned children; lowers as root row (`RootFrame`) without stack arrange by default.
- `UI.Layer(...)`: non-root independent-position container; children are not implicitly stacked.
- `UI.At(child, x, y, width, height, ...)`: lowers to `AbsoluteFrame`.
- `UI.Anchor(child, left/right/top/bottom/width/height, ...)`: lowers to `AnchorFrame`.

`Surface` width/height are authoring metadata today; pipeline render size remains authoritative.

## Example

Bad (placement-by-spacer):

```csharp
UI.Column(children:
[
    UI.VSpace(24),
    UI.Row(children: [UI.HSpace(72), StandardUI.Card(...)])
])
```

Good (placement-first):

```csharp
UI.Surface(children:
[
    UI.At(
        id: "settings-card-slot",
        x: 72,
        y: 24,
        width: 500,
        height: 292,
        child: StandardUI.Card(...))
])
```
\n\n## M3a flat authoring note\nRow-first UiDocument/UiRow authoring is canonical for top-level screens; nested UiNode trees remain optional sugar.

## M3b row-first guidance

For app and screen layout, `UiDocument` + `UiRow` is now the canonical authoring model. Use nested `UiNode` trees as optional sugar for local composition only.

Top-level screens should read as a flat table/blueprint of rows with explicit frames and parents.

`StandardView` helpers provide `UiView` metadata for rows. They are not node-tree constructors.

## M3e field-style guidance

For canonical samples, prefer explicit row composition for form fields (`email-row`, `email-box`, `email-label`) over single mega-view controls. Nested `UiNode` trees remain optional sugar and are not required for normal form composition.


## M3f control skin guidance

For checkbox/switch controls, keep flat row composition explicit and treat `StandardView.CheckboxBox`, `StandardView.SwitchTrack`, and `StandardView.SwitchThumb` as style metadata helpers only. In M3f, state is communicated by rectangular fill/border contrast and thumb position (no rounded corners yet).
\n## M4a hybrid note\nRow-hosted components are now supported: top-level placement stays flat rows, while local component internals use nested UiNode/StandardUI under a host row boundary.

## M4b note (2026-05-26)
Reference audit aligns this document with imported MachinaLayout.JS frame/stack semantics in \.
\n## M4c layout-padding hardening note\n\nM4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (AnchorFrame), rather than relying on  to move children. Stack behavior remains ordered arithmetic () and is not Flexbox.\n

## M4c layout-padding hardening note

M4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (`AnchorFrame`), rather than relying on `UiStyle.Padding` to move children. Stack behavior remains ordered arithmetic (`StackArrange`) and is not Flexbox.

## M4d note
Hosted components can expose deterministic internal ids for geometry assertions (for example button label-region, checkbox box/label, switch track/thumb).
\n- M4e note: presenter sample geometry is now validated with headless resolved-rectangle assertions; manual GUI checks are secondary.


## M4f note
M4f adds semantic-text separation and state-stable control geometry. Semantic labels are not paint; explicit text visuals emit draw text. Checkbox/switch state changes should preserve row identity/shape and adjust stable style/geometry values instead of adding/removing rows.
