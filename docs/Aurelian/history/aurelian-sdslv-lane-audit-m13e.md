# Aurelian SDSL-V Lane Audit M13e

## Purpose

M13e audits the current `Aurelian.Shaders` SDSL-V lane in place and records GPU MIR target analysis pressure without moving code or changing behavior.

M13f later names that future architecture target `VD-MIR` (`Visual Direct MIR`). This M13e document remains the source audit that motivates the M13f doctrine.

This milestone is recon only:

- no SDSL-V migration into Copeland
- no `GpuMir` or `VD-MIR` implementation
- no Slang backend implementation
- no PTX backend implementation
- no shader/kernel MIR split

M14a later implements the first tiny `VD-MIR M0` slice as a compiler seam inside `Aurelian.Shaders`, but this audit still describes the baseline lane that motivated that insertion.

The starting assumption is one common GPU MIR until real evidence proves a split is necessary.

## Current SDSL-V pipeline

The active tested SDSL-V path in `src/Aurelian/Aurelian.Shaders/Language/*` is:

```text
SDSL-V source text
  -> SdslvLexer
  -> SdslvParser
  -> SdslvModule AST
  -> SdslvValidator
  -> HlslEmitter
  -> SdslvStageExtraction
  -> SpirvShaderArtifactEmitter
  -> DxcSpirvCompiler
  -> SPIR-V artifact / compiled shader program export / file writer
```

Real stage boundaries:

- source becomes AST in `Language/Parsing/SdslvParser.cs`
- AST becomes HLSL in `Language/Emission/Hlsl/HlslEmitter.cs`
- DXC is invoked in `Language/External/Dxc/DxcSpirvCompiler.cs`
- SPIR-V stage artifacts are represented in `Language/Artifacts/Spirv/*`
- SDSL-V-to-SPIR-V end-to-end artifact wrapping lives in `Language/Artifacts/SdslvSpirv/*`
- renderer-facing compiled program export lives in `Language/Artifacts/Compiled/CompiledShaderProgramExporter.cs`

The older top-level path still exists:

```text
legacy SDSL-like source
  -> ShaderLexer
  -> ShaderParser
  -> ShaderLowerer / StreamUsageAnalyzer
  -> HLSL text
```

That path is still useful as audit evidence because it already contains backend-shaped concepts, but it is not the primary tested M13e SDSL-V route.

## Source layout

Actual audited source layout under `src/Aurelian/Aurelian.Shaders`:

| Path | Current purpose | Lane classification | Neutral vs specific | Likely future home if migration is earned | Move risk | Relevant tests | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `Language/Lexing` | Current SDSL-V lexer | Frontend, Lexer, Diagnostics, Copeland Candidate | Backend-neutral | `Copeland.Frontends.Sdslv` | Low | `SdslvLexerTests` | Active path |
| `Language/Tokens` | Current token model | Frontend, Lexer, Copeland Candidate | Backend-neutral | `Copeland.Frontends.Sdslv` or shared lane-local frontend package | Low | `SdslvLexerTests`, parser tests | Active path |
| `Language/Parsing` | Current parser and parse result | Frontend, Parser, Diagnostics, Copeland Candidate | Backend-neutral | `Copeland.Frontends.Sdslv` | Low | `SdslvParserM0Tests`, `SdslvParserM1ExpressionTests`, `SdslvParserM1StatementTests` | Active path |
| `Language/Ast` | Current SDSL-V syntax model | AST, Aurelian-Owned today, Copeland Candidate later | Mostly backend-neutral | likely stays lane-specific until migration is earned | Medium | `SdslvAstContractTests`, parser tests | Contains shader and non-shader language forms |
| `Language/Validation` | Current semantic checks | Semantic Model, Diagnostics, Copeland Candidate | Mostly backend-neutral | `Copeland.Frontends.Sdslv` or later SDSL-V semantic package | Medium | `SdslvValidationM0Tests` | Mostly name/type/form validation, not backend validation |
| `Language/Emission/Hlsl` | Current AST-to-HLSL emission | Backend Emitter, Hidden MIR Candidate | HLSL-specific | `Copeland.Backends.Hlsl` | Medium-high | `HlslEmissionM0Tests` | Current backend is direct emitter, not MIR-based |
| `Language/External/Dxc` | DXC discovery, args, subprocess, validation | Tool Boundary, Diagnostics | DXC-specific | `Copeland.Backends.Hlsl` tooling boundary or separate tool package | Medium | `DxcSubprocessM0Tests`, `DxcValidationM0Tests`, SPIR-V tests | Explicit external tool seam |
| `Language/Artifacts` | SDSL-V artifact records, JSON, stage lists | Artifact Boundary, Diagnostics | Mostly backend-neutral wrapping over HLSL path | split between frontend/backend artifact packages later | Medium | `SdslvArtifactM0Tests` | Records source hash, HLSL, inferred stage metadata |
| `Language/Artifacts/SdslvSpirv` | SDSL-V to SPIR-V end-to-end wrapper | Artifact Boundary, DXC/SPIR-V boundary | Mixed, strongly HLSL/DXC-shaped today | likely transitional wrapper around future GPU MIR + backend | High | `SdslvSpirvArtifactM0Tests` | Encodes current pipeline assumption directly |
| `Language/Artifacts/Spirv` | HLSL stage source to SPIR-V artifacts | Artifact Boundary, Tool Boundary | SPIR-V artifact-specific and DXC-shaped | future backend artifact layer | Medium-high | `SpirvShaderArtifactM0Tests` | Stage model already includes entry point, profile, arguments, hashes |
| `Language/Artifacts/Compiled` | Export to renderer contract `CompiledShaderProgram` | Renderer-Facing Contract, Aurelian-Owned | Aurelian-owned contract boundary | likely remain Aurelian-side bridge/export | High | `CompiledShaderProgramExporterM0Tests` | Ties compiler artifacts to render contracts |
| `Language/Artifacts/Files` | Writes `shader.toml`, `.spv`, `generated.hlsl` | Artifact Writing/Loading, Renderer-Facing Contract | Mixed; file semantics are Aurelian artifact policy | likely remain bridge/package-local | Medium-high | `ShaderArtifactFileWriterM0Tests` | Important runtime/asset seam |
| `Language/Diagnostics` | Current SDSL-V diagnostic model | Diagnostics, Copeland Candidate | Backend-neutral | shared only if another lane proves same shape | Low | lexer/parser/validation/emission tests | Phase/severity/span model is reusable in principle |
| `Lexing`, `Parsing`, `Lowering`, `Ast`, `Diagnostics`, `Artifacts` at top level | Older shader lane path | Frontend, Lowering, Hidden MIR Candidate, Artifact Boundary | Mixed; older HLSL-shaped path | likely audit-only until a deliberate cleanup milestone | Medium-high | `ShaderPipelineIdentityTests` | Coexists with current `Language/*` lane |

## Test layout

Actual audited test layout under `tests/Aurelian/Aurelian.Shaders.Tests`:

| Tests | Coverage role |
| --- | --- |
| `SdslvLexerTests.cs` | current lexer coverage |
| `SdslvParserM0Tests.cs` | module/type/shader parse coverage |
| `SdslvParserM1ExpressionTests.cs` | richer expression grammar coverage |
| `SdslvParserM1StatementTests.cs` | richer statement grammar coverage |
| `SdslvAstContractTests.cs` | AST record shape and invariants |
| `SdslvValidationM0Tests.cs` | semantic validation coverage |
| `HlslEmissionM0Tests.cs` | AST-to-HLSL backend emission coverage |
| `SdslvArtifactM0Tests.cs` | SDSL-V artifact and stage metadata coverage |
| `SpirvShaderArtifactM0Tests.cs` | HLSL-stage-to-SPIR-V artifact coverage |
| `SdslvSpirvArtifactM0Tests.cs` | end-to-end SDSL-V to SPIR-V coverage |
| `DxcSubprocessM0Tests.cs` | DXC subprocess contract coverage |
| `DxcValidationM0Tests.cs` | DXC validation/reporting coverage |
| `ShaderArtifactFileWriterM0Tests.cs` | file artifact writing coverage |
| `CompiledShaderProgramExporterM0Tests.cs` | renderer-facing compiled program export coverage |
| `ShaderPipelineIdentityTests.cs` | older top-level lexer/diagnostic smoke coverage |

Fixtures:

- `Fixtures/Sdslv/smoke_triangle.sdslv`
- `Fixtures/Hlsl/tiny_triangle_vs.hlsl`
- `Fixtures/Hlsl/tiny_triangle_ps.hlsl`

The active corpus is intentionally small and deterministic. It proves the lane shape more than a broad language surface.

## Compiler-lane classification

High-level role split:

- Frontend mechanics: `Language/Lexing`, `Language/Tokens`, `Language/Parsing`
- Lexer: `Language/Lexing`
- Parser: `Language/Parsing`
- AST/syntax model: `Language/Ast`
- Semantic model: `Language/Validation`
- Lowering: direct AST-to-HLSL lowering is currently embedded in `Language/Emission/Hlsl`
- Backend emission: `Language/Emission/Hlsl`
- Artifact writing/loading: `Language/Artifacts/*`, `Language/Artifacts/Files/*`
- Diagnostics: `Language/Diagnostics`, plus backend/tool-specific diagnostics in `Artifacts/*` and `External/Dxc/*`
- DXC/SPIR-V tool boundary: `Language/External/Dxc`, `Language/Artifacts/Spirv`
- Tests/corpus: `tests/Aurelian/Aurelian.Shaders.Tests`

Important audit finding:

- the active SDSL-V lane has no explicit MIR layer today
- the lane goes from validated AST straight into HLSL emission and backend/tool wrappers
- some MIR-like pressure is therefore hidden inside emission, stage extraction, and artifact records rather than in a named intermediate representation

## Frontend mechanics

The current frontend is explicit and lane-local:

- `SdslvLexer.Lex` tokenizes source text into `SdslvToken`
- `SdslvParser.ParseModule` builds `SdslvModule`
- parser diagnostics are carried forward in `SdslvParseResult`
- `SdslvValidator.ValidateModule` performs top-level name, duplicate, and basic type/shape validation

This frontend is backend-neutral in structure even though the language is meant for GPU work.

## Lexer

Current lexer observations:

- supports namespaces, uses, records, streams, enums, interfaces, shaders, material blocks, stage methods, functions, flow constructs, expressions, arrays, `try`, `unwrap`, `match`, `switch`, and booleans
- comments, spans, and deterministic token kinds are explicit
- diagnostics are source-spanned and phase-tagged

The lexer is clearly a Copeland-frontends candidate if SDSL-V is ever moved, but no move is performed in M13e.

## Parser

Current parser observations:

- module parser handles namespace/use and top-level declarations
- parser M0 currently treats `flow`, `interface`, and `compile` as recognized-but-not-supported top-level forms during module parsing
- statement and expression parsing already extends beyond the tiny smoke shader
- stage methods are represented as ordinary function declarations with optional stage text

This means the source language surface is already larger than the current backend path can fully emit.

## AST and syntax model

Current active AST surface in `Language/Ast` includes:

- module/use structure
- type aliases
- records
- streams
- enums
- shaders
- interfaces
- flow declarations
- compile declarations
- functions, statements, and expressions
- type refs and source spans

The AST is source-shaped, not backend-shaped. It should not be mistaken for MIR.

The older top-level `Ast/ShaderAst.cs` is a separate, earlier syntax model centered on:

- `SdslShader`
- `SdslStream`
- `SdslStageMethod`
- `BaseCall`
- `HlslDocument`

That older shape is materially closer to backend lowering than the newer SDSL-V AST.

## Semantic model

Current semantic work is intentionally modest:

- duplicate declarations and fields
- duplicate enum variants and generic parameters
- duplicate shader/interface methods
- unknown types and invalid array lengths
- duplicate locals
- light validation for flows/interfaces/compile declarations

What is missing from the current semantic model:

- resolved typed expressions
- resource/binding model
- stage interface validation beyond current conventions
- address space or storage-class modeling
- target capability analysis
- backend-independent execution semantics

That gap is one major reason a future GPU MIR is being considered.

## Lowering

There are effectively two lowering stories:

1. current active lane:
   `SdslvModule -> HlslEmitter -> HLSL text`

2. older top-level lane:
   `SdslDocument/SdslShader -> ShaderLowerer -> HLSL text + stage IO layout + stream usage`

The active lane does not expose a named lowering IR. The backend-neutral-to-backend-specific transition mostly happens inside `HlslEmitter`.

The older `ShaderLowerer` already performs backend-shaped work:

- merges inherited streams and methods
- infers stage IO structs
- classifies semantics
- rewrites base calls
- substitutes generic specializations
- computes stream usage across stages

That is not a real MIR, but it is strong evidence that MIR pressure already exists.

## Backend emission

`Language/Emission/Hlsl/HlslEmitter.cs` is the current backend emitter.

It is explicitly HLSL-shaped:

- emits `struct` declarations
- emits HLSL semantics like `POSITION`, `COLOR0`, `SV_Position`, `SV_Target0`
- maps a small set of SDSL-V scalar/vector/matrix names directly to HLSL type spellings
- emits control flow as HLSL syntax
- rejects unsupported expression and declaration shapes with HLSL-specific diagnostics

This is backend emission, not a backend-neutral middle layer.

## Artifact boundaries

Current artifact boundaries are explicit:

- `SdslvShaderArtifact` captures source hash, HLSL text, inferred stage list, and diagnostics
- `SdslvSpirvShaderArtifact` captures source hash, HLSL, nested SPIR-V artifact, and diagnostics
- `SpirvShaderArtifact` captures stage artifacts, DXC args, hashes, and diagnostics
- `CompiledShaderProgramExporter` maps compiler artifacts into `Aurelian.Rendering.Contracts.Shaders`
- `ShaderArtifactFileWriter` writes runtime-facing file sets and `shader.toml`

These are useful seams and should stay explicit even if a future MIR is introduced.

## Diagnostics

Diagnostics are already structured by phase:

- lexing
- parsing
- validation
- emission

Tool and artifact layers add their own diagnostic vocabularies:

- DXC tool diagnostics
- SPIR-V artifact diagnostics
- SDSL-V-to-SPIR-V wrapper diagnostics
- compiled-program export diagnostics
- file-writing diagnostics

This is healthy for a future MIR path because diagnostics already behave like stage-specific contracts.

## DXC and SPIR-V tool boundary

The DXC/SPIR-V seam is already crisp:

- `DxcExecutableResolver` and `DxcDiscovery` find DXC
- `DxcCommandLineBuilder` and `DxcSpirvCompiler` encode subprocess arguments
- SPIR-V compilation currently assumes `-spirv` and `-fspv-target-env=vulkan1.3`
- stage sources are passed as `HlslShaderStageSource`
- stage outputs are stored as `SpirvShaderStageArtifact`

Important current assumption:

- the SPIR-V route is HLSL-through-DXC, not direct MIR-to-SPIR-V
- `SdslvStageExtraction` hard-codes `VSMain`/`PSMain` and `vs_6_0`/`ps_6_0` expectations for the current smoke path

## Backend-neutral concepts

Backend-neutral or mostly backend-neutral concepts already present:

- source text, tokens, and spans
- module/use structure
- records, enums, type aliases
- shaders, functions, parameters, locals
- statements and expressions
- validation diagnostics
- stage declarations as source-language intent
- source hashes and deterministic artifact wrapping

GPU-domain but not yet inherently HLSL-specific concepts already present:

- shader entry points
- stage identity
- stream declarations
- field and parameter typing
- function bodies and control flow
- source-to-artifact provenance

## HLSL/DXC-specific concepts

Clearly HLSL/DXC-specific parts:

- `HlslEmitter`
- HLSL semantic strings like `POSITION`, `SV_Position`, `SV_Target0`
- HLSL stage entry-point naming assumptions `VSMain` and `PSMain`
- HLSL shader profiles `vs_6_0`, `ps_6_0`, `cs_6_0`
- DXC subprocess discovery and invocation
- DXC command arguments such as `-spirv`, `-fspv-target-env=vulkan1.3`, `-HV 2021`
- HLSL stage source records

SPIR-V-artifact-specific but not purely frontend-neutral:

- `SpirvShaderArtifact`
- per-stage SPIR-V bytes and SHA-256 hashes
- persisted `.spv` and `.spv.hex` file policy

## Hidden MIR candidates

Current code already contains MIR-shaped pressure in at least these areas:

| Current concept | Where present now | Why it is MIR-shaped |
| --- | --- | --- |
| module/program unit | `SdslvModule`, `SdslvShaderArtifact`, `SpirvShaderArtifact` | future GPU MIR likely needs a module root |
| entry points | `SdslvShaderArtifactStage`, `HlslShaderStageSource`, `SpirvShaderStageArtifact`, `SdslvStageExtraction` | backend-facing execution roots already exist |
| shader stage kind | `SdslvShaderStageKind`, `HlslShaderStageKind` | stage/capability metadata belongs in a future MIR |
| functions and parameters | `SdslvFunctionDecl`, `SdslvStageMethod` | backend lowering already reasons about callable units |
| typed values | `SdslvTypeRef`, `MapType`, return/parameter typing | types are present even though no typed MIR values exist |
| local variables and assignments | `SdslvLetStatement`, `SdslvAssignStatement`, emitter lowering | backend already lowers these into imperative code |
| control flow | `if`, `for`, `return`, `switch`, `match` in AST | some future backend-neutral CFG or block model is likely needed |
| stage IO | `SdslvStreamDecl`, older `StageIoLayout`, `StreamBinding` | clearly backend-lowering-shaped, not just source syntax |
| semantics/built-ins | HLSL semantics, stage inference, stream semantic classification | future MIR should represent built-ins without spelling them as HLSL |
| resource/binding-like metadata | material fields and stream layout pressure | not explicit yet, but shaping pressure is visible |
| target profiles/capabilities | `vs_6_0`, `ps_6_0`, `vulkan1.3`, DXC args | currently backend metadata with no neutral home |
| provenance and source spans | `SdslvSpan`, diagnostics, source hashes | future MIR should preserve source attachment |

Especially important hidden MIR evidence from the older lowerer:

- `StageIoLayout`
- `StreamUsageAnalysisResult`
- semantic classification and pass-through decisions
- base-call rewrite and inherited method merge

Those are the kinds of backend-lowering facts that a future `GpuMir` would likely own more cleanly than direct emitters.

## Aurelian-owned renderer-facing concepts

These concepts should remain Aurelian-owned even if some compiler mechanics move later:

- `Aurelian.Rendering.Contracts.Shaders.CompiledShaderProgram`
- compiled shader stage contract mapping
- `shader.toml` runtime artifact conventions
- asset/runtime-facing file layout
- renderer-facing stage semantics required by Aurelian contracts

M13e therefore treats `CompiledShaderProgramExporter` and file artifact policy as bridge seams, not as proof that all shader concepts belong in Copeland.

## Copeland migration candidates

Likely future Copeland candidates if a later milestone earns migration:

- `Language/Lexing`
- `Language/Tokens`
- `Language/Parsing`
- `Language/Ast`
- `Language/Validation`
- possibly a future backend-neutral GPU MIR once it exists
- HLSL backend emission/tooling once split cleanly from Aurelian renderer policy

Not good migration candidates yet:

- renderer-facing compiled shader export
- Aurelian artifact file conventions tied to runtime consumption
- anything that would drag Machina, Aurelian runtime, or Vulkan concerns into Copeland

## Risks

Main migration and architecture risks found by the audit:

- direct AST-to-HLSL emission means backend-neutral semantics are not isolated yet
- current stage extraction is hard-coded for the smoke-triangle style entry points
- semantic validation is still shallow relative to future multi-backend needs
- older and newer lane paths coexist, so careless refactoring could blur active versus legacy ownership
- renderer-facing artifact policy is currently interleaved with compiler artifacts and must not be moved casually

## What changed

M13e adds audit and target-analysis documentation plus deterministic manifest reporting.

## What did not change

M13e does not:

- move `Aurelian.Shaders`
- create Copeland shader/frontend/backend implementation packages
- implement `GpuMir`
- implement Slang
- implement PTX
- split shader and kernel MIR
- change compiler semantics
- change artifact semantics
- wire Machina/Aurelian/Vulkan

M13g later audits `samples/Aurelian/Aurelian.VisibleTriangle` as the future proof target for this lane, and M14a later implements a minimal `SDSL-V -> VD-MIR -> HLSL -> DXC -> SPIR-V` proof path. Even after M14a, the direct/default behavior described here remains preserved, SDSL-V is not migrated into Copeland, and no Slang/PTX or shader/kernel split work is added.

## Deferred work

- define Copeland GPU MIR target architecture in doctrine before implementation
- carry that doctrine forward under the named `VD-MIR` architecture in M13f
- decide whether older top-level `Aurelian.Shaders` legacy path should later be retired or folded into the active lane
- implement `GpuMir` only after M13e/M13f doctrine converges
- evaluate Slang and PTX only after the MIR target model is documented and pressure is concrete
