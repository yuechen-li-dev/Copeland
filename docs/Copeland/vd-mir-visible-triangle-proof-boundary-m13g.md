# VD-MIR Visible Triangle Proof Boundary M13g

## Purpose

M13g defines the concrete proof boundary for using `samples/Aurelian.VisibleTriangle` as the first future visible `VD-MIR` target.

This document is audit-only:

- `VD-MIR` is not implemented
- visible triangle is not wired to `VD-MIR`
- no SDSL-V migration occurs
- no Slang/PTX backend work occurs
- no Machina/Aurelian/Vulkan bridge work occurs

Historical M14a exploratory work later added a compiler-side `VD-MIR` slice, but this proof-boundary document still describes the active sample truth in M14e: the visible triangle sample is not wired to `VD-MIR`, and the runtime artifact boundary remains the same checked-in `shader.toml` plus `generated.hlsl` plus `.spv.hex` files.

## Why visible triangle is the proof target

Visible triangle is the right proof target because it is the smallest current sample that already spans:

- checked-in shader artifacts
- Aurelian runtime contracts
- Vulkan graphics pipeline creation
- visible swapchain presentation
- a human-recognizable proof outcome

It is small enough to isolate the compiler boundary, but real enough to prove that future `VD-MIR` output still survives the existing Aurelian runtime/render path.

## Current sample baseline

Current baseline path:

```text
historical SDSL-V source
  -> HLSL
  -> DXC
  -> SPIR-V
  -> checked-in shader artifact files
  -> Aurelian.Assets load
  -> CompiledShaderProgram
  -> Aurelian.Graphics Vulkan pipeline
  -> visible triangle sample
```

The sample does not compile shaders at runtime and does not own language lowering. It consumes the current Aurelian-owned artifact/export boundary.

## Future proof path

Target future proof path:

```text
SDSL-V source
  -> future SDSL-V frontend/lowering
  -> VD-MIR M0
  -> HLSL backend
  -> DXC
  -> SPIR-V
  -> existing shader artifact/export boundary
  -> existing Aurelian runtime/render path
  -> visible triangle
```

The proof target is therefore a compiler insertion upstream of the current artifact boundary, not a renderer rewrite.

## VD-MIR M0 proof shape

`VD-MIR M0` should be just large enough to express the current smoke-triangle shader path:

- module and entry-point shape
- vertex and fragment stage identity
- stage IO needed for `VSMain` and `PSMain`
- value/type model sufficient for position/color passthrough
- provenance strong enough to preserve diagnostics and deterministic artifacts

It should not try to solve the whole future GPU stack in the first proof.

## Upstream of VD-MIR

Upstream of future `VD-MIR`:

- SDSL-V source text
- lexer/parser/validation/frontend work
- source spans, diagnostics, source hashes, and stage intent discovery
- any future lowering that turns source-language semantics into backend-oriented meaning

These are the areas where Copeland-style compiler ownership may later expand if migration is earned.

## Downstream of VD-MIR

Downstream of future `VD-MIR`:

- HLSL backend emission
- DXC invocation and argument policy
- SPIR-V artifact generation
- shader artifact file writing
- runtime shader manifest loading
- `CompiledShaderProgram`
- Vulkan pipeline creation
- visible presentation path

These are already downstream of the likely MIR seam today, even though no explicit MIR exists yet.

## Artifact boundary

The current artifact boundary that should remain stable for the proof is:

```text
shader.toml
  + generated.hlsl
  + VSMain.spv.hex
  + PSMain.spv.hex
  -> Aurelian.Assets
  -> CompiledShaderProgram
```

The first proof should preserve this runtime-facing shape so the compiler experiment can be validated without changing Aurelian runtime file policy.

## Renderer boundary

The renderer boundary that should remain outside `VD-MIR` is:

- window creation
- Vulkan instance/device/swapchain policy
- render pass/framebuffer/pipeline creation
- offscreen image ownership
- compositor dispatch
- acquire/present lifecycle
- engine/runtime session lifecycle
- sample application structure

`VD-MIR` should not absorb renderer ownership just because the first proof target is a visible sample.

## What the proof should demonstrate

The future proof should demonstrate that:

- current smoke-triangle semantics can lower through `VD-MIR M0`
- a `VD-MIR -> HLSL -> DXC -> SPIR-V` path can reproduce the runtime-consumable artifact boundary
- Aurelian can keep loading those artifacts through existing runtime contracts
- the visible triangle sample can still present the same basic proof outcome through existing renderer/runtime ownership

## What the proof should not demonstrate

The future proof should not be required to demonstrate:

- a universal Copeland IR
- a full SDSL-V migration into Copeland
- a Slang backend
- a PTX backend
- a Machina/Aurelian presenter bridge
- a Vulkan architecture rewrite
- a headless or CI-safe visible proof path on day one

## Deferred work

- future reviewer lane: revisit whether the historical Aurelian-hosted `VdMir` slice is the right starting point
- future reviewer lane: prove visible triangle through `VD-MIR -> HLSL/DXC -> SPIR-V` only if that lane is explicitly resumed
- later: decide whether the stable runtime artifact boundary should stay identical or gain optional compiler-side metadata once the first proof exists
