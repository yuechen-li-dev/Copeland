# Machina Direct-Outline Render Bridge M9h

## Purpose

M9h defines a renderer-facing static text contract for the direct-outline proof path.

It does not switch production UI rendering.
It adds a bridge that lets a renderer or proof host describe UI-ish text intent and choose `DirectOutlineStatic` without making core UI packages depend on tooling.

## Why a bridge contract exists

M9d proved that `DirectOutlineStatic` can render stable static text.
M9g proved that direct-outline text can be laid out deterministically inside UI rectangles.

M9h answers the next seam question:

- how a renderer describes text intent
- how a host chooses the direct-outline backend
- how to keep `Machina.Standard` and `Machina.Core` free of `Machina.Fonts.Tooling`

The contract lives on the renderer/proof side, not in production UI semantics.

## Dependency direction

Target direction:

```text
UI/component layer
  -> describes text intent

Renderer/proof host
  -> maps intent into a backend choice

Machina.Fonts
  -> provides DirectOutlineStatic layout/rendering primitives

Machina.Fonts.Tooling
  -> provides diagnostics, layers, manifests, exports
```

M9h chooses Option A:

- the bridge contract lives in `src/Machina.Fonts/ReferenceRendering`

This keeps the abstraction dependency-light, proof-friendly, and reusable by samples without adding a dependency from `Machina.Core` or `Machina.Standard` to `Machina.Fonts.Tooling`.

## Static text render request

M9h adds `StaticTextRenderRequest`.

Key fields:

- `Text`
- `FontFaceId`
- `Rect`
- `FontSize`
- `Padding`
- `HorizontalAlignment`
- `VerticalAlignment`
- `LineHeightMode`
- `ExplicitLineHeight`
- `ClipMode`
- `UsePairAdjustments`
- `Supersample`
- `Weight`
- `Slant`
- `DebugLabel`

The request validates:

- non-empty text
- finite non-negative rect geometry
- positive finite font size
- valid explicit line-height input when requested
- supported supersample levels

## Static text render result

M9h adds `StaticTextRenderResult`.

It returns:

- the original `Request`
- `DirectOutlineTextBoxLayoutResult`
- rendered `RgbaImage`
- `InkBounds`
- `WasClipped`
- `Glyphs`
- `Diagnostics`

The result is renderer-facing and proof-facing.
It does not require the caller to know atlas pages, MSDF reconstruction details, or glyph internals ahead of time.

## Mapping to DirectOutlineTextBoxLayout

`DirectOutlineStaticTextRenderBridge` is the adapter.

Responsibilities:

- validate `StaticTextRenderRequest`
- map bridge enums into M9g direct-outline layout enums
- map padding/alignment/line-height/clipping into `DirectOutlineTextBoxOptions`
- reuse `DirectOutlineTextBoxRenderer`
- return layout, glyph placements, clipping state, diagnostics, and a rendered image

Important detail:

- the bridge renders into a local image sized from `Rect.Width` and `Rect.Height`
- the request `Rect` remains the host placement intent
- the returned layout is the local proof/layout contract used to generate that image

M9h reuses the M9g algorithm rather than duplicating it.

## Gallery proof

The component gallery now has an opt-in M9h section:

- `DirectOutlineStatic Render Bridge Proof`

Flag:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m9h -IncludeDirectOutlineRenderBridgeProof
```

Artifacts:

- `artifacts/m9h/component-gallery-direct-outline-render-bridge-proof.png`
- `artifacts/m9h/direct-outline-render-bridge-proof.png`
- `artifacts/m9h/direct-outline-render-bridge-layout-grid.png`
- `artifacts/m9h/font-diagnostic-export-manifest.txt`
- `artifacts/m9h/font-diagnostic-export-manifest.json`

The proof covers:

- label
- centered button
- settings row
- card title/body
- clipped long label
- alignment/layout grid

Default gallery export remains unchanged.

## What changed

- added `StaticTextRenderRequest`
- added `StaticTextRenderResult`
- added `DirectOutlineStaticTextRenderBridge`
- added bridge-focused gallery proof/export wiring
- added bridge validation/mapping/rendering tests
- added dependency-boundary tests

## What did not change

- no production default text renderer switch
- no `Standard.Text` semantic change
- no `Machina.Core` document-model change
- no dependency from production packages to `Machina.Fonts.Tooling`
- no new dependency from `Machina.Core` to `Machina.Fonts`
- no MSDF generation/sampling/reconstruction change
- no Typography outline extraction change

`DirectOutlineStatic` remains the static/reference path.
MSDF remains explicit experimental/scalable.

## Future presenter integration path

M9i now proves this path in `samples/Machina.Presenter.Sample`.

- the presenter sample accepts `--include-direct-outline-render-bridge-proof`
- the opt-in proof card composites bridge-rendered text into a presenter-style sample surface
- `.\tools\Export-MachinaPresenter.ps1` writes a deterministic presenter proof PNG

The host still translates text intent into `StaticTextRenderRequest`, chooses `DirectOutlineStaticTextRenderBridge`, and composites the returned image explicitly.

That choice remains host-owned.
The component layer does not need to know about proof tooling.

## Future production integration path

If production UI text later adopts this bridge, that should be a separate milestone that:

- chooses the runtime integration point deliberately
- proves host-side compositing and caching behavior
- decides wrapping/truncation semantics
- validates StandardUI controls through the real renderer path

M9h is not that switch.

## Deferred work

- production presenter integration
- production default renderer adoption
- automatic wrapping and truncation policy
- richer baseline-anchor input
- MSDF production/scalable decisions

## M9i closeout

M9i is the proof-integration and hygiene closeout step for this font phase.

- presenter proof is now covered beside the gallery proof
- canonical commands and artifact locations are documented in `docs/machina-font-phase-closeout-m9i.md`
- production UI text defaults still do not change
