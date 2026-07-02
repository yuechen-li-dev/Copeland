# Copeland GPU MIR Target Analysis M13e

## Purpose

M13e records why a future GPU MIR was being considered for Visionary and why the starting assumption should be one shared GPU MIR rather than an immediate Shader MIR / Kernel MIR split.

M13f now names that future architecture target `VD-MIR` (`Visual Direct MIR`). This M13e document remains valid as the recon and pressure analysis that led into the M13f doctrine.

This is target analysis only:

- no `GpuMir` or `VD-MIR` implementation
- no SDSL-V migration
- no Slang backend implementation
- no PTX backend implementation
- no shader/kernel MIR split

## Why a GPU MIR is being considered

The current Aurelian SDSL-V lane already has backend pressure that no longer fits comfortably inside a pure source-shaped AST:

- stage entry points
- backend stage profiles
- HLSL semantics and built-ins
- DXC/SPIR-V tool metadata
- per-stage artifact hashing and provenance
- stream usage and stage IO shaping

Today those concerns are spread across:

- HLSL emission
- stage extraction
- SPIR-V artifact emission
- DXC tool boundaries
- older legacy lowering helpers

A future GPU MIR is being considered so those concerns can become explicit without secretly baking in HLSL, DXC, SPIR-V, or PTX as the meaning of the language.

## Current SDSL-V pressure

The current active path is:

```text
SDSL-V
  -> lexer/parser/AST
  -> validation
  -> HLSL emission
  -> DXC
  -> SPIR-V
```

The lane has no explicit MIR today.

Pressure points already visible:

- backend-neutral stage intent is mixed with HLSL spelling
- semantic validation is not where target capability checks belong
- SPIR-V and DXC metadata have no neutral representation home
- future compute/PTX work would otherwise either fork directly from AST or distort the HLSL emitter

## GPU MIR doctrine

A future `GpuMir` should be:

- backend-lowering-shaped
- not source-shaped
- not secretly HLSL
- not secretly SPIR-V
- not secretly PTX

It should represent the common execution and data model facts needed by multiple GPU-oriented backends while leaving target-specific details to validation and lowering.

Guiding doctrine:

- source AST preserves language shape
- GPU MIR preserves backend-relevant execution shape
- backend emitters own target syntax, binary format, and tool invocation
- target capability checks belong near backend validation, not in the MIR core

## Non-goals

- not a universal IR for all Copeland lanes
- not a demand that Markdown, script, or numeric lanes share it
- not an excuse to move Aurelian code before the architecture earns it
- not a hidden HLSL AST
- not a direct SPIR-V object model clone
- not a PTX ISA mirror

## Candidate concept model

A plausible future concept inventory for a shared GPU MIR:

- `Module`
- `EntryPoint`
- `Function`
- `BasicBlock`
- `Instruction`
- `Value`
- `Type`
- `ScalarType`
- `VectorType`
- `MatrixType`
- `Pointer` and `AddressSpace`
- `Buffer` or `Resource`
- `Binding`
- `BuiltIn`, `Input`, and `Output`
- `Thread` and `Workgroup` built-ins
- `Barrier` and synchronization operations
- `Intrinsic`
- `Metadata`
- diagnostic/source-span attachment

Important discipline:

- this list is conceptual, not a C# API
- M13e does not define full type hierarchies
- a future implementation should start from the smallest set the first two backends actually need

## One GPU MIR vs Shader/Kernel split

Starting assumption:

```text
Use one common GPU MIR first.
Do not split shader MIR and kernel MIR unless real evidence proves the common shape is failing.
```

Rationale:

- SPIR-V and PTX overlap on many core GPU concepts: typed values, functions, control flow, memory operations, address spaces, built-ins, barriers, and entry points.
- graphics shaders and compute kernels both need GPU-oriented execution metadata, not radically different whole-IR theories.
- the immediate problem is not “invent two perfect MIRs”; it is “stop encoding backend pressure directly inside source AST emitters.”
- a common MIR keeps initial architecture simpler and makes shared validation/lowering opportunities visible.

The graphics-versus-compute distinction can initially live in:

- entry-point metadata
- stage or kernel kind
- capability requirements
- resource/binding usage
- built-in availability
- backend validation rules

## Split criteria

Only split into separate Shader MIR and Kernel MIR if the common representation causes persistent distortion such as:

- graphics-stage concepts repeatedly polluting compute-only kernels
- PTX thread/memory semantics repeatedly distorting graphics shader representation
- backend validations requiring incompatible core invariants
- too many target-specific escape hatches
- tests showing the shared representation is harming clarity more than helping reuse

Until those symptoms are real and repeated, one `GpuMir` was the better starting doctrine in M13e and one `VD-MIR` is the named continuation of that doctrine in M13f.

## HLSL/DXC backend sketch

Current and likely future path:

```text
today:
  SDSL-V
    -> HLSL
    -> DXC
    -> SPIR-V

future candidate:
  SDSL-V
    -> GpuMir
    -> HLSL backend
    -> DXC
    -> SPIR-V
```

What current code already proves:

- HLSL source emission exists
- DXC subprocess invocation exists
- SPIR-V artifact wrapping exists
- compiled shader export into Aurelian contracts exists

What is HLSL/DXC-specific and should likely stay backend-side:

- HLSL spelling of structs, functions, and semantics
- `vs_6_0` / `ps_6_0` / `cs_6_0` profile naming
- DXC command-line arguments and discovery
- DXC validation and subprocess diagnostics

Likely future backend need:

- lower generic GPU MIR entry-point IO into HLSL signatures and semantics
- map MIR built-ins/resources/address spaces to HLSL-compatible forms
- validate stage-specific capability/profile requirements before DXC

## Slang backend sketch

Future candidate:

```text
GpuMir
  -> Slang source or Slang integration boundary
  -> SPIR-V
```

Why Slang may be useful:

- a second SPIR-V route alongside DXC
- cross-check against the HLSL/DXC backend
- potentially cleaner target abstraction for some shader constructs

Current M13e stance:

- no Slang dependency is added
- no Slang implementation is added
- no package is created

Known unknowns:

- whether Slang should be treated as source emission, API integration, or both
- how much its resource/binding model should shape backend lowering
- whether it materially reduces backend distortion compared with HLSL emission

## PTX backend sketch

Future candidate:

```text
GpuMir
  -> PTX
```

Concepts PTX would likely need from a shared GPU MIR:

- kernel entry points
- grid/block/thread built-ins
- address spaces
- explicit memory operations
- barriers and synchronization
- scalar and vector arithmetic
- target intrinsics
- calling convention metadata

Likely current gaps visible from the SDSL-V lane:

- no explicit kernel launch or compute-grid model
- no first-class address-space model
- no backend-neutral barrier/synchronization model
- no explicit resource/buffer binding model suitable for compute

Those are arguments for a future GPU MIR, not arguments for implementing PTX during M13e.

## Source-span and diagnostics policy

A future GPU MIR should preserve source attachment for diagnostics, but it should not inherit the entire source AST shape.

Policy direction:

- source spans stay attached where they help explain backend validation and lowering failures
- diagnostics remain phase-specific
- backend diagnostics may cite MIR entities, but source provenance should remain recoverable

## Artifact policy

A future GPU MIR should not replace artifact boundaries.

Artifact doctrine:

- MIR is an internal compiler representation
- emitted HLSL, emitted SPIR-V, emitted PTX, manifests, and compiled shader contracts remain explicit artifacts
- Aurelian-owned runtime file and contract policy remains outside MIR core design

## Relationship to Aurelian

M13e does not move SDSL-V out of `Aurelian.Shaders`.

Relationship doctrine:

- Aurelian currently owns the active SDSL-V lane
- Aurelian continues to own renderer-facing contracts and runtime artifact policy
- Copeland may eventually host shared frontend/backend pieces only after follow-up milestones prove the split

## Relationship to Copeland workshop doctrine

This analysis follows M13d doctrine:

- no universal IR mandate
- explicit compiler lanes
- shared abstractions promoted only after repeated concrete shape
- no `Copeland.Shaders` umbrella monolith

`GpuMir` is therefore a possible GPU-lane MIR, not a whole-workshop MIR.

## Deferred work

- M13f should refine the target architecture before code moves and name the target architecture `VD-MIR`
- M13g should audit `samples/Aurelian.VisibleTriangle` so the first visible proof target has an explicit topology and ownership boundary before `VD-MIR` implementation starts
- later milestones may extract frontend/backend seams if the lane audit and MIR doctrine keep converging
- actual `GpuMir` / `VD-MIR`, Slang, and PTX implementation all remain deferred
