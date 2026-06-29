# Machina Direct-Outline Static Text M9d

## Purpose

M9d formalizes the text backend split inside the proof/tooling stack.

Direct-outline rasterization is now the default static/UI-text proof backend.
MSDF remains available only as an explicit scalable/experimental path.

No production UI text renderer changed in this milestone.

## Why direct-outline is the static default

- direct-outline rasterized text is visually correct and stable
- browser horizontal kerning is not the target oracle
- Machina's own direct-outline kerning is acceptable and may be preferable
- MSDF output still drifts visually and numerically, especially at larger sizes

That makes direct-outline the right default for ordinary static proof work.

## Render strategies

M9d introduces stable strategy names:

- `DirectOutlineStatic`
- `MsdfScalableExperimental`

## DirectOutlineStatic

`DirectOutlineStatic` renders from Machina/Typography outlines directly.

It uses existing metrics and pair adjustments, supports deterministic supersampling, returns glyph placements and ink bounds, and does not use MSDF generation, atlas packing, or `.dfpage` artifacts.

## MsdfScalableExperimental

`MsdfScalableExperimental` remains available for explicit comparison work and future zoomable/transform-heavy text scenarios.

M9d does not attempt to repair MSDF.

## Diagnostic tooling behavior

`Machina.Fonts.Tooling` now treats direct-outline as the internal geometry reference.

- `cad-debug` defaults to direct-outline static imagery
- `direct-vs-msdf` compares `DirectOutlineStatic` against `MsdfScalableExperimental`
- `msdf-debug` stays explicit MSDF-only diagnostic mode
- manifests and reports record strategy names

## What changed

- explicit text render strategies
- stable `DirectOutlineStaticTextRenderer` API
- direct-outline promoted to the default static diagnostic backend
- MSDF relabeled as scalable/experimental in presets and reports
- manifests record backend policy

## What did not change

- no MSDF generation/sampling/smoothing fix
- no Typography outline extraction semantic change
- no Standard text semantic change
- no production `Machina.Standard` or raster text integration by default
- no browser oracle repair

## Future production integration path

If production text integration follows, it should build on the M9d direct-outline API and remain explicitly opt-in until a separate runtime milestone chooses that path.

## M9e follow-up

M9e uses the M9d API as a sample-only proof bridge in `samples/Machina.ComponentGallery.Sample`.

- the gallery can now render real UI-ish strings through `DirectOutlineStaticTextRenderer`
- the proof path is opt-in with `--include-direct-outline-text-proof`
- default production UI text behavior stays unchanged
- MSDF remains explicit experimental/scalable, including when shown beside direct-outline in proof-only comparison panels

See `docs/machina-direct-outline-text-proof-m9e.md`.

## M9g follow-up

M9g keeps the M9d backend policy intact and adds a proof-level text box/layout contract on top of `DirectOutlineStatic`.

- direct-outline remains the default static/reference backend
- font metrics, line boxes, baselines, padding, alignment, clipping, and explicit newline layout are now formalized for proof work
- production UI text behavior remains unchanged

See `docs/machina-direct-outline-text-layout-contract-m9g.md`.

## Future MSDF repair path

MSDF repair remains a separate milestone focused on placement drift, larger-size mismatch growth, and scalable-text use cases.

## M9f follow-up

M9f is that first real repair milestone.

- `DirectOutlineStatic` stays the geometry oracle.
- `MsdfScalableExperimental` keeps its explicit experimental/scalable label.
- the repaired MSDF path now scales field resolution with em size in proof/export workflows and uses a texel-center UV sampling contract.
- no direct-outline geometry was changed to match broken MSDF output.
- no production UI text default changed.

## M9h follow-up

M9h keeps the M9d backend policy intact and adds a renderer-facing bridge contract on top of the M9g layout API.

- `DirectOutlineStatic` remains the static/reference backend.
- renderer/proof hosts can now describe UI-ish text intent through `StaticTextRenderRequest`.
- the bridge lives in `Machina.Fonts.ReferenceRendering`, not in `Machina.Fonts.Tooling`.
- production UI text behavior remains unchanged.

See `docs/machina-direct-outline-render-bridge-m9h.md`.

## M9i follow-up

M9i keeps the M9d backend policy intact and uses it as the phase-closeout golden path.

- `DirectOutlineStatic` remains the static/reference backend
- presenter and gallery bridge proofs are both opt-in
- MSDF remains explicit experimental/scalable
- production defaults remain unchanged
