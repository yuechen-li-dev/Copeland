# Machina Flat Authoring (M3a)

Machina now supports canonical row-first authoring for top-level screens.

## Canonical model

Use `UiDocument` with a flat `UiRow` table.

- Placement rows should use `Row.Absolute` or `Row.Anchor`.
- Stack children should use `Row.Fixed` or `Row.Fill` under a parent row with `Arrange`.
- Row metadata should use `UiView` helpers (`View` and `StandardView`).

Nested `UiNode` authoring remains supported, but it is optional sugar and is not the canonical top-level authoring shape.

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
