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

- M5c update: StandardTheme now carries typed component style records and supports explicit root theme handoff with `with` customization.


## M5c1 authoring pattern

```csharp
var buttonStyle = theme.Button.Default with
{
    Background = ColorToken.Hex(0x18181BFF),
    Foreground = ColorToken.White,
};

var cardStyle = theme.Card.Default with
{
    ContentInset = 18,
};
```

```csharp
StandardUI.Button("Save", action: Actions.Save.ToAction(), style: buttonStyle);
StandardUI.Card(id: "settings", style: cardStyle, child: UI.Text("Body"));
```


## M5c2 note

Input style records are now fully wired. Input content positioning remains explicit (`AnchorFrame` via `*.content`) and does not rely on style padding.


## M5c3 Checkbox and Switch style wiring

M5c3 fully wires `StandardCheckboxStyle` and `StandardSwitchStyle` into `StandardUI.Checkbox` and `StandardUI.Switch`. Checkbox and switch geometry, visual style, gap spacing, and label text style now resolve deterministically from the selected style record (`style:` if supplied, otherwise theme default). Checked/on state changes values (for example mark fill and thumb X) without changing row identity.


- M5c4 clarification: use StandardUI as the default component surface and pass an explicit root theme; use StandardView for lightweight flat-row metadata when that authoring style is preferred.

## M5d contract cleanup note
Superseded guidance: app-level manual decomposition of standard checkbox/switch internals is no longer canonical. Prefer `StandardUI.Checkbox`/`StandardUI.Switch` in app/component code; use `StandardView` sub-parts only for advanced custom composition. See `docs/standard-ui-vs-standard-view-m5d.md`.



## M5e headless harness
M5e standardizes component/document headless assertions through tests/Machina.Testing/GeometryHarness.cs so component tests can assert resolved rectangles, row presence, metadata, and hit targets without repeating lowering/resolve plumbing.

- M5f update: presenter sample is the canonical reference app and is contract-tested in tests/Machina.Presenter.Sample.Tests (document shape, hosted component boundary, localized StandardUI internals, plain C# dispatch, theme propagation, and geometry/hit-target stability).

## M6a Machina.Text boundary note

M6a establishes `Machina.Text` as a separate subsystem contract. Frame/stack/table layout still places component rectangles; `Machina.Text` will lay out text only inside those assigned boxes.

Wrap, overflow, leading, block/list spacing, and text alignment are text-domain primitives and must not be added to general layout semantics.

Headings remain a component/layout responsibility (for example title variant selection in `StandardUI.Card`), not a supported inline markup mechanism inside restricted Machina text source.

The current simple `UI.Text` path is transitional until `Machina.Text` parser/model/layout integration milestones (M6b+) are complete.

## Standard rich text authoring (M6b)

Rich text authoring is now modeled under `Machina.Standard.Text` rather than as a Core primitive or standalone production package. Standard owns this surface because typography variants and label/title/caption policies are tied to Standard components and themes.

The existing `Machina.Core.Authoring.UI.Text(string)` remains a simple primitive/transitional path. M6b adds typed Standard helpers such as `Text.Plain(...)`, `Text.Markup(...)`, `Text.Paragraph(...)`, `Text.BulletList(...)`, `Text.Strong(...)`, `Text.Emphasis(...)`, `Text.Code(...)`, and `Text.Link(...)`, but does not add a rendered rich text container yet.

Restricted markup supports paragraphs, bullet lists, strong/emphasis/code/link inline runs, and explicit diagnostics. Markdown headings remain forbidden; component authors should use Standard typography variants for titles instead of source-level heading syntax.

## Standard text layout contract (M6c)

M6c adds a headless layout layer for that Standard text model.

- `MachinaTextLayoutEngine` consumes a parsed document or a `MachinaTextSpec`.
- The caller must still supply the assigned text box from general layout.
- The result is `MachinaTextLayoutResult`, which contains deterministic content bounds, line boxes, run boxes, overflow flags, and diagnostics.

This does not replace current primitive `UI.Text` rendering yet. It establishes the data contract that future M6d renderer integration will consume.

## Standard text render bridge proof (M6d)

M6d now adds that renderer bridge proof, but only as a narrow seam:

- authored Standard rich text can be laid out headlessly
- a render-layer adapter can emit `DrawTextCommand` values from the layout result
- primitive `UI.Text` remains the active default authoring/rendering path for existing controls

This keeps authoring ownership clear:

- frames place text boxes
- `Machina.Standard.Text` lays out text inside those boxes
- bridge/render layers emit commands

No broad StandardUI migration is implied by M6d.

## M5g screen/component/action convention

Presenter-style samples should keep top-level screen placement separate from component composition:

- screens build `UiDocument` row tables and own explicit placement frames;
- components build localized `UiNode` subtrees with `StandardUI`/`UI`;
- actions live in a screen/domain contract and dispatch references that contract;
- theme is an explicit argument handed from screen to component to child `StandardUI` controls.

For card content, prefer `StandardUI.Card(theme: theme, gap: 10, children: [...])` over `StandardUI.Card(child: UI.Column(...))`. This preserves the hosted component boundary while keeping local composition boring and reviewable.
