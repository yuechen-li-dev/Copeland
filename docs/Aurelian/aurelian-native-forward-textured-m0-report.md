# AURELIAN-NATIVE-FORWARD-TEXTURED-M0 report

## Outcome

**Outcome A — the existing Vulkan stack consumes the compiler-produced M3 `ForwardTextured` program and produces deterministic textured/tinted pixels.**

The qualified path is:

```text
ForwardTexturedM3.v.ts
-> Copeland GPU binder / graphics.m3 VD-MIR
-> Aurelian.Shaders HLSL + DXC Vulkan 1.3 SPIR-V
-> CompiledGraphicsProgram
-> Aurelian.Graphics Vulkan render pass, pipeline, descriptors and resources
-> 64x64 R8G8B8A8_UNORM offscreen draw
-> transfer readback
-> canonical RGBA SHA-256
```

No TinyFarm, sprite, camera, transform, material-object, asset, batching, swapchain, compositor, frame-graph, depth, blend, MSAA, cache, or background-thread work was added.

## Mandatory Vulkan infrastructure audit

| Required M0 capability | Existing type/API | Reuse directly? | Missing seam |
| --- | --- | --- | --- |
| instance/device/physical selection | `VulkanPlantInitializer`, `AurelianVulkanPlant` | yes | instance API cap raised from 1.2 to compiler-required 1.3 |
| queue family / graphics queue | `AurelianVulkanPlant` | yes | none |
| command pool/buffers | `VulkanCommandBufferPool`, `VulkanCommandBufferLease` | yes | none |
| timeline fences / submit / wait | `VulkanFenceBundle`, `VulkanCommandSubmitter` | yes | none |
| buffers / mapped memory | `VulkanBufferFactory`, `RawVulkanMemoryAllocator` | yes | bounded mapped `ReadBytes` added |
| images / memory / views | `VulkanTextureFactory`, `AurelianVulkanTexture` | yes | none |
| texture staging/upload/transitions | `VulkanTextureUploader`, barrier/layout tracker | yes | none |
| sampler | none | no | one nearest/clamp sampler in bounded native scenario |
| descriptor layout/pool/set/writes | none | no | metadata-driven set-zero realization in bounded native scenario |
| shader modules | `VulkanGraphicsPipelineFactory` | yes | none |
| pipeline layout | `VulkanGraphicsPipelineFactory` | yes | accepts internal descriptor-set layouts instead of always zero layouts |
| graphics pipeline | compiled-stage mapper and pipeline factories | yes | vertex adapter derives stride/attributes from compiler metadata |
| render pass / framebuffer | render-pass and framebuffer factories | yes | none; existing render-pass path retained |
| draw recording | render-pass and draw encoders | yes | descriptor set bound before existing draw call |
| readback copy | none | no | one image-to-mapped-buffer copy plus transfer-to-host barrier |
| swapchain/presentation | existing but irrelevant | no | intentionally unused |
| pipeline cache | no participating cache | no | intentionally deferred |
| validation layers | `VulkanPlantInitializer` | yes | Vulkan 1.3 mismatch found and fixed by the first validation run |

The audit classification is predominantly **WORKS AS DESIGNED**. The typed compiled-graphics contract, descriptor realization, internal pipeline-layout input, sampler, and readback are **SMALL SEAM** items. No **ARCHITECTURAL PROBLEM** was found.

## Compiler artifact boundary

`CompiledGraphicsProgram` is renderer-neutral and contains the existing `CompiledShaderProgram` SPIR-V stages plus program name, feature level, compiler profile, VD-MIR hash, vertex inputs, pixel targets, resources, stage visibility, and material ABI. `CompiledGraphicsProgramExporter` is owned by `Aurelian.Shaders`; it projects successful compiler/backend results into that contract. `Aurelian.Graphics` references only `Aurelian.Rendering.Contracts`, never Copeland, VD-MIR, HLSL, or DXC.

SPIR-V bytes and entry names come directly from the compiled stage objects. Graphics does not reopen `.spv` files and does not assume `main`. A bounded binary verifier reads SPIR-V `DescriptorSet`, `Binding`, and member `Offset` decorations only as a cross-check. Compiler metadata remains construction authority.

## Pipeline and resource realization

- Vertex metadata maps location 0 `float3` position and location 1 `float2` UV into a 20-byte binding. The fixture is a clockwise-in-framebuffer six-vertex quad so `frontFace` exercises the sampled/tinted shader branch.
- Compiler resources map `Texture2D` to `SampledImage`, `Sampler` to `Sampler`, and material to `UniformBuffer`. Visibility maps exactly from compiler stages; M0 does not use `ALL_GRAPHICS`.
- Set 0 has the compiler-provided bindings 0, 1, and 2. No push constants exist.
- The 2x2 RGBA texture is red/green/blue/white, uploaded by the existing staging path with `Undefined -> TransferDestination -> ShaderResourceFragment` transitions.
- The sampler is nearest/nearest, clamp-to-edge, no anisotropy, and one mip level.
- Material upload is a 32-byte array. The writer locates `tint` and `roughness` fields by compiler names and writes at compiler offsets. Tint is `(0.5, 1.0, 0.75, 1.0)` and roughness is `0.375`.
- The target is 64x64 `R8G8B8A8_UNORM`, color-attachment plus transfer-source, with no depth. The existing render-pass/framebuffer path transitions its final layout to transfer-source.
- Fixed state is triangle list, fill, no culling, no blending, no depth, sample count 1, full dynamic viewport/scissor.

## Submission, synchronization, readback and lifecycle

The one-frame law is initialize, upload, draw, wait, read, dispose. Texture upload completes through the existing timeline fence before descriptor use. The render pass performs the color transition, the draw completes before the image-to-buffer copy, and a transfer-write to host-read buffer barrier precedes the waited submission and mapped read.

Canonical pixels are the 64 rows returned by tightly packed Vulkan image-to-buffer copy, with no row padding, in RGBA channel order. Only the 16,384 canonical pixel bytes are hashed. The qualified SHA-256 is:

```text
521e2788a769bb98bd3cc8f966fba3940e2d5a7ad0cd0ff06ac52ceea16c60f7
```

Semantic facts are 1,792 exact clear pixels, 2,304 drawn pixels, four distinct drawn colors, texture contribution true, and tint contribution true. Ten create/draw/read/dispose cycles on one device produced the same hash. A separately initialized fresh device also passed with the same hash. Same-machine exact equality is the M0 law; cross-device qualification should retain semantic assertions and introduce explicit tolerances only if another device demonstrates byte variation.

Destruction order is: wait device idle; descriptor pool; sampler; pipeline and pipeline layout; framebuffer; render pass; descriptor-set layout; readback/material/vertex buffers; render target; sampled texture; command/fence/allocator owners; device; instance. Image views are destroyed before their images and allocations by `AurelianVulkanTexture`; buffers are destroyed before allocations by `AurelianVulkanBuffer`.

## Diagnostics and negative contracts

Pre-submit validation rejects:

- a missing required binding, including the missing texture-binding case;
- any material payload whose byte length differs from compiler metadata;
- a vertex stride or byte payload incompatible with compiler vertex metadata;
- duplicate/set-nonzero resources, unsupported physical vertex types, malformed texture extent/data, missing stages, and SPIR-V/metadata binding or material-offset disagreement.

The initial validation run caught a genuine SPIR-V 1.6 versus Vulkan 1.2 environment mismatch. `VulkanPlantInitializer` now requests Vulkan 1.3 when available, matching the M3 compiler target. The qualified run enabled `VK_LAYER_KHRONOS_validation` and emitted zero validation errors or warnings.

## Qualified machine and timings

The proof was recorded on an NVIDIA GeForce RTX 3070, vendor `0x10de`, device `0x2488`, driver integer `2500395008`, Vulkan API integer `4211017`, queue family 0, with timeline semaphores and Khronos validation enabled. The canonical recorded timings were: compiler 251.902 ms, device init 399.868 ms, resource creation/upload 19.342 ms, pipeline/descriptors 23.976 ms, record/submit/wait 8.215 ms, readback/assert/hash 0.918 ms, and draw-scenario total 54.576 ms. These are diagnostic, not performance gates.

Hardware Vulkan 1.3 plus timeline semaphores is required by the current path. Headless CI is feasible with a Vulkan 1.3 software ICD such as Lavapipe and the validation layer installed, but it is not faked or made mandatory in M0.

## Tests, artifacts and regressions

Focused tests cover compiled metadata completeness, stage SPIR-V presence, vertex and descriptor mappings, material layout, reflection cross-check inputs, texture/sampler/target/pipeline/draw/readback through the native runner, semantic pixels, repeated device reuse, fresh initialization, and the three required negatives. Existing Aurelian Graphics tests continue to qualify modules, layouts, buffers, textures, uploads, render passes, framebuffers, pipelines, draw, submit, synchronization, swapchain, and compositor paths.

The five compact artifacts are under `artifacts/aurelian-native-forward-textured-m0/`. They contain hashes and sampled pixel evidence only; no framebuffer or screenshot is stored.

## M1 continuation

**AURELIAN-NATIVE-2D-QUAD-M1 is now qualified with Outcome A.** The persistent renderer reuses `CompiledGraphicsProgram`, records immutable ordered quad submissions, owns opaque persistent RGBA8 textures, and passes canonical, 100-quad, and 100-pass native Vulkan proofs without adding TinyFarm, sprites, text, materials, assets, cameras, transforms, generalized batching, or a render graph. See `docs/Aurelian/aurelian-native-2d-quad-m1-report.md`.
