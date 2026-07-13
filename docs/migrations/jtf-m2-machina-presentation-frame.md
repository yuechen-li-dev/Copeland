# JTF-M2 — Machina backend-neutral presentation frame

## Status

Completed. Machina UI now has a narrow immutable presentation frame without Aurelian, Dominatus, raster, platform, or game-engine types.

## Delivered boundary

`src/Machina.UI/Machina.Presentation` contains `MachinaPresentationFrame`, `MachinaPresentationViewport`, five closed operation types, `MachinaTextPresentationBuilder`, and the sole `MachinaPresentationFrameBuilder` lowering traversal. It depends only on `Machina.Core`, `Machina.Layout`, and `Machina.Standard` and is included with matching fast tests in `Machina.UI.slnx` and `JointTaskForce.slnx`.

The canonical production route is now:

```text
lowering + resolved layout -> MachinaPresentationFrameBuilder -> presentation frame
  -> LegacyMachinaRenderCommandAdapter -> legacy Dominatus commands -> existing raster output
```

The pipeline returns the presentation frame beside resolved layout and hit testing. Hit testing remains a sibling Machina artifact and no input-routing work moved into this milestone.

## Compatibility and retirement

`Machina.Dominatus.Rendering.Bridge.LegacyMachinaRenderCommandAdapter` is a deliberately mechanical compatibility adapter. `MachinaRenderBridge` and `MachinaTextRenderBridge` remain documented compatibility APIs and delegate to presentation preparation rather than retaining a second semantic traversal. This code is retained only until JTF-M5 retires the Dominatus render route; the eventual production consumer is `Aurelian.Machina` after the JTF-M3 backend consolidation.

## Equivalence evidence

Fast tests cover representative fills, strokes, primitive and rich positioned text, mixed ordered operations, nested clips, stable identities, adapter command equivalence, pipeline output, hit testing, and retained deterministic raster output. The legacy adapter produces identical begin/fill/stroke/text/clip/end command values for a frame; existing `MachinaRasterPipeline` tests continue to compare deterministic PPM output across repeated representative documents.

Validation also checks that the frame assembly has no Dominatus, raster, or Aurelian references. Dependency-boundary validation needs no new exception because the new project has only same-subsystem inward references.

## Deliberate non-goals

This milestone did not introduce Aurelian references or the `Aurelian.Machina` bridge, move raster/Vulkan projects, alter renderer command ownership, add general resources or graphics features, redesign text layout/hit testing/input, or intentionally change visible output.

## JTF-M3 handoff

JTF-M3 should move concrete CPU raster realization and renderer ownership into Aurelian while preserving `MachinaPresentationFrame` as the producer output. It should create the first Aurelian-owned consumer seam, prove CPU pixel parity, and remove Aurelian.Core's concrete graphics/backend coupling without broadening Machina's presentation vocabulary.
