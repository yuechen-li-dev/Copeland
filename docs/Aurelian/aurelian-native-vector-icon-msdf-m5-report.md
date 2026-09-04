# AURELIAN-NATIVE-VECTOR-ICON-MSDF-M5 report

## Outcome

**Outcome A — arbitrary static vector icons are production-qualified.** The real path is
`bounded SVG/path source -> canonical vector contours -> deterministic MSDF -> vector
atlas -> Machina semantic icon -> Aurelian adapter -> one native quad -> Vulkan`.
No bitmap intermediate, runtime source parser, runtime tessellation, per-frame field
generation, or duplicate icon shader was added.

> SVG is an authoring/import format, not a runtime rendering model.

> Aurelian renders compiled vector fields, not SVG DOMs.

## Existing infrastructure audit

| Needed M5 capability | Existing type/API | Reuse? | Missing seam supplied by M5 |
| --- | --- | --- | --- |
| normalized vector geometry | glyph `GlyphOutline`/contours/line/quadratic/cubic | law reused | frontend-neutral `VectorShape`, `VectorContour`, and vector segments |
| exact-zero cleanup | `MsdfSharpShapeConverter` | exact doctrine reused | vector constructor applies the same exact equality rule |
| MSDF generation | `MsdfSharpDistanceFieldGenerator`, MSDF-Sharp | settings and algorithm reused | vector-specific compiler result and diagnostics |
| deterministic packing | `GeneratedFieldAtlasPacker` | shelf/sort law reused | vector-keyed atlas metadata without glyph/font namespaces |
| semantic identity | `MachinaFontAtlasId` | no | `MachinaVectorIconId`, derived from normalized geometry and settings |
| renderer-neutral intent | `MachinaTextPresentationPrimitive` | pattern reused | `MachinaVectorIconPresentationPrimitive` |
| atlas lifetime | `AurelianMsdfAtlasResource`/cache | lifetime and row law reused | vector-specific resource/cache over vector metadata |
| native reconstruction | `NativeMsdfQuadSubmission`, `MsdfText.v.ts` | unchanged | icon adapter only |
| ordinary UI authoring | `UI`, layout, clips, actions | yes | `UI.Icon(...)` and `VectorIconNode` |

The existing `MsdfText.v.ts` contains no text layout, baseline, glyph, or font semantic.
It samples RGB, takes the median, computes coverage, and applies tint. M5 therefore
reuses it byte-for-byte. Renaming the already-qualified asset and pipeline enum would
create regression risk without changing semantics.

## Bounded source and canonical geometry laws

The parser accepts `svg`, `g`, `path`, `rect`, `circle`, and `ellipse`; path commands
`M/m`, `L/l`, `H/h`, `V/v`, `Q/q`, `C/c`, and `Z/z`; and affine `translate`, `scale`,
`rotate`, and six-number `matrix`. Every path contour must close. Shapes lower
immediately to line, quadratic, or cubic segments. `viewBox` is required, translated
to an origin-relative plane, and its SVG-down Y axis is normalized to upward vector
space before hashing and generation. Source transforms are multiplied and flattened
into coordinates; no transform stack reaches presentation or rendering.

The fill law is **non-zero**. Canonicalization computes contour nesting using only
larger containing contours, assigns clockwise outer contours, and alternates winding
at each nesting depth. This both makes holes deterministic and avoids a small island
misclassifying a containing hole. Only segments whose complete control polygon has
exactly identical points are removed; there is no epsilon deletion. Empty,
degenerate-only, non-finite, or zero-area geometry rejects.

Unsupported elements include text, image, filter, mask, clipPath, pattern, gradients,
animation, script, foreignObject, style, and use. CSS/class/style, strokes, event
attributes, filters, masks, clips, and external references reject. Only monochrome
black/currentColor fill and `fill-rule="nonzero"` are accepted. This is a bounded
static icon subset, not a browser SVG compatibility claim.

Malformed XML reports line/column. Unsupported or malformed vector input reports the
element, attribute where applicable, line where the XML API exposes it, and reason.

## Compilation, identity, bounds, and atlas

`VectorIconMsdfCompiler.CompileSvg` records source name, UTF-8 source SHA-256,
normalized-geometry SHA-256, compiler/version string, and settings. Icon identity is
SHA-256 over normalized geometry plus fill, field quality, short-axis minimum,
pixel range, edge coloring, and orientation—not the source pathname.

The deterministic quality rule makes the longest field axis 64 pixels and preserves
the source aspect ratio on the other axis, bounded to at least 16 pixels. The qualified
settings are RGB MSDF, pixel range 4, simple edge coloring at angle threshold 3 and
seed 0, and the existing finite RGB monochrome-SDF fallback. The corpus produced zero
non-finite values.

Semantic plane bounds remain distinct from padded field bounds and packed atlas bounds.
Padding expands storage only. Runtime `Contain` fits plane bounds into the destination,
then derives the padded field quad; layout never depends on field or atlas pixels.
The atlas is 256x256, padding 2, `TopToBottom`, eight entries, 44.043% field occupancy,
and SHA-256 `21a2185657be4273f15906cba402a1d3ffb4d3714856e290622b2e768b5978d4`.
Unspecified row order rejects. The Aurelian cache flips rows once at upload and the
adapter normalizes the matching UV interval.

## Presentation and native realization

Application authoring is `UI.Icon(Icons.Settings, size: 24)`. It carries a typed icon
identity, rectangle, RGBA tint, and optional explicit clip; it has no baseline, atlas
UV, source path, renderer choice, or `UseMsdf` flag. Intrinsic aspect comes from plane
bounds and the sole M5 fit mode is `Contain`. Hit testing remains the UI control rect.

The integration validates identity, atlas rectangle, normalized UVs, finite positive
field parameters, and opaque texture handle. Clipping changes destination and UV
together. Tint alpha uses the unchanged MSDF straight-alpha blend law. Icons share
the normal ordered MSDF submission stream; no overlay pass or reordering exists.
Compatible vector icons occupy one contiguous atlas/material range and collapse to
one draw. The complete proof stream is three draws because vector icons, cyan MSDF
heading, and white MSDF Settings label are distinct compatible ranges.

Cold runtime uploaded one vector atlas and one font atlas. The immediately repeated
warm frame performed zero atlas uploads, descriptor allocations, and descriptor writes.
Every semantic icon produced exactly one quad and six vertices. The proof contains 18
icon uses and 23 text glyph quads; no CPU geometry is submitted.
The retained runtime warm pass disables readback, allocates 10,304 managed bytes in
the measured renderer path, and records zero readback time. A disposed vector-atlas
cache is also exercised and must reject further resolution.

## Corpus and parity results

All fixtures are self-authored for this milestone and record that provenance.

| Icon | Contours | Segments | Field | Qualification feature |
| --- | ---: | ---: | ---: | --- |
| Settings/Gear | 2 | 20 | 64x64 | concavity and central hole |
| Play | 1 | 3 | 57x64 | asymmetric triangle |
| Pause | 2 | 8 | 50x64 | narrow gap/tiny feature |
| Check | 1 | 6 | 64x52 | concavity and non-square bounds |
| Close | 1 | 12 | 64x64 | source rotation flattened before compile |
| Heart | 1 | 4 | 61x64 | cubic curves and concavity |
| InfoCircle | 4 | 16 | 64x64 | nested hole/island winding and cubic circles |
| Folder | 2 | 10 | 64x39 | deliberately wide aspect |

Direct-vector versus CPU-MSDF was measured at 16, 24, 32, 64, and 128 px for every
icon. The acceptance gate is IoU >= 0.72 and mean edge distance <= 1.5 pixels. All 40
cases pass: minimum IoU is 0.7625 (InfoCircle at 16 px), and maximum mean edge distance
is 1.0034 pixels (Folder at 128 px). Settings IoU is 0.9801/0.9882/0.9967/1.0/0.9971
across the five sizes. CPU-MSDF versus Vulkan was measured for all 18 showcase uses;
minimum IoU is 0.9231 and most corpus cases exceed 0.99. Exact per-case hashes,
IoUs, edge distances, and bounds are in `parity.json`, `icons.json`, and `atlas.json`.

## Real UI proof

The 1280x720 Machina tree contains actionable Settings and Play controls, a rounded
analytic button substrate, Settings gear plus native MSDF label, Play icon plus raster
label, active pill/check status, info row/icon, all eight corpus icons, and the same
compiled Settings icon at 16/24/32/64/128 px. RoundedRect, Circle, Pill, MSDF text,
MSDF icons, and RasterPixel text coexist. No per-icon pixel nudge is present; placement
uses ordinary `UI.At`/layout rectangles.

The curated captures are `showcase-1280x720.png` and `showcase-2560x1440.png`. The 2x
capture intentionally reuses the same compiled fields; it does not select alternate
raster assets. The canonical native MSDF readback SHA-256 is
`31189773aaa219c61021c39683c36aca7ea4512dd6024e4a7cb3b8ca48e928c5`.

## Performance, validation, and boundaries

The retained proof records per-icon cold compile time, total source-to-field time,
atlas pack time, compile allocations, adapter allocations, vertex upload, command
recording, submit/wait, and readback separately. The observed cold compile total was
machine-specific (roughly 0.22 s before later reruns); these numbers are evidence, not
an architecture contract. Warm execution performs no source parsing, contour
normalization, MSDF generation, packing, or texture upload. Only obvious bounded
allocations were addressed; the runtime metric excludes proof readback, while the
separate evidence pass intentionally allocates a full image.

Khronos validation was requested and available. Shader compilation used the real
Visual TypeScript -> VD-MIR -> HLSL -> SPIR-V path; both stages validated and Vulkan
completed with zero reported errors. `CompiledGraphicsProgram` remains pipeline and
material authority. Machina references no Aurelian/Vulkan type; Aurelian.Graphics
references no Machina/vector parser type. The adapter/cache is the only bridge.

The production path is intended to be:

```text
assets/icons/*.svg
-> build/compiler
-> compiled MSDF asset bundle
-> app/runtime
```

M5 deliberately does not build a general asset daemon, SVG DOM, gradient/multicolor
model, stroke engine, layered icon system, runtime rotation system, or GPU field
generator.

## Tests, artifacts, and next milestone

Focused tests cover the bounded parser, viewBox, every transform, segment families,
multiple contours, exact-zero cleanup, malformed/unsupported/empty rejection,
content identity, deterministic fields/atlas, explicit orientation, all eight fixtures,
five sizes, direct/MSDF parity, `UI.Icon` lowering, tint/alpha transport, aspect fit,
UVs, clips, and invalid identity/texture rejection. The native proof hard-asserts real
actions, one quad per icon, warm lifetime behavior, CPU/GPU parity, deterministic
readback, and zero initialization errors.

The compact retained set is `proof.json`, `icons.json`, `atlas.json`, `parity.json`,
`rendering.json`, `manifest.json`, and two PNG captures. The executable is
`tools/Aurelian.NativeVectorIconMsdfM5`.

GPU MSDF generation remains technically plausible as `CPU semantic preprocessing ->
GPU field generation`, but it would add compute synchronization and a second oracle
path without demonstrated runtime compilation pressure. CPU remains the reference and
offline compiler. The exact next milestone is **MACHINA-VECTOR-ASSET-PIPELINE-M6**:
package the now-qualified compiler artifacts into a build-time bundle and registry.
`AURELIAN-GPU-MSDF-COMPILATION-M6` should proceed only if measured authoring/runtime
pressure justifies it.
