# VD-MIR M0 Implementation Slice M14a

## Purpose

M14a turns the M13f doctrine into a minimal real compiler seam: one smoke-triangle-sized `VD-MIR M0` inside `Aurelian.Shaders`.

## Why M0 is intentionally tiny

M0 is intentionally small so the repo proves one real lowering seam without overclaiming a final MIR shape. It is only large enough to represent the current smoke-triangle shader path.

## M0 supported concepts

- `Module`
- `EntryPoint`
- `StageKind`
- struct-like stage IO
- scalar/vector/matrix/struct types needed by smoke triangle
- local/assign/return/expression statements
- simple identifier/literal/field-access/call/binary/unary expressions
- semantic metadata for current HLSL emission
- source provenance and diagnostics

## M0 unsupported concepts

- general SSA
- general CFG
- resource/binding model beyond the smoke path
- compute/kernel model
- address spaces
- barriers/synchronization
- Slang/PTX-specific shapes
- SPIR-V object model
- Vulkan/runtime/renderer contracts

## Pipeline shape

```text
SDSL-V source
  -> lexer/parser/validator
  -> VD-MIR M0
  -> VD-MIR HLSL backend
  -> existing DXC/SPIR-V artifact path
```

The existing direct path remains:

```text
SDSL-V source -> HlslEmitter -> DXC/SPIR-V
```

## Backend boundary

M14a keeps HLSL spelling in the backend. `VD-MIR` stores semantic kinds, not raw HLSL semantic strings as its public meaning. DXC invocation remains outside the MIR.

## Artifact boundary

The runtime artifact boundary is unchanged. M14a writes proof artifacts only under `artifacts/m14a` and leaves `samples/Aurelian.VisibleTriangle` on its existing checked-in artifact set.

## Relationship to M13f doctrine

M14a follows the M13f doctrine exactly:

- implement only the smallest useful M0 slice
- keep it inside `Aurelian.Shaders` for now
- do not extract Copeland packages yet
- do not implement Slang/PTX
- do not split shader and kernel MIR

## Relationship to visible triangle proof

M14a is upstream of the visible sample only. It proves that smoke triangle can lower through `VD-MIR` and still reach the existing artifact path, but the visible sample runtime is not wired to that path yet.

## What this proves

- a minimal `VD-MIR M0` can exist as a real compiler seam
- the current smoke-triangle AST can lower into it
- HLSL can be emitted from MIR rather than directly from AST
- the existing DXC/SPIR-V infrastructure can still be reused from that proof path

## What this does not prove

- final MIR architecture
- compute/kernel viability
- Slang/PTX backend viability
- runtime sample integration
- renderer architecture changes
- Copeland package extraction viability

## Deferred work

- visible-triangle runtime proof through `VD-MIR`
- stronger stage IO/resource metadata
- compute/kernel expansion
- multi-backend expansion
- extraction into broader Copeland packages only if later milestones earn it
