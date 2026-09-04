# AURELIAN-NATIVE-MACHINA-PRESENTATION-M3 report

## Outcome

Outcome A. A real Machina composition built from `UiDocument`, anchored rows,
`VStack`, `Card`, `Button`, `Label`, and `Text` now presents raster/pixel and native
MSDF text side by side. Raster remains the default and an intentional retro style.
MSDF is an explicit opt-in realization mode and uses the qualified M2 Vulkan path.

> Raster/pixel text remains a first-class aesthetic presentation mode. MSDF text
> adds scalable vector-quality realization; it does not invalidate the raster style.

## Presentation-path audit and reuse

| Concern | Existing owner | Reuse? | M3 change |
| --- | --- | --- | --- |
| semantic controls and labels | Machina Core/Standard | yes | none |
| layout, bounds, hit testing | Machina Layout/Runtime | yes | none |
| ordered presentation frame | `Machina.Presentation` | yes | text operation can carry one optional qualified primitive |
| raster/pixel realization | `MachinaPresentationTranslator` plus `AurelianCpuRasterRenderer` | yes | remains implicit default |
| glyph placement | `MachinaGlyphRun` | yes | carried unchanged for both parity modes |
| atlas metadata and field planes | `Machina.Fonts` | yes | opaque atlas identity added at presentation boundary |
| glyph-to-native-quad lowering | `AurelianGlyphRunAdapter` | yes | presentation adapter supplies operation offset and optional clip |
| atlas GPU lifetime | integration layer | new bounded seam | stable identity cache uploads once and disposes textures |
| shader and Vulkan draw | M2 `MsdfText.v.ts` and `VulkanOrderedQuadRenderer` | yes | unchanged |
| compositor | renderer-neutral operation ordering | yes | no general compositor redesign |

There is still one text operation model. `PositionedTextOperation` retains semantic
text, rect, style, and color and optionally carries `MachinaTextPresentationPrimitive`:

```text
MachinaGlyphRun
+ MachinaFontAtlasId
+ MachinaTextRenderingMode (RasterPixel | Msdf)
```

The primitive contains no Vulkan, SpriteBatch, Avalonia, or parser object. Applying
it through `MachinaTextPresentationFrame.Apply` replaces only presentation values;
viewport, operation order, rectangles, semantic text, resolved layout, and hit-test
geometry remain unchanged. Existing operations with no primitive report
`RasterPixel`, which is the global default policy.

## Real UI proof

The dedicated runner `tools/Aurelian.NativeMachinaPresentationM3` builds the bounded
`Text Rendering Backends` UI. Its left card is `Raster / Retro`; its right card is
`MSDF / Smooth`. Both contain `Hello Machina`, Inventory, Settings and Play buttons,
a hotbar-like label, small status, a 32 px inventory line, and the fox sentence.
Fixed Machina stack slots reserve the same control geometry independently of backend.

The MSDF card uses 16, 24, 32, and 64 px runs. The 2x proof reuses a nearest-scaled
raster base, preserving crisp pixel-art scaling, while rebuilding the MSDF realization
at 32, 48, 64, and 128 px. This exercises 1.0x and 2.0x host scale without blurring
the raster style. The production M2 linear sampler, reconstruction threshold, field
range, straight-alpha blend, and shader are unchanged.

White, 75%-alpha muted, and cyan accent colors flow from normal Machina text styles. Painter
order remains operation order. Button and hit-test geometry do not depend on mode.
The adapter supports rectangular clipping by clipping destination rectangles and UVs;
no new general clip system was added.

## Shared-layout proof

The canonical parity strings are `Hello Machina`, `Inventory`, `Settings`, and
`The quick brown fox jumps over the lazy dog`. For each case, raster and MSDF
primitives reference the same local-coordinate `MachinaGlyphRun`. The proof fails
unless both the complete run hash and semantic layout hash are equal. Atlas storage
coordinates, pixel range, and atlas identity are excluded from layout. Attaching the
presentation mode must also preserve the complete frame topology/geometry hash.

The current atlas policy remains M2's size-qualified policy. Font identity, glyph
set, size, and MSDF settings produce a content-bearing `MachinaFontAtlasId`. Atlases
are generated before presentation. `AurelianMsdfAtlasCache` maps that identity to
persistent native textures, rejects identity/version misuse, performs no font parsing,
reuses unchanged textures on warm frames, and disposes them at shutdown.

## Atlas orientation guard

Machina atlas artifacts store rows top-to-bottom, while the Vulkan presentation path
uses a bottom-to-top texture convention. M3 makes this explicit with the required
`AurelianMsdfAtlasRowOrder.TopToBottom` declaration. Unspecified orientation is
rejected. `AurelianMsdfAtlasUpload` owns both the one-time row reversal and matching
`(v0,v1) -> (1-v1,1-v0)` packed-interval transform. Tests cover both halves so a
future adapter cannot accidentally produce upside-down glyphs or sample another
packed row.

## Native result and telemetry

The proof renders multiple labels through:

```text
Machina UI -> Machina presentation frame -> MachinaTextPresentationPrimitive
-> Aurelian.Machina.Graphics -> NativeMsdfQuadSubmission[]
-> VulkanOrderedQuadRenderer -> MsdfText.v.ts
```

The 1280x720 and 2560x1440 PNGs are local curated visual evidence under
`artifacts/aurelian-native-machina-presentation-m3-visual/`. Manual inspection
confirmed upright readable glyphs, deliberate raster alignment, smooth MSDF edges,
correct baselines, punctuation, colors, button placement, and no atlas seams. Exact
pixel hashes, layout hashes, glyph/quad counts, draw counts, descriptor writes,
uploads, warm timing, and CPU allocation are recorded in the JSON artifacts.

Each output uses four size-qualified atlas uploads before drawing. A second warm
frame must retain the upload count and produce zero descriptor writes. Atlas generation
never occurs in a draw call or repeated frame. Same-atlas/color runs coalesce according
to the existing ordered renderer; the proof deliberately retains several colors and
sizes, so it records multiple ordered draws rather than inventing a universal batcher.

## Boundaries and regressions

`Aurelian.Graphics` still references no Machina, Typography, font parser, shaping,
kerning, or atlas-generation package. `Aurelian.Machina.Graphics` alone sees both
Machina font/presentation values and native GPU handles. No Vulkan type enters Machina.
No MSDF, atlas, or pixel-range value enters layout. Headless Machina presentation tests
exercise both modes without a GPU.

The runner requests `VK_LAYER_KHRONOS_validation`, validates both SPIR-V stages, and
fails on validation errors. Compact evidence is limited to five JSON files under
`artifacts/aurelian-native-machina-presentation-m3/`; visual evidence is kept in the
separate curated local folder.

Future vector-backed SVG, icons, and line art may reuse the field-backed native path.
Rounded rectangles, circles, and pills may later use analytic SDF; decorative panels
may use analytic SDF or nine-slice. None are implemented here.

## Exact next milestone

`AURELIAN-NATIVE-ANALYTIC-SDF-PRIMITIVES-M4`: qualify compositor-neutral rounded
rectangle, circle, and pill presentation primitives through compiler-owned shaders and
the ordered native path, without changing Machina text or introducing a render graph.
