# Machina.Standard.Text Layout M6c

## Purpose

M6c lands a deterministic, renderer-independent text measurement and layout engine under `Machina.Standard.Text`.

This milestone is layout-only. It measures and arranges rich text content inside an already-assigned rectangle and returns stable layout boxes for future consumers.

## Boundary with general layout

General Machina layout still owns component placement.

- Frames and stack/grid layout assign the text box.
- `Machina.Standard.Text` lays out content only inside that assigned box.
- Text layout does not place components, resize parent layout, or mutate frame resolution.

M6c does not render, does not migrate `UI.Text`, and does not change StandardUI control implementations.

## Inputs

M6c supports:

- `MachinaTextSpec` plus assigned `MachinaTextBox`
- `MachinaTextDocument` plus explicit `MachinaTextPolicy` and assigned `MachinaTextBox`
- deterministic `IMachinaTextMeasurer`
- adapter usage through existing Core `ITextMeasurer` when needed

`MachinaTextSpec` flows through the existing parser first, and parse diagnostics are preserved on the layout result.

## Layout result model

The engine returns `MachinaTextLayoutResult`:

- `Box`
- `ContentBounds`
- `Lines`
- `Runs`
- `HasOverflow`
- `Diagnostics`
- `ParseDiagnostics`

Line and run geometry is expressed with stable `MachinaTextBox` values and contains no Avalonia, presenter, window, or raster types.

## Measurement policy

M6c still uses deterministic Standard variant metrics for line-height policy:

- `Body`: font size `14`, default leading `1.4`
- `Label`: font size `12`, default leading `1.3`
- `Caption`: font size `11`, default leading `1.25`
- `Title`: font size `18`, default leading `1.25`
- `Mono`: font size `12`, default leading `1.35`

From M6d onward, the default `MachinaTextMeasurers.Deterministic` implementation shares the same deterministic bitmap measurement seam as:

- `DeterministicTextMeasurer`
- `ReadableBitmapTextRasterizer.MeasureText(...)`

That means Standard text width/height measurement now follows current renderer bucket reality:

- `Title` -> `TextSize.H1`
- `Body` -> `TextSize.Md`
- `Label` -> `TextSize.Sm`
- `Caption` -> `TextSize.Sm`
- `Mono` -> `TextSize.Sm`

Line height remains Standard-owned policy (`fontSize * leading`) and is intentionally separate from glyph pixel height for now.

Leading resolves as:

- `Tight` -> `1.15`
- `Normal` -> variant default
- `Loose` -> `1.6`
- numeric -> exact supplied value

Line height in pixels is `fontSize * resolvedLeading`.

## Paragraph layout

Paragraphs flatten inline runs into styled text fragments, then produce deterministic line boxes inside the assigned text box.

- `Wrap.None` produces one line per paragraph and reports overflow when width exceeds the box.
- `Wrap.Word` wraps at whitespace boundaries when possible.
- Tokens that cannot fit on a line are placed on their own line and still report overflow.
- Empty paragraphs still produce one deterministic line box with the resolved line height.

Line bounds use actual measured text width rather than full-box width.

## Inline runs

M6c preserves run metadata for:

- plain text
- strong
- emphasis
- code
- link

`Strong` and `Emphasis` currently preserve metadata only. `Code` switches the run metadata/measurement variant to mono. `Link` preserves `href` metadata and lays out its child text normally.

## Bullet lists

Bullet lists are supported as deterministic block flow.

- Each item uses a bullet marker run.
- Nested items indent by `16` pixels per depth level.
- Wrapped continuation lines align to the post-marker text start for that item.
- `ListGap` is applied between sibling list items.

This is intentionally baseline list layout, not a full markdown/CSS list engine.

## Alignment and vertical alignment

Per-line horizontal alignment supports:

- `Start`
- `Center`
- `End`

Block pack vertical alignment supports:

- `Top`
- `Center`
- `Bottom`

Alignment is computed after measurement using the assigned box and the final content height. If content overflows the box, alignment still resolves deterministically and overflow is reported.

## Overflow behavior

M6c implements overflow reporting, not clipping/render behavior.

`HasOverflow` becomes true when:

- line width exceeds the available box width
- content height exceeds the box height
- the assigned box has non-positive width or height
- an unsplittable token cannot fit

Diagnostics include:

- `BoxTooSmall`
- `ContentOverflow`
- `UnsupportedInline`
- `UnsupportedOverflow`

`Ellipsis` and `Scroll` are not implemented in M6c; they currently degrade to clip-style reporting with diagnostics.

## Deferred features

M6c intentionally defers:

- renderer integration
- `UI.Text` migration
- StandardUI control migration
- draw command changes
- raster bridge changes
- shaping/kerning/font backend work
- ellipsis behavior
- scroll behavior
- dynamic font sizing
- CSS-like cascade

## Tests

Headless tests now cover:

- no-wrap geometry
- whitespace word wrapping
- width and height overflow
- horizontal and vertical alignment
- block gap
- leading variants
- bullet list geometry
- inline style metadata preservation
- parser-backed spec layout
- determinism

These tests live in `tests/Machina.Standard.Tests/Text/MachinaTextLayoutTests.cs`.

## M6d update

M6d now lands that bridge proof in `Machina.Dominatus.Rendering.Bridge.MachinaTextRenderBridge`.

- `Machina.Standard.Text` still owns layout only.
- The bridge consumes `MachinaTextLayoutResult` and emits renderer-facing `DrawTextCommand` values.
- `UI.Text` and existing StandardUI controls are still not migrated.

See `docs/machina-standard-text-render-bridge-m6d.md` for the measurement audit and bridge contract.

## M6e update

M6e now proves that the M6c layout engine can drive a real Standard component.

- `StandardUI.TextBlock` receives an assigned box from normal layout.
- `MachinaTextLayoutEngine` lays out text inside that box.
- The presenter sample includes one controlled visible probe.

General layout semantics remain unchanged.
