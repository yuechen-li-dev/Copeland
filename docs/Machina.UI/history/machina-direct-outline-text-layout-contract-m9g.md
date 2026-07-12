# Machina Direct-Outline Text Layout Contract M9g

## Purpose

M9g defines the proof-level UI text layout contract for `DirectOutlineStatic`.

The goal is deterministic text-in-rect layout for gallery/tooling proof work:

- given text, font face, font size, UI rect, padding, alignment, line-height policy, and clipping policy
- return line boxes, baselines, glyph placements, ink bounds, content bounds, and a rendered proof image

This milestone does not switch production UI text rendering.
It extends the direct-outline proof path only.

## Rect model

M9g formalizes two rectangles:

- `OuterRect`: the full UI box passed by the caller
- `ContentRect`: `OuterRect` minus `DirectOutlineTextPadding`

Text alignment is always resolved inside `ContentRect`.

The proof API returns both rectangles in `DirectOutlineTextBoxLayoutResult`.

## Font metrics

The layout contract uses font-level ascent, descent, line gap, and units-per-em when the outline source can provide them.

For Typography-backed proof fonts:

- `TypographyGlyphOutlineSource` now implements `IDirectOutlineFontMetricsSource`
- ascent comes from the font ascender scaled to the requested font size
- descent comes from the absolute value of the font descender scaled to the requested font size
- line gap comes from the font line gap scaled to the requested font size

If a proof source cannot provide those metrics, M9g falls back to a stable temporary policy:

- ascent = `0.8 * fontSize`
- descent = `0.2 * fontSize`
- line gap = `0`

That fallback is explicit in diagnostics and exists only to keep proof layout deterministic without arbitrary visual nudges.

## Line height

M9g supports two line-height modes:

- `DirectOutlineLineHeightMode.FontMetrics`
- `DirectOutlineLineHeightMode.Explicit`

Default line height is:

`ascent + descent + lineGap`

Explicit line height is proof-only caller input for controlled gallery/tooling cases.

## Baseline rules

Baseline placement is deterministic and derived from `ContentRect` plus font metrics.

Top alignment:

- first baseline = `contentTop + ascent`

Middle alignment:

- compute the full line block height
- center that block inside `ContentRect`
- first baseline = `blockTop + ascent`

Bottom alignment:

- align the last line descent to `contentBottom`
- first baseline = `contentBottom - blockHeight + ascent`

Baseline alignment:

- M9g currently treats `Baseline` as a stable proof-only mode equivalent to `Top`
- first baseline = `contentTop + ascent`

This is intentional for M9g because the proof API does not yet accept an external baseline anchor.

## Horizontal alignment

Each explicit line uses the existing direct-outline glyph metrics and pair-adjustment path to compute its advance width.

Per-line X placement is:

- left: `contentLeft`
- center: `contentLeft + (contentWidth - lineWidth) / 2`
- right: `contentRight - lineWidth`

## Vertical alignment

The full explicit-line block is aligned inside `ContentRect`.

Supported modes:

- `Top`
- `Middle`
- `Bottom`
- `Baseline`

No arbitrary pixel offsets or visual fudge factors are used.

## Multi-line behavior

M9g supports explicit newline layout.

- input text is split on explicit `\n`
- each explicit line gets its own line box and baseline
- line spacing follows the selected line-height mode

Word wrapping is deferred in M9g.

That means:

- explicit multi-line proof layout is supported
- automatic wrapping inside content width is not part of this milestone

## Clipping behavior

M9g supports:

- `DirectOutlineTextClipMode.None`
- `DirectOutlineTextClipMode.ClipToContentRect`

Clipping is proof-side pixel clipping after rasterization.

- glyph placement and ink bounds are still computed from the full positioned outline geometry
- when clipping is enabled, rendered pixels outside `ContentRect` are removed
- `WasClipped` reports whether the unclipped ink extended beyond `ContentRect`

## Gallery proof

The component gallery now has a second opt-in direct-outline section:

- `Direct Outline Text Box Layout Proof`

It renders proof-only layout cases for:

- labels with left/center/right alignment
- centered button labels at small/medium/large sizes
- settings rows with visible baseline alignment
- cards with title/body padding
- explicit multi-line body text
- long-label clipping
- an alignment grid with bounds and baseline guides enabled

New gallery/tooling artifacts include:

- `artifacts/m9g/component-gallery-direct-outline-text-layout-proof.png`
- `artifacts/m9g/direct-outline-text-box-layout-proof.png`
- `artifacts/m9g/direct-outline-text-alignment-grid.png`
- `artifacts/m9g/font-diagnostic-export-manifest.txt`
- `artifacts/m9g/font-diagnostic-export-manifest.json`

## What changed

- added `DirectOutlineTextBoxOptions`, layout records, alignment enums, padding, clip mode, and line-height mode in `Machina.Fonts.ReferenceRendering`
- added `DirectOutlineTextBoxLayouter`
- added `DirectOutlineTextBoxRenderer`
- added Typography-backed font metrics loading for direct-outline proof layout
- added opt-in component-gallery text-layout proof export and standalone crops
- added tests for content rects, line height, alignment, clipping, and explicit newline behavior

## What did not change

- production UI text rendering behavior is unchanged
- `Standard.Text` semantics are unchanged
- `Machina.Core` document-model semantics are unchanged
- `Machina.Layout` resolver behavior is unchanged
- MSDF generation, sampling, reconstruction, and experimental/scalable policy are unchanged in M9g
- no browser oracle work landed here
- no Aurelian/Vulkan integration landed here

## Future production integration path

M9g is the bridge from readable direct-outline proof rendering to future production integration work.

If production UI text later adopts this contract, the next milestone should:

- choose the real runtime integration point deliberately
- decide how baseline anchoring should interact with real controls
- add wrapping rules, truncation rules, and richer clipping behavior where needed
- prove parity on real StandardUI control labels and `TextBlock` surfaces

## Deferred work

- automatic word wrapping
- external baseline-anchor input for true baseline-relative control layout
- richer clip/truncation policies
- production renderer integration
- MSDF production/scalable decisions

## M9h follow-up

M9h keeps the M9g layout contract intact and adds a renderer-facing bridge that maps UI-ish text intent into that contract.

- `StaticTextRenderRequest` now owns the renderer-facing request shape
- `DirectOutlineStaticTextRenderBridge` maps that request into `DirectOutlineTextBoxOptions`
- gallery proof work can now exercise the bridge without changing the M9g layout algorithm
- production UI text behavior remains unchanged

See `docs/Machina.UI/history/machina-direct-outline-render-bridge-m9h.md`.

## M9i follow-up

M9i keeps this layout contract unchanged and uses it through the presenter proof bridge path.

- presenter proof remains opt-in
- `DirectOutlineStatic` remains the static/reference path
- word wrapping stays deferred
- production integration stays deferred
