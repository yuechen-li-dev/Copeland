# AURELIAN-NATIVE-MSDF-TEXT-M2 report

## Outcome

Outcome A. The qualified `MachinaGlyphRun` and vector-derived MSDF atlas now render
through the production Visual TypeScript shader, VD-MIR, HLSL, DXC SPIR-V, the
persistent Aurelian ordered-quad path, Vulkan offscreen draw, and readback. Aurelian
does not parse fonts or perform layout.

## Mandatory integration audit

| Needed M2 capability | Existing API/type | Reuse? | Missing seam resolved |
| --- | --- | --- | --- |
| qualified placement | `MachinaGlyphRun` / `MachinaGlyphPlacement` | yes | integration adapter only |
| atlas metadata | `FontAtlasSnapshot`, `GlyphAtlasEntry`, `GlyphFieldPlacement` | yes | none |
| vector-derived RGB fields | `GeneratedFieldAtlasPage` | yes | direct float-to-RGBA8 upload encoding |
| ordered glyph geometry | `VulkanOrderedQuadRenderer`, six vertices per quad | yes | explicit MSDF submission overload |
| persistent texture/upload | `CreateTexture`, `VulkanTextureUploader` | yes | none |
| program metadata | `CompiledGraphicsProgram` | yes | second exact material shape |
| production shader | Visual TypeScript GPU profile | yes | `MsdfText.v.ts` plus bounded scalar math |
| sampler/blend | native pipeline factory | yes | linear/clamp sampler and one fixed straight-alpha state |
| offscreen/readback | native M0/M1 target and readback | yes | larger bounded target only |

No duplicate run, glyph, field-placement, atlas-entry, or texture-handle model was
introduced. The one new renderer-facing value is `NativeMsdfQuadSubmission`:
destination rectangle, UV rectangle, opaque atlas handle, color, pixel range,
per-vertex field scale, and threshold.

## Frozen authority and identities

The font remains `CrimsonText-Regular.ttf`, SHA-256
`48e6c5d5ad1d01599d374ecb817e15890d1feb3b8a3a88e527d44c90389e1f06`.
The checked-in outline M1 manifest hashes to
`8a2a0cf5f5684745cb5b3648817e3fd0a77d31aec3d63c4c9332aaadf4220688`;
the MSDF M1 manifest hashes to
`b8b04f211b5d07a5f345c3bdae75cea09f7fd37f56c8b740beb1077d3a0daa4c`.
The canonical 64 px run identity is
`c5fc69dc48482911bd2782ff0b848f0d7ff167a15b51bc29be6dc76bbc0e64ac`,
and its union atlas float hash is
`6010902d276385e5d417c1ac9be3a78d1e9bfb0754686d15588781d80f13f8e0`.
Individual field hashes are recorded in `proof.json`.

`AurelianGlyphRunAdapter` is owned by the renderer-specific integration leaf
`src/Integrations/Aurelian.Machina.Graphics`. It looks up
the existing `GlyphKey` atlas authority (the current atlas does not store a second
GlyphId index), skips whitespace, and preserves run order. Destination rectangles are
`OriginX + field.PlaneLeft`, `BaselineY + field.PlaneTop`, and the qualified field
plane width/height. Atlas storage dimensions and padding never determine placement.
UVs remain storage-rectangle edges; normalized Vulkan linear sampling implements the
same texel-center mapping as the CPU reference.

## Shader and native realization

The production source is `src/Aurelian/Aurelian.Shaders/Assets/MsdfText.v.ts`. It
samples RGB, reconstructs the median distance, applies the existing scale-aware
smooth coverage law around threshold 0.5, multiplies coverage into straight-alpha run
color, and outputs one target. There is no handwritten HLSL fallback.

Shader identities:

| Artifact | SHA-256 |
| --- | --- |
| VD-MIR | `a5bd3700a1835692f036af43b971ad70aac4906ca723a6fe6b10918fcf7e53c7` |
| HLSL | `92d7f9dfd33e0a458da84c9e264c62eaefa3b45aa53da127ee936ed375c7fe0a` |
| vertex SPIR-V | `82a0080d39dba4dac957dae58d5aaf61d7083c7eb32166a613f87e7b755e2de3` |
| pixel SPIR-V | `d075854aa947790ef555e1fc841a90d4f5316f1b04a7e7bfee0e090a90c37936` |
| renderer metadata | `bbfeaac5337d0e1ae73cc8d5c006a313924ce17aa9bc43a0b04491c4546a9dc2` |

The native atlas is `VK_FORMAT_R8G8B8A8_UNORM`: qualified RGB floats are deterministically
quantized to RGB8 and alpha is 255, then uploaded directly without PNG or image loader.
The existing uploader performs the canonical transfer-destination to shader-read layout
path. One persistent linear, clamp-to-edge sampler and one persistent MSDF pipeline are
owned by the renderer. Straight-alpha color blending is source-alpha / one-minus-source-alpha;
alpha blending is one / one-minus-source-alpha.

Glyph-specific field scale is a scalar vertex attribute. Pixel range 4, threshold 0.5,
and tint are run material data. This avoids a per-glyph uniform/descriptor and lets one
atlas/color run remain one ordered draw. Compiler metadata owns all three descriptor
bindings and the 32-byte material layout.

## Rendering and parity

The bounded offscreen target is 2560x384 with RGBA clear `(16,32,64,255)`, wide enough
for the 128 px fox proof. Same-machine canonical hashes are:

| Proof | Result |
| --- | --- |
| `Hello Machina`, 64 px | `972e1cd92139d8bd4f13ac92a0795196627b58d5bd24f555079d6e189a6b18e9`, 12 quads, 1 draw |
| fox, 64 px | `f00df20b778d501a2b3603a27a303c756a0bb1bd3abd13576cf86002c789fa29`, 35 quads, 1 draw |
| `M` | non-clear 53x41 bounds, 1 draw |
| `.` | non-clear 7x7 bounds, 1 draw |
| `g` | non-clear 26x41 bounds with pixels below baseline, 1 draw |
| `Q` | non-clear 45x50 bounds, 1 draw |
| `Agjpqy`, 64/96 px | descenders extend below both qualified baselines |

CPU comparison uses the same quantized RGBA8 atlas bytes, UVs, destination planes,
colors, and reconstruction parameters. Across all required cases, binary-coverage IoU
is at least 0.856; 64 px `Hello Machina` is 0.991 and the 64 px fox is 0.969. Exact
bounds and per-case mean channel delta are in `parity.json`. The small-size minimum is
bounded edge quantization; GPU and CPU bounds agree.

Fields follow the qualified size policy: 16/32 use 32 fields, 64 uses 64, and 96/128
use 128. Therefore atlas reuse is within a size/field-settings identity, not across all
display sizes. Each tested size uses one atlas page. No glyph generation or upload
occurs during draw.

Three separate 64 px runs (`Hello Machina`, fox, `Agjpqy`) sharing atlas, color, and
parameters form one 53-quad draw. Two colors form two ordered draws. The final pass of
100 persistent repetitions uses one draw, zero descriptor writes/allocations, one
vertex upload, 0.012 ms upload, 0.072 ms recording, 0.375 ms submit/wait, and 16.909 ms
readback on this machine. The final qualification run measured 255.0 ms shader compilation,
199.5 ms atlas generation, 2.2 ms renderer/pipeline creation, and 3.4 ms atlas upload.
The adapter allocated 1,280 bytes for canonical `Hello Machina`; the full pass allocation
includes the optional 3.9 MB readback buffer copy.

Khronos validation was enabled and reported zero errors and zero warnings. Disposed
atlas handles, missing entries, out-of-page storage rectangles, invalid UV mappings,
and invalid/non-finite reconstruction parameters reject deterministically before draw.
Existing pass lifecycle rejection remains unchanged.

## Ownership and disposition

The frozen chain is:

```text
Machina.Fonts: layout, glyph run, vector field, atlas semantics
-> Aurelian.Machina.Graphics: renderer-specific glyph-run adapter
-> Aurelian.Graphics: opaque texture, ordered quads, Vulkan lifetime
-> Visual TypeScript / VD-MIR: shader and binding semantics
```

`Aurelian.Graphics` has no Machina, Typography, font-file, shaping, kerning, baseline,
or atlas-packing dependency. No layout, hmtx, GPOS, outline transform, or MSDF generation
source changed. The same SDF realization can later support vector icons and line art,
but M2 implements none of those features.

The Vulkan 1.3 headless/offscreen strategy is the same as M0/M1 and is CI-feasible on
an agent with a Vulkan 1.3 hardware or software ICD and Khronos validation layer.
The existing uploader is command/fence based and accepts prebuilt atlas bytes; no async
generation or scheduling change is needed for compatibility.

The exact next integration milestone is `AURELIAN-NATIVE-MACHINA-PRESENTATION-M3`:
add one compositor-neutral text presentation primitive that carries an already-built
glyph run and atlas identity into this adapter. It must not add shaping, font loading,
text editing, rich text, fallback, or a general compositor framework.

Compact evidence is limited to five JSON files under
`artifacts/aurelian-native-msdf-text-m2/` (about 43 KB total). No framebuffer bundle is
committed.
