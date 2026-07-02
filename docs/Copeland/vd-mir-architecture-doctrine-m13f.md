# VD-MIR Architecture Doctrine M13f

## Purpose

M13f defines `VD-MIR` as the future common GPU-oriented MIR candidate for Visionary/Copeland.

This milestone is architecture doctrine only:

- `VD-MIR` is not implemented
- no SDSL-V migration is performed
- no HLSL backend behavior changes are performed
- no Slang backend is implemented
- no PTX backend is implemented
- no shader/kernel split is performed
- no Machina/Aurelian/Vulkan bridge is implemented
- `samples/Aurelian.VisibleTriangle` is not wired to `VD-MIR`

M14a later implements the first tiny slice inside `Aurelian.Shaders` only. That implementation does not invalidate this doctrine: `VD-MIR` is still not extracted into Copeland packages, the direct AST-to-HLSL path is still preserved as the default, and visible-triangle runtime wiring remains deferred.

## Name

`VD-MIR` means `Visual Direct MIR`.

Why this name:

- `Visual` means the MIR is oriented toward visual, rendering, and GPU-program workloads in the Visionary stack.
- `Direct` means it should represent backend-lowering facts directly enough to target GPU backends without treating HLSL as the semantic center.
- `MIR` means a middle representation: not the source AST and not the final target ISA or binary form.

M13f adopts `VD-MIR` as the architecture name that replaces the temporary phrase `GPU MIR` in roadmap discussion. M13f does not rename any existing code, tests, or packages.

## Why VD-MIR exists

M13e showed that the active `Aurelian.Shaders` SDSL-V lane already contains MIR pressure without naming it as MIR.

The active path today is:

```text
SDSL-V source
  -> SdslvLexer
  -> SdslvParser
  -> SdslvModule AST
  -> SdslvValidator
  -> HlslEmitter
  -> SdslvStageExtraction
  -> SpirvShaderArtifactEmitter
  -> DxcSpirvCompiler
  -> SPIR-V artifacts / compiled shader export / file writer
```

That path hides backend pressure inside:

- HLSL emission
- stage extraction
- SPIR-V artifact emission
- DXC tool boundaries
- the older `ShaderLowerer`

`VD-MIR` exists so backend-lowering facts can become explicit before HLSL, DXC, SPIR-V, Slang, or PTX details distort the source language shape.

## Relationship to Copeland compiler workshop

M13d defines Copeland as the compiler workshop for Visionary.

`VD-MIR` follows that doctrine:

- it is one lane-specific MIR candidate, not a universal Copeland IR
- it exists because a GPU lane has concrete backend pressure
- it should be promoted only when the lane needs it
- it does not force Markdown, script, numeric, or future lanes into the same representation

`VD-MIR` is therefore a GPU-oriented MIR candidate inside the workshop, not the workshop's one true IR.

## Relationship to M13e SDSL-V audit

M13e documented the current SDSL-V pipeline, backend-neutral concepts, HLSL/DXC-specific concepts, and hidden MIR candidates.

M13f turns that recon into target doctrine:

- M13e asked whether a common GPU MIR should exist
- M13f names that future MIR `VD-MIR`
- M13f defines what `VD-MIR` is and is not
- M13f defines the smallest staged architecture for reaching it
- M13f keeps the one-common-MIR starting assumption

M13f still does not implement the first slice.

## What VD-MIR is

`VD-MIR is the common GPU-oriented MIR candidate for Visionary.`

It is:

- backend-lowering-shaped
- source-provenance-preserving
- target-aware
- capability-checkable
- artifact-friendly

It should hold the common execution and resource facts needed before backend-specific lowering:

- entry points
- functions
- typed values
- control flow
- stage/kernel metadata
- resource and binding facts
- built-ins
- target and capability requirements
- provenance needed for diagnostics and artifact reporting

## What VD-MIR is not

`VD-MIR` is not:

- a universal Copeland IR
- a Markdown/document IR
- an Oct/numeric IR
- a JavaScript/TypeScript runtime model
- secretly HLSL
- secretly SPIR-V
- secretly PTX
- a Vulkan object model
- an Aurelian renderer contract

It should not absorb runtime-facing concerns that belong elsewhere:

- `CompiledShaderProgram`
- `shader.toml`
- renderer-owned contract semantics
- Vulkan device or pipeline object modeling

## One common MIR starting assumption

Use one `VD-MIR` for shader and compute GPU programs until evidence proves a split is necessary.

Why start there:

- SPIR-V and PTX overlap on typed values, functions, control flow, memory and address-space concerns, built-ins, barriers, and entry points.
- the immediate problem is shared backend pressure, not proof that two separate MIRs are already required
- one common MIR keeps the first implementation slice narrower and easier to validate

Graphics-versus-compute differences can initially live in:

- entry-point metadata
- `StageKind` or `KernelKind`
- capabilities
- resource and binding rules
- built-in availability
- backend validation passes

## Shader/Kernel split criteria

Do not split now.

Only split if:

- graphics-stage concepts repeatedly pollute compute-only kernels
- PTX thread or memory semantics repeatedly distort graphics shaders
- validations require incompatible core invariants
- target-specific escape hatches become pervasive
- tests prove the common representation harms clarity more than reuse

Until those conditions are repeated and concrete, one `VD-MIR` remains the preferred doctrine.

## Candidate concept inventory

M13f records a conceptual inventory, not a C# API surface.

Candidate concepts:

- `Module`
- `EntryPoint`
- `Function`
- `Parameter`
- `BasicBlock`
- `Instruction`
- `Value`
- `Type`
- `ScalarType`
- `VectorType`
- `MatrixType`
- `Pointer`
- `AddressSpace`
- `Buffer`
- `Resource`
- `Binding`
- `BuiltIn`
- `Input`
- `Output`
- `StageKind`
- `KernelKind`
- `ThreadBuiltIn`
- `WorkgroupBuiltIn`
- `Barrier`
- `Synchronization`
- `Intrinsic`
- `Capability`
- `TargetProfile`
- `Metadata`
- `SourceSpan`
- `DiagnosticAttachment`
- `ArtifactProvenance`

Discipline:

- concept inventory is not implementation
- no concrete C# records or classes are defined in M13f
- no package folders are created in M13f
- the model must not be overfit to SPIR-V or PTX instruction shapes
- the first implementation slice should start from the smallest set it actually needs

## Staged architecture

Target staging:

```text
VD-MIR
  -> backend-specific lowering and validation
  -> target emission and tool invocation
  -> artifacts
```

The smallest plausible staged architecture is:

```text
SDSL-V frontend
  -> source-shaped AST and validation
  -> VD-MIR
  -> backend lowering
  -> target/tool boundary
  -> artifacts and bridge exports
```

That staged architecture keeps source-language meaning, MIR meaning, backend meaning, and artifact meaning separate.

## VD-MIR M0

`VD-MIR M0` should be just enough to represent the current SDSL-V smoke-triangle path before HLSL emission.

Target shape:

```text
SDSL-V AST
  -> VD-MIR
  -> HLSL backend
  -> existing DXC/SPIR-V path
```

M0 should stay intentionally small:

- enough module and entry-point structure for the smoke-triangle path
- enough value and type structure for the current shader bodies
- enough stage metadata to stop hard-coding all stage facts in later extraction
- enough provenance to preserve useful diagnostics

M13f does not implement M0. M14a later lands the minimal smoke-triangle compiler slice in `src/Aurelian.Shaders/Language/VdMir` without promoting that code into Copeland packages yet.

## VD-MIR M1

`VD-MIR M1` should make stage IO, built-ins, resources, and bindings strong enough to replace current hard-coded stage extraction assumptions.

The goal is to move stage-lowering facts out of ad hoc HLSL-side reconstruction and into explicit compiler state.

Likely doctrine targets:

- stronger `EntryPoint` stage metadata
- explicit input/output shape
- explicit built-in availability
- explicit resource and binding model
- clearer capability and target-profile checks

## VD-MIR M2

`VD-MIR M2` should add the compute and kernel model:

- kernel entry points
- thread and workgroup built-ins
- address spaces
- barriers and synchronization

This is the stage where the one-common-MIR assumption is exercised against real compute pressure rather than treated as theory.

## VD-MIR M3

`VD-MIR M3` should make multi-backend feasibility concrete enough that these become implementable target paths:

- `HLSL/DXC`
- `Slang`
- `PTX`

M3 is about feasibility and backend structure, not premature optimization.

## Backend model

Backend doctrine:

```text
VD-MIR
  -> backend-specific lowering/validation
  -> target emission/tool invocation
  -> artifacts
```

That means:

- backend syntax belongs in the backend
- tool discovery and subprocess policy belong at the tool boundary
- target binaries and text remain artifacts
- target capability validation belongs close to backend lowering, not in the source AST

## HLSL/DXC backend

Current path:

```text
SDSL-V -> HLSL -> DXC -> SPIR-V
```

Future path:

```text
SDSL-V -> VD-MIR -> HLSL backend -> DXC -> SPIR-V
```

HLSL/DXC doctrine:

- HLSL spelling belongs in the backend
- DXC discovery, arguments, and subprocess invocation belong in the tool boundary
- SPIR-V artifacts remain explicit artifacts
- HLSL/DXC are backend mechanisms, not the semantic center of the pipeline

M13f does not change the current HLSL/DXC path.

## Slang backend

Future path:

```text
VD-MIR -> Slang backend -> SPIR-V
```

Why it stays backend-side:

- it is a second SPIR-V route, not the meaning of the language
- it can act as a cross-check against HLSL/DXC lowering
- it may offer a cleaner shader-language backend target later

M13f adds no Slang dependency, package, or implementation.

## PTX backend

Future path:

```text
VD-MIR -> PTX backend
```

Why it stays backend-side:

- PTX is a target route, not the semantic center
- compute-specific execution details should lower from shared GPU-oriented facts first
- PTX pressure should inform split decisions only after repeated evidence appears

Concepts PTX will likely need later:

- kernel entry points
- grid, block, and thread built-ins
- address spaces
- memory operations
- barriers
- scalar and vector operations
- target intrinsics
- calling-convention metadata

M13f adds no PTX implementation.

## Source provenance and diagnostics

`VD-MIR` should preserve source provenance without preserving full source syntax shape.

Doctrine:

- `SourceSpan` remains recoverable for diagnostics
- `DiagnosticAttachment` can connect backend or validation issues to MIR entities
- provenance must survive lowering well enough to explain backend failures
- diagnostics remain phase-specific rather than flattened into one generic error channel

This is one reason `VD-MIR` must be source-provenance-preserving rather than syntax-preserving.

## Artifact policy

`VD-MIR` is compiler-internal.

Artifacts remain explicit:

- emitted HLSL
- emitted SPIR-V
- future Slang outputs
- future PTX outputs
- manifests
- compiled shader bridge exports

Artifact-friendly means `VD-MIR` should support deterministic provenance, capability, and target reporting. It does not mean `VD-MIR` should replace the artifact boundary.

## Relationship to Aurelian.Shaders

`Aurelian.Shaders` remains the home of the active SDSL-V lane today.

Doctrine:

- SDSL-V should eventually lower into `VD-MIR`
- `VD-MIR` should then lower through targeted backends such as `HLSL/DXC`, `Slang`, and `PTX`
- M13f does not move SDSL-V into Copeland
- M13f does not create `Copeland.Frontends.Sdslv`
- M13f does not create `Copeland.Mir.Vd` or `Copeland.Mir.VdMir`

M13f therefore changes the target architecture, not current ownership.

## Relationship to Aurelian.VisibleTriangle

`samples/Aurelian.VisibleTriangle` now exists in the repository and is acknowledged as an important future proof target.

M13f doctrine:

- the sample is not wired to `VD-MIR` in M13f
- the sample remains a current Aurelian render and shader proof baseline
- near-term value is sample topology checking, asset/audit understanding, and proof-boundary planning
- once `VD-MIR M0` exists, the sample is a natural candidate for the first visible proof

Possible future sequence:

```text
M13g:
  Aurelian.VisibleTriangle sample topology and proof-boundary audit

M14a:
  VD-MIR M0 implementation for smoke triangle

M14b:
  visible triangle proof through VD-MIR -> HLSL/DXC -> SPIR-V
```

This sequence is doctrine only in M13f.

## Relationship to Aurelian.Rendering.Contracts

`Aurelian.Rendering.Contracts` remains Aurelian-owned.

Doctrine:

- renderer-facing contracts remain Aurelian-owned
- `VD-MIR` is compiler-internal
- compiled shader program export remains a bridge and artifact boundary
- `VD-MIR` should not absorb `CompiledShaderProgram`
- `VD-MIR` should not absorb `shader.toml` runtime file policy

This keeps compiler semantics and renderer contract semantics cleanly separated.

## Relationship to Machina

Machina may later present `VD-MIR` diagnostics, manifests, and proof artifacts.

But:

- Machina should not own `VD-MIR` semantics
- Machina should not become the home of GPU compiler meaning
- M13f adds no Machina/Aurelian/VD-MIR bridge

Machina remains a presentation and workbench lane, not a GPU compiler core.

## Relationship to Dominatus

Dominatus may later orchestrate compiler or render effects that involve `VD-MIR` artifacts or diagnostics.

But:

- Dominatus should not own compiler semantics
- `VD-MIR` meaning should not migrate into orchestration layers
- M13f adds no Dominatus integration

Dominatus remains control-plane infrastructure.

## Naming and future package guidance

Architecture naming:

- docs should use `VD-MIR`
- expanded name is `Visual Direct MIR`
- the former temporary phrase `GPU MIR` is now replaced in doctrine discussion

Likely future code spelling:

```text
VdMir
```

Possible future package names:

```text
Copeland.Mir.Vd
Copeland.Mir.VdMir
```

M13f creates neither package.

## What changed

M13f adds doctrine only:

- `VD-MIR` is named as `Visual Direct MIR`
- the common GPU-oriented MIR target is defined more precisely
- one-common-MIR doctrine is preserved
- split criteria are defined without splitting now
- staged architecture and milestone model are documented
- backend doctrine is clarified so HLSL/DXC, Slang, and PTX are backends, not semantic centers
- `Aurelian.VisibleTriangle` is acknowledged as a future proof target without wiring it in

## What did not change

M13f does not:

- implement `VD-MIR`
- create MIR types
- create implementation packages
- migrate SDSL-V into Copeland
- change current HLSL backend behavior
- implement Slang
- implement PTX
- split Shader MIR and Kernel MIR
- wire Machina, Aurelian, or Vulkan to `VD-MIR`
- rename the repository

Runtime behavior is unchanged.

## Deferred work

- M13g later performs the topology and proof-boundary audit for `samples/Aurelian.VisibleTriangle` without changing the sample's current shader/runtime/render path and without adding any `VD-MIR` implementation.
- `M13g`: audit `Aurelian.VisibleTriangle` topology and proof boundaries
- `M14a`: implement `VD-MIR M0` for the smoke-triangle path
- `M14b`: prove visible triangle through `VD-MIR -> HLSL/DXC -> SPIR-V`
- later: evaluate whether `VD-MIR M1`, `M2`, and `M3` converge cleanly enough to keep one common MIR
- `M4+`: add optimization or canonicalization passes only if repeated concrete pressure earns them
