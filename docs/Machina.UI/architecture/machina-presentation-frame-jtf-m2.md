# Machina presentation frame (JTF-M2)

## Purpose and ownership

`Machina.Presentation` owns Machina UI's backend-neutral presentation output. Its `MachinaPresentationFrame` is the immutable envelope emitted after lowering and resolved layout. It is UI intent, not a renderer command list: the assembly references only `Machina.Core`, `Machina.Layout`, and `Machina.Standard`, and has no Aurelian, Dominatus, raster, platform, or engine dependency.

The frame contains an explicit `MachinaPresentationViewport` and one deterministic ordered sequence of `MachinaPresentationOperation` values. The closed operation vocabulary is deliberately limited to:

- `FillRectangleOperation`
- `StrokeRectangleOperation`
- `PositionedTextOperation`
- `PushRectangularClipOperation`
- `PopClipOperation`

Each visual operation carries its stable Machina source identity. Geometry is resolved `Machina.Layout.Geometry.Rect`; colors are backend-neutral `Machina.Core.Styling.ColorToken`; text carries the already-resolved Machina `TextStyle` and resolved presentation color.

## Invariants

Viewport dimensions are positive. Operation geometry must be finite, strokes must be finite and positive, source identities must be nonblank, and positioned text must be nonblank and have a text style. Construction defensively copies the supplied operation sequence and exposes a read-only view. Clip pushes and pops are validated as a balanced stack, which preserves the legacy nesting and ordering semantics before any renderer sees the frame.

No broad graphics features are implied: there are no resources, images, paths, transforms, materials, gradients, pixel buffers, or backend handles.

## Canonical preparation flow

```text
Machina UI document/node
  -> lowering
  -> resolved layout
  -> hit-test index (sibling artifact)
  -> MachinaPresentationFrameBuilder.Build
  -> MachinaPresentationFrame
  -> LegacyMachinaRenderCommandAdapter (temporary)
  -> legacy Dominatus/raster path
```

`MachinaPresentationFrameBuilder` is the one semantic traversal. It walks the resolved layout tree once, resolves fill/stroke/clipping intent, runs the existing Machina rich-text layout, and emits positioned text runs. The adapter never walks UI/layout data, measures text, resolves styles, or interprets clipping; it only maps each frame operation to the corresponding legacy command and wraps the stream in legacy begin/end commands.

Text shaping, wrapping, alignment, metrics, rich-text run ordering, and run placement remain Machina work. Raster glyph realization remains outside the frame. Hit testing also remains a separate Machina result (`UiHitTestIndex`), correlated by the same stable node identities rather than embedded in presentation operations.

## Transitional compatibility

`MachinaRasterPipeline` now exposes `MachinaFrame.PresentationFrame` and routes its retained raster proof through `LegacyMachinaRenderCommandAdapter`. `MachinaRenderBridge` and `MachinaTextRenderBridge` remain source-compatible transitional APIs, but each delegates to the presentation-frame preparation and then mechanically translates the output. They are scheduled for removal or relocation with the Dominatus render path in JTF-M5.

The future `Aurelian.Machina` integration will consume this frame and translate it into Aurelian-owned renderer contracts. JTF-M2 intentionally does not implement that consumer bridge or move CPU/Vulkan realization.

## Cost and evidence

Preparation traverses the resolved layout tree once and allocates one operation list plus the frame's defensive array copy. The legacy adapter allocates one legacy command list but does not repeat layout traversal or text layout. No cache, pooling, or mutable builder was introduced because fast-loop validation showed no material regression.

Fast structural tests prove viewport and operation validation, immutable storage, order, nested clips, identities, rich/primitive text colors, adapter translation, pipeline exposure, and the absence of legacy/backend assembly references. Existing pipeline and raster tests continue to prove deterministic representative raster output and hit-test behavior.
