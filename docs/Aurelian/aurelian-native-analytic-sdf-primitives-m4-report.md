# AURELIAN-NATIVE-ANALYTIC-SDF-PRIMITIVES-M4 report

## Outcome

**Outcome A — rounded rectangles, circles, and pills are production-qualified as
textureless analytic GPU primitives.** Each semantic shape becomes one ordered quad
with six vertices, compiler-owned material metadata, generated HLSL, and validated
SPIR-V. No bitmap fallback, texture upload, CPU tessellation, shape mesh, generic SDF
tree, SVG, gradient, shadow, or blur path was added.

> Aurelian treats simple 2D vector geometry as analytic GPU programs, not
> pre-rasterized assets.

> MSDF is used for arbitrary precompiled contours; analytic SDF is used for simple
> parametric contours.

## Existing-path audit

| Concern | Existing owner/type | Reuse? | M4 change |
| --- | --- | --- | --- |
| Card and Button meaning | `Machina.Standard` components and `UiStyle` | yes | latent theme radii now become `RoundedRect` style semantics |
| Badge meaning | `Machina.Standard.Badge` | yes | badge shell uses the closed `Pill` kind |
| Fill and border | `UiStyle`, `FillRectangleOperation`, `StrokeRectangleOperation` | yes | analytic operation carries the same colors and width |
| Rectangle clips | Machina push/pop clip operations | yes | adapter intersects bounds and preserves original local coordinates |
| Hit testing | Machina layout/action geometry | yes | unchanged rectangular semantic bounds |
| Presentation order | `MachinaPresentationFrame` | yes | one ordered analytic operation replaces a shape's fill/stroke pair |
| Ordered native quads | `VulkanOrderedQuadRenderer` | yes | one `AnalyticShape2D` pipeline variant and submit overload |
| Blend state | native MSDF straight-alpha state | yes | analytic variant selects the same qualified blend law |
| Pipeline lifetime | persistent ordered renderer | yes | pipeline is constructed once per renderer and reused across all passes |
| Shader authority | `GpuGraphicsBinder`, VD-MIR, `CompiledGraphicsProgram` | yes | canonical `AnalyticShape2D.v.ts`; reflection remains a cross-check |

Machina contains no Vulkan type, and `Aurelian.Graphics` contains no Machina type.
`Aurelian.Machina.Graphics` owns the sole adaptation step.

## Renderer-neutral primitive and laws

`MachinaAnalyticShapePrimitive` contains `Kind`, `DestinationRect`, `FillColor`,
optional `BorderColor`, `BorderWidth`, and resolved `Radius`. The kind is closed over
`RoundedRect`, `Circle`, and `Pill`; there is no arbitrary path or expression tree.

- RoundedRect clamps author radius to `0 <= radius <= min(width,height)/2`.
- Circle requires an exactly square destination and derives radius as `width/2`.
- Pill ignores authored radius and derives `min(width,height)/2`, in either orientation.
- Presentation rejects zero/negative dimensions, negative or non-finite radius/border,
  and non-square circles. The native boundary independently rejects non-finite values.
- Rounded hit testing remains rectangular and layout-owned in M4.

Standard Card uses the theme large radius, Standard Button the medium radius, and
Standard Badge the pill law. Application semantics do not mention SDF.

## Shader and material

Production source is `src/Aurelian/Aurelian.Shaders/Assets/AnalyticShape2D.v.ts`.
The compiler profile gained only scalar `Abs(f32)` and `Sqrt(f32)` intrinsics. It emits
one 64-byte set-0/binding-0 uniform material:

| field | offset | storage |
| --- | ---: | --- |
| `fillColor` | 0 | `float4` |
| `borderColor` | 16 | `float4` |
| `halfSize` | 32 | `float2` |
| `radius` | 40 | `f32` |
| `borderWidth` | 44 | `f32` |
| `shapeKind` | 48 | `u32` |

There are no texture or sampler bindings. `shapeKind` is a bounded integer; Pill is
normalized to the rounded-rectangle law before submission and Circle is `1` in the
shader. Compiler metadata remains binding/layout authority.

The vertex stream is `position: float3` plus `local: float2`. Local coordinates are
`[0,1]` across the original quad. A clipped quad carries the corresponding subinterval,
so clipping never changes the distance field. Pixel coordinates retain the native 2D
top-left, +x-right, +y-down convention.

Rounded rectangles use:

```text
q = abs(p) - (halfSize - radius)
d = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius
```

Circle uses `length(p)-radius`; Pill reuses the rounded-rectangle function at half the
minor dimension. Coverage is the exact dimension-aware one-pixel law
`smoothstep(clamp(0.5-d,0,1))`. Derivatives were deliberately not generalized.
Border color uses the same distance and `clamp(d + borderWidth + 0.5,0,1)` transition.

## Qualification results

The canonical Vulkan proof starts from one real Machina `UiNode` tree and renders its
fourteen analytic presentation primitives, including 256x128/r24 rounded rect,
128x128 circle, 256x64 pill, 240x32 extreme pill, 16px circle, 512x256 large card,
buttons, and radius 0/1/8/16 cases. It emits 1280x720 and 2560x1440 captures. The
tree contains a real `StandardUI.Card`, two actionable `StandardUI.Button` controls,
a `StandardUI.Badge`, and semantic status geometry. The showcase renders its heading
through M3's native MSDF adapter and its remaining labels through the raster/pixel path,
so both text families are visibly present on the analytic substrate.

The C# authoring surface now exposes `UI.Fixed`, `UI.Fill`, `UI.Auto`, and `UI.Space`
as concise aliases over the existing typed stack-item model. This is the bounded piece
ported from MachinaLayout.JS for M4: it removes nested `UI.StackItem` ceremony without
adding another authoring IR, fluent DSL, or lowering path.

CPU reference and GPU readback agree at IoU 1.0 for all three canonical shapes. Maximum
8-bit alpha differences are 2 (rounded rect), 3 (circle), and 4 (pill), within the
declared 4/255 interpolation tolerance. Canonical 1280x720 GPU-only pixel SHA-256 is
`8632ada85334d750793f9de9b6faab185031050aa541a50b892bf169fbd04623`.

Every shape is one quad/six vertices. The canonical mixed geometry frame is fourteen
quads and fourteen draws because its materials differ; the existing contiguous-identical
binding coalescing law remains in effect and painter order is never changed. An opaque
overlap sample hard-asserts that the later green rounded rectangle wins over the earlier
purple rectangle. Cold material transport performs fourteen descriptor
allocations/writes (one UBO each); warm
frames perform zero. One hundred passes reuse the same pipeline, render pass, target,
framebuffer, descriptor pool, bindings, and vertex buffer. Texture uploads, shader
compilations, pipeline creations, and mesh generations during warm frames are all zero.

Exact machine-specific timings and allocations are recorded in `rendering.json` rather
than promoted to architecture. The warm metric excludes proof readback allocation.
Khronos validation was requested and available, with zero initialization/runtime errors.

## Regression and artifact policy

Headless Machina remains GPU-free. `AurelianCpuRasterRenderer` is the explicit CPU host
policy and contains the tiny pixel-center analytic reference used for parity and
headless realization. Semantic topology and hit-test geometry remain layout-owned;
only the background presentation operation changes family.

The compact artifact set is:

- `proof.json`
- `shader.json`
- `parity.json`
- `rendering.json`
- `manifest.json`
- two proof PNGs

The proof executable is `tools/Aurelian.NativeAnalyticSdfPrimitivesM4`. Required
solution lanes and M3/M2/native-quad regressions are recorded in the final milestone
validation rather than copied into JSON.

Current validation totals are 715 passing tests for `Machina.UI.slnx`, 650 for
`Aurelian.slnx`, 1,601 for `Copeland.TS.slnx`, 3,283 for `JointTaskForce.slnx`, and
39 focused `Aurelian.Machina.Tests`. The M4 native proof, M3 mixed-presentation proof,
M2 native-MSDF proof, M1 native ordered-quad proof, JSON artifact parse, and
`git diff --check` all pass. Khronos validation reports zero errors and warnings.

## Next milestone

The exact next vector-native milestone is **AURELIAN-NATIVE-VECTOR-ICON-MSDF-M5**:
reuse M2/M3's arbitrary-contour MSDF path for semantic icon contours, without adding
SVG runtime parsing or a generic path/SDF framework.
