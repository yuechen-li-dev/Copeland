# Aurelian.Machina bridge — JTF-M3c

## Ownership and dependencies

`src/Integrations/Aurelian.Machina` is consumer-owned integration code. Its only production references are `Machina.Presentation` and `Aurelian.Rendering.Contracts`:

```text
Aurelian.Machina -> Machina.Presentation
Aurelian.Machina -> Aurelian.Rendering.Contracts
Aurelian.Rendering.Raster -> Aurelian.Rendering.Contracts
```

Neither Machina nor Aurelian references the bridge. In particular, the bridge does not reference the raster backend, so the composition root chooses a renderer. It has no engine, runtime, graphics, windowing, Dominatus, raster, or lifecycle responsibility.

## Translation contract

`MachinaPresentationTranslator.Translate(MachinaPresentationFrame)` is synchronous, deterministic, stateless, and linear in operation count. It copies the viewport dimensions and emits exactly one Aurelian operation for every source operation, in source order.

| Machina presentation value | Aurelian resolved-2D value |
| --- | --- |
| viewport width and height | `Resolved2DViewport` width and height |
| `FillRectangleOperation` | `FillRectangleOperation` with rectangle and straight RGBA color |
| `StrokeRectangleOperation` | `StrokeRectangleOperation` with rectangle, color, and exact thickness |
| `PositionedTextOperation` | `PositionedTextOperation` with bounds, text, presentation color, face, size, and alignment |
| `PushRectangularClipOperation` | `PushRectangularClipOperation` with its original rectangle |
| `PopClipOperation` | `PopClipOperation` |

The bridge does not measure, wrap, shape, re-place, theme, rasterize, or load fonts. Machina has already resolved renderer-facing bitmap text values. Nested clips are not intersected in the bridge; the backend realizes the clip stack.

## Identity and validation

Resolved operations carry the stable source ID with `.{operationIndex}` appended, for example `card.title.4`. The suffix preserves repeated source IDs without a registry or random value. Pop operations have no source ID, so their stable diagnostic IDs are `pop.{operationIndex}`. This is the same normalization previously used by the parity proof.

The immutable source and target contracts validate normal construction. The translator checks null input, null source entries, unknown future operation types, unmappable text enums, and inconsistent positioned-text presentation/style colors explicitly. Machina's legacy text realization uses the style color; normally the frame's resolved `Color` is exactly that value, and a mismatch is invalid rather than silently changing legacy semantics. Target construction continues to reject invalid viewport, finite geometry, thickness, text values, and clip balance if malformed data somehow bypasses source validation. RGBA conversion is a direct byte extraction from Machina's `0xRRGGBBAA` token.

## Test ownership and coexistence

Cross-system tests live at `tests/Integrations/Aurelian.Machina.Tests` and are owned by `JointTaskForce.Integration.slnx`. The former `AurelianCpuRasterParityTests` was moved from `Aurelian.Integration.Tests`; that project retains only Aurelian-only compositor/graphics coverage.

The integration proof sends real Machina frames through the production translator and `AurelianCpuRasterRenderer`, then compares dimensions, every straight-RGBA pixel, deterministic PPM bytes, and SHA-256 values with the retained legacy Machina raster route. It covers empty frames, fills, strokes, alpha blending, clips, nested clips, positioned bitmap text, mixed ordering, rich text positions, and a canonical authored Standard document.

The legacy `MachinaRasterPipeline` and `LegacyMachinaRenderCommandAdapter` remain unchanged for JTF-M3c. JTF-M3d should relocate/remove only legacy renderer realization and compatibility entry points after preserving this integration evidence; it must not add a Machina-to-Aurelian production edge.

## Non-goals

This milestone does not alter either presentation vocabulary, select a backend, implement renderer behavior, change UI input or screen ownership, integrate Aurelian Core/Runtime/worlds/Vulkan, or retire legacy Machina raster code.
