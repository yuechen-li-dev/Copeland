# Aurelian deterministic CPU raster backend — JTF-M3b

JTF-M3b adds Aurelian.Rendering.Raster: a synchronous, deterministic CPU realization of the small resolved-2D renderer input in Aurelian.Rendering.Contracts.Resolved2D.

## Ownership and dependency shape

Aurelian.Rendering.Contracts owns resolved 2D data. Aurelian.Rendering.Raster references only that project. Cross-system comparison is owned by Aurelian.Integration.Tests, which alone references both the legacy Machina raster projects and the new Aurelian raster project.

The backend has no package references and no production Machina, Dominatus, Core, Runtime, Graphics, Vulkan, Silk.NET, windowing, or sample dependency.

## Existing renderer vocabulary audit

RenderSnapshot, RenderCommandPlan, RenderPassPlan, DrawItem2D, and symbolic mesh/material/shader/pipeline/target references are retained unchanged. They are world-render and GPU-setup vocabulary, not an appropriate encoding of already-resolved 2D paint work.

M3b therefore adds one narrow sibling input: Resolved2DPlan. It is immutable, copies its ordered operation list, and validates clip balance before rendering. Its values are Resolved2DViewport, finite Resolved2DRectangle, and straight-alpha Resolved2DRgbaColor. Its explicit operations are fill rectangle, inside stroke rectangle, positioned text, push rectangular clip, and pop clip. Each has an Aurelian renderer operation identity.

ICompositorMechanism and IPresentationMechanism remain prepared compositor/presentation lifecycle ports. They do not fit this in-memory pixel realization and are not implemented by it. Aurelian.Rendering.Null remains a world-command trace backend. A future Vulkan implementation may consume the resolved-2D input separately; M3b has no Vulkan adapter.

## API, pixel contract, and text boundary

AurelianCpuRasterRenderer.Render accepts Resolved2DPlan and returns a completed RasterFrame containing an immutable RasterSurface. Pixels are row-major straight/non-premultiplied RGBA bytes with a top-left origin. GetPixel and CopyPixels are deterministic inspection APIs. RasterPpmEncoder emits retained P6 output using stored RGB and omitting alpha.

The surface begins transparent. Operations execute in input order and source-over uses integer premultiplied intermediates before returning straight-alpha bytes. Rectangles and clips use floor(x), floor(y), ceil(x + width), and ceil(y + height), then intersect the current clip and surface bounds. Empty or negative rectangles are no-ops; non-finite geometry is rejected during plan construction. Nested clips intersect. Stack underflow and incomplete clip frames are rejected during plan construction. Stroke is inside-rect with positive finite thickness normalized by ceiling and a one-pixel minimum.

Positioned text accepts only content, resolved bounds, resolved color, the deterministic readable 5x7 face, scale 1/2/3, and horizontal/vertical placement. Empty text is a no-op, lowercase maps to uppercase, whitespace maps to a space, and unsupported characters map to question-mark. It owns no shaping, wrapping, paragraph composition, typography policy, UI measurement, font loading, or Machina types.

## Temporary duplication and parity

The legacy Machina raster, raster-text, and Dominatus adapter remain unchanged. M3b ports only their pixel arithmetic, rounding, clip/stroke rules, and bitmap glyph data into Aurelian-owned source. It does not port Machina Rect, ColorToken, TextStyle, layout, Dominatus actuation, or pipeline ownership.

The integration-only AurelianCpuRasterParityTests independently construct Aurelian plans in test code and compare exact dimensions, all RGBA pixels, P6 bytes, and SHA-256 bytes against the legacy realization. The eleven passing cases cover transparent frame, opaque fill, clipped fill, nested clips, alpha blend, inside stroke, overlap ordering, primitive text, rich-text-position output reduced to text operations, a mixed frame, and a Standard card/button presentation frame produced through lowering and layout. This is exact automated overlapping-raster evidence, not a visual-parity claim.

## Scope and next milestone

M3b does not consume MachinaPresentationFrame in production, add Aurelian.Machina, switch MachinaRasterPipeline, remove legacy code, alter Machina operations, or add images, paths, transforms, gradients, textures, animation, GPU resources, render graphs, Vulkan, screen/input, Core lifecycle, or shader work.

M3c should add only the consumer-owned Aurelian.Machina translator from Machina presentation frames to Resolved2DPlan, preserving order, clipping, resolved text placement/color, and generated renderer identities. M3d may remove the temporary legacy realization only after that bridge becomes canonical and parity evidence stays green.
