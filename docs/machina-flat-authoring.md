# Machina Flat Authoring (M3a)

Machina now supports canonical row-first authoring for top-level screens.

## Canonical model

Use `UiDocument` with a flat `UiRow` table.

- Placement rows should use `Row.Absolute` or `Row.Anchor`.
- Stack children should use `Row.Fixed` or `Row.Fill` under a parent row with `Arrange`.
- Row metadata should use `UiView` helpers (`View` and `StandardView`).

Nested `UiNode` authoring remains supported, but it is optional sugar and is not the canonical top-level authoring shape.

For interactive rows, define `UiActionId` values once and reuse them across view metadata (`UiAction.Named(actionId)`) and runtime dispatch tables.

## Core APIs

- `UiDocument.Create(rows: [...])`
- `Row.Root`, `Row.Absolute`, `Row.Anchor`, `Row.Fixed`, `Row.Fill`
- `View.Rect`, `View.Text`
- `UiDocumentLowerer.Lower(UiDocument)`

## Pipeline

`MachinaRasterPipeline` supports rendering `UiDocument` directly:

- `Render(UiDocument document, int width, int height)`
- `Render(UiDocument document, MachinaRasterPipelineOptions options)`

The flat path lowers directly to the existing canonical `LayoutRow` model and does not generate wrapper rows.

## M3b hardening updates

- `UiDocumentSnapshotWriter.Write(UiDocument)` provides deterministic row-table snapshots for human and LLM inspection.
- Snapshot output includes row id, parent, order, frame details, optional arrange details, and view metadata (style/text/semantics/action).
- `StandardView` helpers are metadata builders that return `UiView`; they are not tree constructors.
- For field-style composition, use multiple rows (for example, a label row plus an input row) instead of a single mega-view.

## Canonical row-first example

```csharp
var document = UiDocument.Create(
    rows:
    [
        Row.Root("root", view: View.Rect(background: C.Surface)),

        Row.Anchor(
            id: "settings-card",
            parent: "root",
            left: 72,
            top: 24,
            width: 500,
            height: 292,
            view: StandardView.Card()),

        Row.Anchor(
            id: "title",
            parent: "settings-card",
            left: 20,
            right: 20,
            top: 20,
            height: 30,
            view: StandardView.Text("Machina Presenter", size: TextSize.Md)),
    ]);
```
\n\n### M3d text alignment\nTextStyle now includes horizontal (TextAlignX) and vertical (TextAlignY) alignment metadata. Defaults remain Left/Top for backward compatibility. Alignment only changes glyph paint origin inside the resolved text rectangle; layout geometry is unchanged. M3d does not add wrapping, ellipsis, multiline layout, baseline typography, kerning, anti-aliasing, or external font dependencies.

## M3e form-row composition

M3e hardens the presenter sample around explicit form-row composition in the flat model. Checkbox and switch controls are now authored as multiple rows (row region + control sub-row + label row content), with action metadata attached to actionable sub-parts.


## M3f control composition note

Checkbox and switch controls remain canonical flat compositions (`email-box`, `notifications-track`, `notifications-thumb`) with state expressed through rectangular fills, borders, and thumb position. No rounded corners are introduced in M3f.
\n## M4a hybrid note\nRow-hosted components are now supported: top-level placement stays flat rows, while local component internals use nested UiNode/StandardUI under a host row boundary.

## M4b note (2026-05-26)
Reference audit aligns this document with imported MachinaLayout.JS frame/stack semantics in \.
\n## M4c layout-padding hardening note\n\nM4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (AnchorFrame), rather than relying on  to move children. Stack behavior remains ordered arithmetic () and is not Flexbox.\n

## M4c layout-padding hardening note

M4c clarifies that style padding is paint metadata only. Components that host child layout (for example Card, Input text content) must create an explicit inset content region with placement rows (`AnchorFrame`), rather than relying on `UiStyle.Padding` to move children. Stack behavior remains ordered arithmetic (`StackArrange`) and is not Flexbox.
