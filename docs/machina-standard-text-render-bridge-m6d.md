# Machina.Standard.Text Render Bridge M6d

## Purpose

M6d lands a proof bridge from renderer-independent `MachinaTextLayoutResult` data into existing `DrawTextCommand` output.

This milestone is intentionally narrow:

- `Machina.Standard.Text` still owns parsing, policy, and layout only.
- Dominatus/render integration owns draw-command emission.
- Existing primitive `UI.Text` and current StandardUI controls remain on their existing paths.
- No broad migration is attempted in M6d.

## Measurement consistency audit

M6d starts with a measurement audit because M5h already fixed a real text clipping bug caused by layout and renderer disagreement. The bridge is only safe if Standard text measurement matches the same deterministic bitmap reality the raster renderer uses today.

Audit result:

- `MachinaTextMeasurers.Deterministic`
- `DeterministicTextMeasurer`
- `ReadableBitmapTextRasterizer.MeasureText(...)`

now agree exactly for the representative strings covered by tests in this milestone.

## Measurement paths found

1. `Machina.Standard.Text.IMachinaTextMeasurer`
   Standard text layout measures run widths through this interface.
2. `MachinaTextMeasurers.Deterministic`
   Standard-owned default measurer used by `MachinaTextLayoutEngine` when no explicit measurer is supplied.
3. `MachinaTextMeasurers.FromCore(ITextMeasurer)`
   Adapter path that lets Standard text layout consume the existing Core measurement seam.
4. `Machina.Core.Measurement.DeterministicTextMeasurer`
   Existing Core deterministic measurer used by `UiLowerer` and current primitive text/control paths.
5. `Machina.Renderer.Raster.Text.ReadableBitmapTextRasterizer.MeasureText(...)`
   Bitmap renderer-side measurement assumption used by raster-focused tests and live draw behavior.
6. StandardUI local helpers
   Current StandardUI controls still rely on Core text styles plus `DeterministicTextMeasurer` through existing lowering and button/control sizing paths. No separate fourth measurement algorithm was found in StandardUI control code.

## Agreement / mismatch findings

Agreement after M6d changes:

- `Increment`
- `Machina Presenter`
- `Count: 12`
- `Email updates: on`
- `Hello world`
- `Hello  world`
- `code_value`
- punctuation/caption representative text

all measure identically across:

- Standard deterministic text measurement
- Core deterministic measurement adapter
- bitmap raster measurement

Important nuance:

- Standard layout line height still uses Standard variant metrics (`Body 14`, `Label 12`, `Caption 11`, `Title 18`, `Mono 12`) plus leading policy.
- Rendered glyph size still uses the existing renderer bucket reality (`TextSize.H1`, `TextSize.Md`, `TextSize.Sm`).

That separation is intentional for M6d. Width agreement is what protects wrapping/overflow and avoids reintroducing the M5h clipping bug. Full typographic baseline/font-backend fidelity remains deferred.

## Bridge architecture

Ownership stays split:

- `Machina.Standard.Text`
  owns `MachinaTextSpec`, parser/model, policy, measurement interface, and `MachinaTextLayoutResult`
- `Machina.Dominatus.Rendering.Bridge`
  owns `MachinaTextRenderBridge` and `MachinaTextRenderStyle`
- existing raster renderer
  continues to consume `DrawTextCommand`

M6d adds:

- `MachinaTextRenderBridge.ToDrawTextCommands(...)`
- `MachinaTextRenderStyle`

The bridge accepts:

- a stable id prefix
- a `MachinaTextLayoutResult`
- current renderer-compatible style mapping inputs

and emits deterministic `DrawTextCommand` instances in visual line/run order.

## Render command mapping

Each visible `MachinaTextRunBox` maps to one `DrawTextCommand`:

- `text` -> run text
- `rect` -> exact run bounds from layout result
- `style.color` -> bridge base color or link color override
- `style.size` -> mapped renderer bucket
- `style.align` -> forced `Left/Top` so run bounds remain authoritative

Current size mapping:

- `Title` -> `TextSize.H1`
- `Body` -> `TextSize.Md`
- `Label` -> `TextSize.Sm`
- `Caption` -> `TextSize.Sm`
- `Mono` -> `TextSize.Sm`

Whitespace-only runs are skipped because they have no visible ink output in the current renderer.

`DrawTextCommand` is sufficient for this proof because it already carries:

- text
- rect
- color
- size
- alignment metadata

Known gap:

- it does not carry rich inline-style metadata, clip rect, or separate font backend identity
- M6d therefore preserves rich inline metadata in layout results but does not try to encode all of it into draw commands

## Supported visual behavior

M6d supports:

- deterministic text draw-command emission from Standard rich text layout
- exact run-box geometry handoff
- link color override if the caller provides one
- mono/code runs mapped to current renderer-supported small text bucket
- overflow reporting from layout without hidden bridge-side clipping logic

## Deferred visual behavior

Still deferred:

- replacing primitive `UI.Text`
- migrating StandardUI labels/buttons/checkboxes/switches broadly
- renderer-specific bold/italic/link-decoration styling
- clip rect emission for Standard rich text overflow
- ellipsis/scroll rendering
- baseline-aware typography
- shaping/kerning/font backend work
- dynamic font sizing

Renderer support for inline styles today:

- `strong` and `emphasis` metadata are preserved in layout only
- `code` is preserved and mapped to current mono-size bucket
- `link` metadata is preserved and may affect color only if the caller chooses

## Tests

Added/updated proof coverage:

- `tests/Machina.Standard.Tests/Text/MachinaTextMeasurementAuditTests.cs`
  verifies Standard/Core/raster measurement agreement and proves layout widths match renderer measurement reality
- `tests/Machina.Dominatus.Tests/MachinaTextRenderBridgeTests.cs`
  verifies draw-command emission, run-bound geometry, determinism, order preservation, inline-style tolerance, and overflow behavior

The representative measurement audit includes:

- `Increment`
- `Machina Presenter`
- `Count: 12`
- `Email updates: on`
- `Hello world`
- `Hello  world`
- `code_value`

## M6e migration plan

M6e should build on this proof incrementally rather than replacing the current text stack wholesale.

Recommended next steps:

1. Introduce one controlled Standard text surface for real authored rich text content.
2. Adopt the M6d bridge in that controlled surface only.
3. Migrate selected StandardUI text consumers one class at a time with measurement parity tests.
4. Revisit richer visual styling only after renderer command and backend requirements are explicit.

M6d therefore proves the seam without silently changing primitive `UI.Text` or existing StandardUI controls.

## M6e follow-through

M6e is now the first real consumer of this bridge.

- `StandardUI.TextBlock(...)` is the first Standard-owned rich text component.
- Dominatus now consumes Standard rich text metadata, lays it out in assigned bounds, and emits existing `DrawTextCommand` output.
- Primitive `UI.Text` remains intact.
- Broad Standard control migration is still deferred.

See `docs/machina-standard-textblock-m6e.md` and `docs/machina-standard-textblock-local-visual-audit-m6e.md`.
