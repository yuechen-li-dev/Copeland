# Aurelian VD-MIR M0 Smoke Triangle M14a

## Purpose

M14a implements the first tiny `VD-MIR` compiler slice for the active smoke-triangle SDSL-V path inside `Aurelian.Shaders`.

## Scope

This milestone is compiler-side only:

- add a minimal `VD-MIR M0` model under `src/Aurelian/Aurelian.Shaders/Language/VdMir`
- lower the current smoke-triangle SDSL-V AST into that model
- emit HLSL from `VD-MIR`
- optionally prove DXC/SPIR-V through the existing infrastructure
- preserve the direct AST-to-HLSL path as the default behavior

## Current baseline path

```text
SDSL-V
  -> SdslvLexer
  -> SdslvParser
  -> SdslvModule AST
  -> SdslvValidator
  -> HlslEmitter
  -> DXC/SPIR-V artifact path
```

## New VD-MIR M0 proof path

```text
SDSL-V
  -> SdslvLexer
  -> SdslvParser
  -> SdslvModule AST
  -> SdslvValidator
  -> VdMirM0Lowerer
  -> VdMirModule
  -> VdMirHlslEmitter
  -> existing DXC/SPIR-V path
```

## VD-MIR M0 model

Implemented concepts:

- module
- stage entry points
- vertex/pixel stage kinds
- struct-like stage IO
- scalar/vector/matrix/struct type spellings needed by smoke triangle
- local/assign/return/expression statements
- simple identifier/literal/field-access/call/binary/unary expressions
- semantic metadata as MIR enums rather than raw HLSL strings
- source-span provenance where the current AST exposes it
- diagnostic collection

## Lowering from SDSL-V AST

`VdMirM0Lowerer` lowers the validated active AST and supports only the smoke-triangle subset. Unsupported declarations, shader shapes, statements, expressions, and types return structured diagnostics instead of crashing or pretending the whole language is supported.

## HLSL emission from VD-MIR

`VdMirHlslEmitter` consumes only `VD-MIR`, not the source AST. HLSL spelling stays in the backend: MIR stores semantic kinds like `SvPosition` and `SvTarget0`, and the emitter maps those to HLSL semantics during text generation.

## DXC/SPIR-V proof

`VdMirSmokeTriangleArtifact` routes emitted HLSL through the existing `SpirvShaderArtifactEmitter` path. In the current environment on July 1, 2026, DXC was available, so deterministic `.spv.hex` proof artifacts were generated under `artifacts/m14a`.

## Runtime artifact boundary

The runtime boundary is preserved. M14a writes proof artifacts only under `artifacts/m14a` and does not rewrite `samples/Aurelian/Aurelian.VisibleTriangle/Assets/Shaders/SmokeTriangle/*`. The sample still loads its existing checked-in `shader.toml`, `generated.hlsl`, `VSMain.spv.hex`, and `PSMain.spv.hex`.

## Tests

Added coverage includes:

- `VdMirM0Lowerer_*`
- `VdMirHlslEmitter_*`
- `VdMirSmokeTriangle_CanCompileToSpirvThroughExistingDxcPath`
- `M14aManifest_*`
- `M14aProofArtifacts_AreWrittenDeterministically`
- `M14a_*` boundary guards

## Validation results

The shader test project passes with the new slice, and the proof artifact writer generated deterministic M14a outputs. Broader solution validation results are recorded with the milestone closeout.

## What changed

- added `src/Aurelian/Aurelian.Shaders/Language/VdMir/*`
- added opt-in compiler proof artifact generation
- added `artifacts/m14a/*`
- added tests for lowering, emission, artifacts, and boundaries

## What did not change

- visible triangle runtime is not wired to `VD-MIR`
- renderer/runtime behavior is unchanged
- direct AST-to-HLSL remains the default path
- no Slang/PTX backend exists
- no shader/kernel split exists
- no Copeland package extraction happened

## Deferred work

- later visible-triangle wiring proof through the existing artifact boundary
- broader stage IO/resource/binding model work beyond M0
- compute/kernel model
- Slang/PTX backends
- any Copeland package extraction only after repeated pressure earns it
