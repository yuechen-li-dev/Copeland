# AURELIAN-NATIVE-2D-QUAD-M1 report

> M2 follow-on: the same persistent ordered six-vertex quad owner now has one explicit
> MSDF text pipeline variant. Glyph-specific field scale is a vertex attribute; run-level
> tint, pixel range, threshold, atlas, sampler, and descriptors remain reusable, so an
> ordinary run is one draw without reordering. The original textured M1 defaults and
> canonical regression path remain unchanged.

## Outcome

**Outcome A — a reusable native ordered-quad primitive emerges cleanly.** One `VulkanOrderedQuadRenderer` reuses the existing `AurelianVulkanPlant`, compiler-driven pipeline and descriptor layout, sampler, descriptor pool, command/fence infrastructure, target, framebuffer, and mapped vertex buffer across passes. Raw RGBA8 textures persist independently across passes. The API is the deliberately small `Begin2D`, immutable `SubmitQuad`, and `End2D` path; optional readback remains proof-only.

No sprite, entity, transform, camera, material instance, asset database, atlas, text, compositor adapter, swapchain, render/frame graph, bindless descriptor, indirect draw, instancing, or retained scene state was added.

## M0 lifetime audit

The audit was taken from `VulkanNativeForwardTexturedRenderer.Render`, not inferred from desired architecture.

| Concern | M0 lifetime | M1 target/implemented lifetime |
| --- | --- | --- |
| Vulkan instance/device | caller-owned; reused for ten `Render` calls | existing `AurelianVulkanPlant`; reused for renderer lifetime |
| shader modules | recreated inside every pipeline creation, then immediately destroyed | created once during renderer pipeline construction, then destroyed; pipeline persists |
| descriptor layout | recreated per `Render` | once per renderer/program |
| pipeline layout | recreated per `Render` | once per renderer/program |
| graphics pipeline | recreated per `Render` | once per renderer/compatible RGBA8 target |
| render pass | recreated per `Render` | once per renderer |
| framebuffer | recreated per `Render` | once per renderer/target |
| vertex buffer | recreated per `Render` | one reusable mapped buffer; starts at 256 quads and doubles |
| descriptor pool/set | one pool/set per `Render` | one renderer pool; cached set per unique texture/tint pair |
| sampler | recreated per `Render` | one shared nearest/clamp sampler per renderer |
| material buffer | recreated per `Render` | cached 32-byte buffer per unique texture/tint pair |
| command buffer | rented per `Render`; pool recreated per call | one command buffer rented/reused per pass from a persistent pool |

M0 also recreated and re-uploaded its sampled fixture texture, render target, mapped readback buffer, allocator, fences, uploader, and submitter per call. M1 keeps sampled textures, target, allocator, fences, uploader, and submitter alive; readback buffers remain transient and are created only when proof capture is requested.

## Submission contract

`NativeQuadSubmission` is an immutable renderer DTO containing `Native2DRect`, `Native2DUvRect`, opaque `Native2DTextureHandle`, and `Native2DTint`. The renderer copies values into transient renderer-owned submission storage and clears that storage after every pass.

Coordinates are pixel-space with a top-left origin, +x right, +y down, and axis-aligned rectangles. Width and height must be non-negative; off-target geometry clips naturally. UVs are immutable ordered `u0,v0,u1,v1` bounds. All floats must be finite. Tint components must be within `[0,1]`. Unknown and disposed handles are rejected before command recording.

Order is submission sequence only. There is no sorting or depth buffer. Opaque later submissions overwrite earlier submissions in overlap. Only adjacent submissions with the exact same texture and tint may share one draw call; the renderer never reorders for texture locality.

## Geometry, shader, and material

M1 uses six CPU-built vertices per quad because it fits the existing location-0 `float3` position/location-1 `float2` UV contract directly. A shared unit quad would still require per-draw transform transport, and instancing would require new compiler/shader interface work. One mapped upload contains the complete ordered pass geometry.

The M3 `ForwardTextured` Visual TypeScript program is unchanged. It still flows through VD-MIR, compiler metadata, generated HLSL, DXC SPIR-V, and `CompiledGraphicsProgram`. Graphics derives vertex attributes, descriptor bindings, visibility, and the 32-byte material layout from that program. SPIR-V binding/member decorations remain a cross-check only.

Tint remains `texture sample * material.tint`. M1 writes tint by the compiler-provided field offset and writes roughness as canonical `1.0`; the shader does not consume roughness. Because the existing binding is a non-dynamic uniform buffer, a cached descriptor/material resource per unique texture/tint pair is the smallest correct unchanged-shader model. No public material abstraction exists.

## Texture and descriptor ownership

`CreateTexture(width, height, rgba8)` accepts tightly packed RGBA8 only. A `Native2DTextureHandle` exposes only an opaque integer identity. Upload uses the existing staging path once and leaves the texture in stable shader-read layout across passes. The shared sampler is nearest/nearest, clamp-to-edge, one mip, and no anisotropy.

The renderer owns a 4,096-set bounded descriptor pool. A first use of a unique texture/tint pair allocates one set and writes the compiler-required texture, sampler, and uniform bindings (three writes). Reuse writes zero descriptors. Disposing a texture waits for outstanding work, frees every associated descriptor set/material buffer, destroys the native texture, and makes the handle deterministically invalid.

For cold compatible groups of 1, 10, and 100 same-texture/same-tint quads, each case allocated one set and performed three descriptor writes. This is constant with quad count. The observed pressure is unique texture/tint-pair growth, not same-binding quad count.

## Buffer, pass, and target law

The mapped vertex buffer starts at 256 quads. Capacity doubles deterministically when exceeded and never shrinks. Each pass performs one vertex-buffer write, records one render pass into one command buffer, performs one queue submission, and synchronously waits in this bounded M1 implementation. The 256x256 RGBA8 offscreen target uses fixed clear `[16,32,64,255]`; there is no depth or blending. `End2D(false)` performs no readback. `End2D(true)` appends the existing image-to-buffer proof copy and host barrier to the same command buffer.

Nested begin, submit outside a pass, end without begin, texture mutation during a pass, bad values, and unknown/disposed handles are rejected. The renderer retains GPU resources but no semantic scene objects.

## Rendering evidence

The canonical scene contains nine quads, two textures, more than three tints, an opaque overlap, and one partially offscreen quad. Exact sampled pixels prove the fixed clear, red and green material tints, later yellow overlap replacement, and clipped magenta geometry. Its same-machine SHA-256 is:

```text
4fedd1050accc442864faaa8eeae11c7d02efc6b1009a85d80d080c1372b1192
```

The deterministic 100-quad alternating-binding scene produced:

```text
56bb01ad7377a4ae342fa70f107d72e3abc878d3cc4d1dc448726588863adbf5
```

It used one renderer/device, one graphics pipeline, one command buffer, one queue submission, one vertex upload, and 100 draw calls. One hundred canonical passes reused the same renderer and textures and produced the canonical hash at both sampled endpoints. Validation reported zero errors. A second texture was created, rendered, disposed while the renderer remained alive, and then rejected on attempted reuse.

Same-machine hashes are exact. Cross-device policy remains semantic pixel assertions/tolerance if a second implementation demonstrates byte variation; M1 does not claim universal raster-byte identity.

## Measured scaling

Measurements are one local diagnostic run on the qualified RTX 3070 and are not performance gates. Compiler, device, and renderer initialization were 235.534 ms, 378.347 ms, and 45.715 ms respectively.

| Compatible adjacent quads | vertex upload ms | command recording ms | submit/wait ms | CPU allocated bytes | draws | descriptor writes | queue submits |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 0.002 | 0.041 | 0.106 | 1,336 | 1 | 3 cold / 0 warm | 1 |
| 10 | 0.007 | 0.038 | 0.094 | 2,632 | 1 | 3 cold / 0 warm | 1 |
| 100 | 0.065 | 0.042 | 0.439 | 15,592 | 1 | 3 cold / 0 warm | 1 |

Readback timing is recorded separately and is zero for these normal-path measurements. CPU allocation scales mainly with the renderer-owned vertex byte snapshot and binding-key snapshot. It is bounded and not pathological for M1; zero-allocation work is not justified yet.

## Tests, regressions, and artifacts

Seven focused xUnit cases cover valid values, non-finite coordinates, negative dimensions, reversed UVs, and invalid tint. The native proof additionally covers single/multiple same-texture quads, multiple textures/tints, overlap order, clipping, 100 quads, 100 repeated passes, texture persistence/disposal, lifecycle rejection, stable hashes, and zero validation errors.

The compact artifact bundle is `artifacts/aurelian-native-2d-quad-m1/`: `proof.json`, `rendering.json`, `resources.json`, `performance.json`, and `manifest.json`. No framebuffer dump is retained.

The release validation lanes are `dotnet test Aurelian.slnx -m:1`, `dotnet test Copeland.TS.slnx -m:1`, and `dotnet test JointTaskForce.slnx -m:1`, plus the native M0 runner, native M1 runner, artifact budget, JSON parsing, and `git diff --check`.

## Exact recommended M2 scope

**AURELIAN-NATIVE-2D-GLYPH-M2 — qualify one alpha-masked glyph-quad path over this ordered submission/resource lifetime.** The observed quad path itself does not need generalized batching: same-binding ranges already collapse to one draw, while order-correct alternating bindings expose expected draw pressure. The next ordinary 2D capability gap is text/glyph alpha semantics. M2 should add only the shader/compiler and fixed-blend pressure demonstrated by glyphs; it should not add a font system, atlas builder, compositor integration, sprite abstraction, or generalized material/batching framework.
