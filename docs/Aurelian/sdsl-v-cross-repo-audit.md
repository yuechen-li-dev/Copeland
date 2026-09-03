# AURELIAN-SDSLV-AUDIT-M0 cross-repository semantic port audit

## Decision

**Outcome B — SDSL-V is one semantic language; Copeland parser reuse is viable, with one small metadata syntax/AST extension required.**

Audited repositories:

- Oct `584bd176fd50664edadcb2bc3ae78431ac0f1e51`
- Copeland/Aurelian `3c4df459aa8b9dde775bb5a9d802445b11ab6d17`

The authority audited in Oct is `docs/SDSL_V_LANGUAGE_SPEC.md` plus
`examples/SDSL-V/conformance/manifest.json`. GoOct is the reference
implementation, not the language definition. The existing independent parser
under `Aurelian.Shaders.Language` is useful historical/backend evidence but is
not conformant authority and must not be extended into a second SDSL-V language.

> SDSL-V is defined by semantic behavior and target contracts, not by one
> frontend parser. Oct and Copeland may expose different syntactic frontends
> only if they preserve one semantic language.

## Cross-repository mapping

This table existed before the bounded parser-reuse proof was added.

| Oct SDSL-V concept | Oct implementation | Copeland equivalent | Reuse directly? | Port/adapt? |
| --- | --- | --- | --- | --- |
| Module/import | `ast.Module`, `namespace`, `use`; conformance manifest | `SyntaxTree`, `.ts` module imports, `CopelandProjectCompiler` graph | Yes, infrastructure | Map module identity; do not require Oct `namespace` spelling |
| Functions/control flow | `FunctionDecl`; typed validation; VD-MIR blocks | function, block, `if`, `for`, `while`, break/continue, return AST/binder/MIR | Yes | GPU legality and bounded-loop checks in profile |
| Records/value update | `RecordDecl`, immutable `with` | nominal records, structural objects, ordinary expressions | Mostly | Define GPU value/layout subset; keep host record semantics distinct |
| Payload enums/match | `EnumDecl`, exhaustive `match`, tagged VD-MIR values | payload enums and `MatchExpressionSyntax` | Syntax yes | Port explicit tagged layout before runtime support |
| Concepts | static fact requirements, no interfaces | erased structural Copeland `interface` + `RequirementSet` | Yes conceptually | GPU profile treats interface only as compile-time capability evidence |
| Generics | template/config/compile materialization | closed generic specialization in binder | Yes | Reject open/runtime generic realization |
| Templates/comptime | bounded immutable planning | `template`, `static`, bounded template evaluator | Yes conceptually | Add SDSL-specific facts/outputs; no second evaluator |
| Reflection | bounded compiler-owned metadata, no runtime reflection | compile-time `reflect nameOf/fieldsOf/enumCasesOf/callsOf` | Yes | Extend only with typed SDSL layout/stage/binding queries when demanded |
| Scalar/vector/matrix | closed SDSL primitive families | identifier/generic type syntax; no GPU primitives yet | Parser yes | Add compiler-known nominal GPU types and operations |
| Arrays/tensors | fixed arrays, runtime resource arrays, ndarray/tensor/tile views | array syntax, generic types, indexing | Parser mostly | Port static shape/layout rules and specialized tensor forms as semantic constructs |
| Shader stages | compute/vertex/pixel in semantic model | ordinary functions; no declaration metadata | No | Add closed annotation metadata and GPU entry binding |
| Streams | compiler boundary with stage-value/resource/builtin role | `stream` exists but means Copeland layout data | No | Do not reuse the unrelated stream node; express typed boundary metadata in GPU profile |
| Resources | storage buffer, uniform, texture2d, sampler, acceleration structure | generic type/call/record shapes only | Parser yes | Add compiler-known resource types, access and bindings |
| Attributes | closed `binding`, `builtin`, `location`, `target`, `interpolation`, `numthreads`, `space` | no general declaration annotation node | No | One small closed/general annotation syntax and AST extension |
| Semantic spaces | nominal vector alias identity, physically erased | nominal aliases exist; no annotation | Mostly | Metadata extension plus GPU assignability law |
| Entry/linkage law | validator + `GraphicsEntryPoint`/`GraphicsProgram` | no GPU profile | No | New GPU semantic pass over ordinary bound/source AST |
| Intrinsics | typed `vdmir.Intrinsic` enum | calls and compiler-known operations | Infrastructure yes | Add typed intrinsic registry; never magic-string emission |
| Storage/address space | resource kinds/access, workgroup declarations, locals | host variable/type semantics only | Parser partly | GPU binder-owned storage class and mutation legality |
| Layout | material offsets; row-major fixed shapes; explicit interface facts | records have semantic fields but no GPU ABI | No | Port canonical SDSL layout calculator and manifest facts |
| Semantic IR | `internal/sdslv/vdmir` | Copeland MIR is host-target oriented; Aurelian VD-MIR M0 is tiny | No source reuse | Port structure/spec into frontend-neutral SDSL IR; do not lower through fake Oct AST |
| HLSL | deterministic emitter from VD-MIR | Aurelian has HLSL/DXC artifact plumbing | Backend seam only | Replace heuristic stage semantics with semantic-IR consumption |
| DXC/SPIR-V | one toolchain wrapper, validation and structural facts | `Aurelian.Shaders` has subprocess DXC/artifact/export/file plumbing | Yes locally | Aurelian owns the sole C# process wrapper; align flags/contracts with Oct |
| Vulkan consumption | Oct Prometheus/Kaiju integrations consume artifacts | Aurelian.Graphics consumes neutral compiled shader stages | Yes locally | Keep compiler metadata upstream of renderer/pipeline creation |
| Diagnostics/provenance | stable `SDSL-Vnnnn`, exact/related spans | Copeland diagnostics and source paths/spans | Yes infrastructure | Map canonical category/code where practical and preserve spans into backend diagnostics |
| Conformance | `sdslv.conformance.v1`, six tiers | no current consumer | No | Make this corpus the cross-repo oracle |

## Language semantics

The following are SDSL-V language law, independent of Oct syntax and Go types.

- It is statically typed with shared core semantics and exactly compute, vertex,
  and pixel execution profiles. Helpers are not entries.
- Runtime scalar law is the canonical `bool`, `i32`, `u32`, `f16`, and `f32`
  family plus closed vector/matrix forms, fixed arrays, value records, payload
  enums, resource handles, ndarray/tensor forms, and compiler-known builtins.
- `let` is initialized immutable state; `var` is initialized mutable state.
  `with` produces a new aggregate. Mutation is explicit and type checked.
- Concepts/configs/templates/compile materialization are closed static
  abstraction. Generic entry points must be monomorphized before emission.
- Comptime is deterministic immutable planning for sizes, layouts, variants,
  unrolling, bindings, specialization and artifact structure. It is not arbitrary
  I/O, mutable global state, runtime reflection or allocation.
- A stream is a compiler-owned typed boundary with exactly one of stage-value,
  resource or builtin role. Mixed or ambiguous roles are errors.
- Canonical metadata is closed: binding, builtin, location, target,
  interpolation, numthreads and semantic space. Explicit numbers and source
  order survive lowering.
- Semantic-space aliases add nominal identity to compatible float vectors and
  erase physically. No implicit cross-space or spaced/unspaced conversion exists.
- Compute includes explicit storage-buffer access, compute builtins, workgroup
  memory/barriers, fixed-shape/tensor lowering and bounded capability islands.
- Vertex/pixel linkage agrees on location, physical/semantic type and
  interpolation. Vertex output has exactly one clip-position `float4`; pixel
  outputs have unique explicit targets.
- Resource law includes storage buffers, uniforms, `texture2d<T>`, samplers and
  the currently implemented acceleration-structure capability. Binding set is
  zero in the bounded canonical graphics profile and binding index is explicit.
- `Sample(texture, sampler, float2)` is typed and backend neutral. Raw HLSL
  methods and `SV_*` names are not source semantics.
- Material is sugar for one immutable uniform record with deterministic
  HLSL-compatible 16-byte register packing. Every field offset, size/alignment,
  total size and binding is semantic metadata.
- Shared control includes `if`, bounded loops, return, exhaustive match and the
  current bounded flow/board/state model. Flow push cycles are invalid because
  they create unbounded stack depth.
- Backend-neutral meaning ends at semantic SDSL IR. HLSL spelling and SPIR-V
  bytes are not universal identity; normalized interface/resource/layout facts
  and runtime behavior are.
- Shader fallibility, interfaces/implements/override, uninitialized bindings,
  implicit coordinate conversion, runtime allocation/reflection, arbitrary
  attributes and unsupported stages/resources are rejected rather than silently
  accepted.

### Compatibility law

If the Oct and Copeland frontends accept the same semantic program, they must
agree on resolved types and nominal spaces; static closure and specialization;
entry identity and stage legality; resources, access and bindings; storage and
mutation; interface and material layout; intrinsic meaning; control flow; and
the normalized HLSL/SPIR-V target meaning. Diagnostic spelling may differ, but
the rejection class, primary source site and related conflict site must agree
where the conformance manifest specifies them.

## Oct implementation details

Oct's lexer/token spelling, Rust-like `fn`, `shader` block grammar, Go AST data
types, validator organization, VD-MIR Go structs, dump format, HLSL formatting,
CLI flags, filesystem layout and process runner are implementation details. The
current pipeline is:

```text
source -> lex -> Oct SDSL AST -> validate -> lower -> VD-MIR
       -> deterministic HLSL -> DXC -> SPIR-V -> spirv-val/structural facts
```

The semantic IR is backend-neutral in intent and already resolves stream roles,
types, resources, bindings, entries, layouts, intrinsics, flow transitions and
provenance. It is nevertheless coupled to Go source spans and accumulated Oct
milestones, and is internal to the Oct module. Assessment: **PORT STRUCTURE**,
not source reuse and not wholesale reimplementation from parser code.

### Syntax classification

| Surface | Classification | Reason |
| --- | --- | --- |
| `fn`, block punctuation, `namespace`/`use` spelling | OCT-SYNTAX ACCIDENT | Copeland syntax can express the same declarations/import graph |
| `shader`, `stage`, `resources`, `material` block spelling | CONVENIENCE ONLY | The roles are essential; this grouping syntax is not |
| explicit stage/resource/builtin/location/target/interpolation/thread metadata | ESSENTIAL SEMANTIC FEATURE | Required before HLSL and pipeline reflection |
| concept/config/template/compile spelling | OCT-SYNTAX ACCIDENT | closed constraints/materialization are essential |
| fixed shapes, access modes, bindings, semantic spaces | ESSENTIAL SEMANTIC FEATURE | Affect typing, ABI or target behavior |
| `[[vk::binding]]`, `SV_*`, DXC profile/flags | BACKEND CONSTRAINT | Must be generated from semantic metadata |
| `match`, `with`, ordinary operators/control flow | ESSENTIAL SEMANTIC FEATURE | Surface spelling may remain Copeland-shaped |
| `comptime`, guarded memory and bounded flow surface | ESSENTIAL SEMANTIC FEATURE | May map onto Copeland static/profile constructs rather than copied grammar |

## Copeland frontend decision

### Strategies

| Strategy | Decision |
| --- | --- |
| A: dedicated `.v` parser | Reject. It duplicates the current Aurelian mistake and is not required by ordinary expression/declaration shapes. |
| B: `.v.ts` GPU profile | Select as the recommended authoring/tooling convention. Existing `SourceFileKind.FromSourcePath` already treats `.v.ts` as a TypeScript module. |
| C: ordinary `.ts` plus explicit target | Semantically sufficient and required as the underlying build model. The target/profile, not the suffix, is authority. |

`.v.ts` decision: **USEFUL BUT NOT REQUIRED**. It improves discovery, editor
association, transitive closure diagnostics and human recognition. File-level
GPU closure does not have to be known before parsing and can be selected before
binding by an explicit compiler target. No grammar branch should depend on the
suffix. A `.v.ts` file and an ordinary `.ts` module compiled with the same SDSL
profile have identical semantics.

Parser reuse result: functions, records, interfaces, payload enums, generic
types/functions, templates, static constructs, reflect syntax, arrays,
index/member/call/operator expressions, match, imports and ordinary control
flow already have usable nodes. Copeland has no namespace declaration; module
path identity supersedes it. Copeland's existing `stream` syntax is layout-data
language syntax and must not be repurposed as a shader stream.

One syntax/AST gap crosses the dedicated-syntax threshold: declaration/field/
type metadata. A small closed annotation facility is warranted because encoding
stage identity, binding, builtin, location, target, interpolation, numthreads or
semantic space as naming convention or detached calls would be misleading and
would lose first-class spans. The parser should parse general annotation shape;
the GPU profile must admit only the canonical SDSL set. This is Outcome B, not
authorization for a shader grammar.

## GPU profile law

The detailed profile is in `docs/Copeland/sdsl-v-gpu-profile.md`. In summary:

- profile selection precedes binding and closes transitively over reachable
  imports/symbols;
- safe shared definitions are certified symbol-by-symbol rather than all shared
  `.ts` being banned;
- accepted runtime types have a closed GPU representation; GC references,
  heap allocation, exceptions/fallibility, tasks/threads, runtime reflection,
  boxing, dynamic, virtual dispatch and heap closures are illegal;
- interfaces are erased compile-time capabilities with zero dispatch;
- generics/templates are closed and specialized; reflect is compile-time only;
- captures may be explicit immutable static specialization inputs only, never
  runtime closure environments;
- recursion is **deferred/rejected** until the canonical spec adds an explicit
  law and conformance cases; backend acceptance alone is not language law;
- dynamic indexing is allowed only where the canonical type/resource contract
  permits it; descriptor arrays and texture arrays remain unsupported;
- payload enums are **SUPPORTED only after explicit tagged layout is ported**;
  until then the implementation gate is DEFER. `Option<T>` may map to a payload
  enum only after that layout exists. `Result<T,E>` is **UNSUPPORTED at shader
  runtime** because canonical SDSL-V deliberately removed fallibility.

## Records, layout and host boundary

Copeland records may supply field/name/type input to an SDSL value struct, but a
host record is not automatically a GPU ABI record. GPU admissibility, value
semantics, nominal identity and the canonical layout algorithm must be certified
by the profile. Material layout uses the Oct canonical 16-byte register law;
fixed ndarray/tensor storage is row-major where specified. No audit evidence
supports claiming universal std140/std430 layout or exposing raw CLR layout.

The desired boundary is:

```text
one certified source definition
-> SDSL semantic layout/binding metadata
-> generated host upload/binding type and Aurelian pipeline input
```

Generation must consume semantic metadata, never inspect emitted HLSL or require
a second handwritten C# binding description.

### Remaining semantic edge decisions

- Storage/address spaces are target meanings, not imported Rust references:
  function/local private values, workgroup memory, stage input/output, uniform
  and storage resources are distinct typed categories. Source exposes handles
  and validated l-values, not ownership, lifetimes or general pointers.
- Compute's current toolchain accepts at most one record entry parameter and
  lowers it as a Vulkan push constant. This is implemented behavior that the
  canonical language specification does not yet elevate alongside the graphics
  resource law; classify it **NEEDS SPEC DECISION** before porting. Do not
  independently design a Copeland push-constant surface.
- No canonical specialization-constant surface was found. Config/template
  values are compile-time specialization. Runtime/Vulkan specialization
  constants are **DEFER**.
- Matrix row/column-major ABI beyond the explicit material and fixed-shape laws
  is not fully specified. Preserve compiler-recorded convention and add layout
  conformance before sharing host structures.
- The specification names `f16`, while generic VD-MIR scalar kinds currently do
  not include a general `TypeF16`; current half support appears in packed and
  cooperative-matrix capability contracts. Treat general `f16` realization as
  **NEEDS REFERENCE-IMPLEMENTATION CLARIFICATION**, not permission to diverge.

## Frontend-neutral SDSL semantic IR contract

The next implementation must define a deterministic, implementation-neutral
schema containing:

- schema/semantic feature level and source provenance;
- module identity, imports and capability requirements;
- canonical nominal/physical types, static dimensions and layouts;
- records/enums and resolved field/case order;
- functions with typed parameters/results, locals, structured control and typed
  expressions/intrinsics;
- concrete entry points with stage, workgroup size, inputs, outputs and builtins;
- resources with kind, access, set/binding and visibility;
- material/host layouts with offsets, sizes, alignments and matrix convention;
- specialization/materialization inputs and concrete emitted identities;
- source/related spans sufficient to map semantic and backend diagnostics.

Use deterministic JSON first because Oct's canonical conformance and bundle
formats are JSON and both repositories already support it. TSON may later be a
human-facing projection, but must not become a second schema. Binary transport
is unjustified in M0.

## Backend and ownership

Recommended topology:

```text
Oct frontend       -> canonical SDSL semantic contract -> Oct HLSL/toolchain
Copeland GPU bind  -> canonical SDSL semantic contract -> Aurelian.Shaders backend
                                                        -> Aurelian.Graphics consume
```

- Oct owns its frontend/reference implementation and canonical spec/corpus.
- Copeland owns `.ts` parsing, GPU profile binding, static closure and semantic
  IR projection.
- `Aurelian.Shaders` owns one C# SDSL IR-to-HLSL backend, one DXC subprocess
  wrapper, SPIR-V validation/reflection and artifact/cache metadata.
- `Aurelian.Graphics` owns Vulkan module/pipeline realization and caches native
  handles, never compiler semantics or DXC invocation.
- Application/Aurelian asset build owns persistent artifact placement. A shader
  cache key includes semantic IR hash, specialization, target/feature level,
  backend/DXC version and flags.

The current Aurelian path already has useful DXC, SPIR-V artifact, compiled-stage
export and renderer consumption seams. Its `SdslvParser`, `SdslvValidator`,
heuristic `SdslvStageExtraction`, legacy HLSL emitter and tiny smoke-only VD-MIR
must not become the semantic source of truth. Preserve/retarget downstream seams
while replacing heuristic frontend input with Copeland-produced semantic IR.

HLSL parity requires equivalent typed declarations, bindings, layouts, entries
and intrinsics; byte-identical HLSL is not required. Align Aurelian DXC ownership
with Oct's current stage profiles (`cs_6_0` with capability upgrades, `vs_6_0`,
`ps_6_0`), `-spirv`, Vulkan target contract, optimization flags, validation and
recorded compiler identity. SPIR-V validation/reflection belongs to the shader
backend; Vulkan object creation belongs to graphics.

### Sharing and versioning

Share the contract through the canonical specification, conformance corpus and
deterministic JSON fixtures. Port the schema types idiomatically into Go and C#;
do not couple the repositories through a Git submodule, a shared runtime binary
or copied-and-edited language tests. A checked-in schema generator may become
worthwhile only after the compute slice proves the contract stable.

No independent SDSL-V semantic-version system is justified yet. Keep the
existing `sdslv.conformance.v1` schema identity, add an explicit bounded feature
level to every semantic manifest, and reject unknown feature levels. Change the
schema identifier only for incompatible representation changes; extend the
feature level when the schema can still represent the new language feature.
Repository commits and toolchain identities remain provenance, not language
versions.

## Conformance and diagnostics

Use Oct's `examples/SDSL-V/conformance/manifest.json` as the one canonical
corpus. Do not copy and edit independent expected behavior. Each frontend may
have idiomatic source paired by a shared semantic case ID. Compare in tiers:

1. acceptance/rejection;
2. normalized semantic manifest;
3. diagnostic class/code and exact primary/related spans;
4. structural SPIR-V facts;
5. runtime behavior;
6. exact bytes only for named golden artifacts.

The initial port slice must include compute no-regression, minimal vertex,
minimal pixel and canonical forward-textured cases; duplicate binding, invalid
stage builtin, varying mismatch, immutable mutation and layout cases. Copeland
diagnostics may retain `COPE-*` framing, but conformance output must carry the
canonical SDSL diagnostic category/code when specified. Every IR node that can
fail in validation/emission retains source provenance.

## Aurelian renderer prerequisites

Before a native SDSL-driven renderer path is authoritative, it needs semantic
shader compilation; validated SPIR-V; stage/entry/capability metadata; descriptor
set/binding/access/visibility facts; vertex/varying/target and material layouts;
specialization data; source/backend diagnostic mapping; and deterministic asset
identity. Aurelian already has Vulkan execution and neutral compiled-stage
plumbing, but current shader stage extraction and semantics are heuristic.

The compositor relation remains:

```text
Aurelian world layer -> SDSL-V shaders -> Vulkan
Machina UI layer     -> SDSL-V shaders -> Vulkan
plant outputs        -> compositor     -> presentation target
```

SDSL-V remains compute-first as well as graphics-capable. Host RyuJIT versus
NativeAOT changes compiler hosting only and never emitted shader meaning. No
Rust ownership, lifetimes, borrow checker or trait-object semantics are imported.

## Performance observations

This audit found no scale evidence justifying parser or IR architecture based on
micro-performance. Oct already lowers production compute shaders through typed
VD-MIR and deterministic HLSL. Copeland's ordinary parser is shared, mature and
already required for all `.ts` tooling; reusing it removes duplicate lex/parse
work and semantic maintenance. Performance qualification belongs after semantic
parity and must measure parse, GPU bind, IR serialization, HLSL and DXC separately
without weakening artifact hashes or target behavior.

## Validation result

- `go test ./internal/sdslv/...`: passed all SDSL-V packages.
- `go run ./cmd/oct sdslv compile-graphics ...CanonicalGraphicsProgram...`:
  passed with DXC and `spirv-val`; replay identity
  `270415a98a26d4e7a449054b9174f0d20377b8151401805d39caea263ab4eac3`,
  canonical vertex/pixel SPIR-V hashes reproduced.
- `go run ./tools/sdslv_workspace_check`: failed on an Oct repository ownership
  mismatch: `registry does not own manifest source
  internal/prometheus/shaders/sdslv/production/sgemm/sgemm_scalar_baseline_plus.sdslv`.
  The audit changed no Oct files and did not conceal this unrelated checker
  failure.
- `dotnet test Copeland.TS.slnx --no-restore`: passed 1,581 tests.
- `dotnet test Aurelian.slnx --no-restore`: passed 616 tests.
- `dotnet test JointTaskForce.slnx --no-restore`: passed 3,192 tests.
- JSON parsing, `git diff --check`, and audit-manifest hash checks are part of
  the final artifact verification.

## Final report index

This index answers the required handoff fields directly; the preceding sections
contain the evidence and rationale.

1. **Outcome:** B.
2. **Oct revision:** `584bd176fd50664edadcb2bc3ae78431ac0f1e51`.
3. **Copeland/Aurelian revision:** `3c4df459aa8b9dde775bb5a9d802445b11ab6d17`.
4. **Oct architecture:** lex, parse, validate, lower to VD-MIR, emit HLSL,
   compile with DXC, validate and reflect SPIR-V.
5. **Language inventory:** the closed static, stage, type, resource, layout,
   control, intrinsic and specialization laws under Language semantics.
6. **Oct-specific details:** Go AST/VD-MIR structures, parser spelling, CLI,
   HLSL formatting, filesystem layout and process runner.
7. **Copeland coverage:** ordinary declarations, types, generics, templates,
   reflect, payload enums, expressions, imports and control flow are reusable.
8. **Missing syntax/AST:** first-class declaration, field and type annotations
   carrying closed SDSL metadata with source spans.
9. **`.v.ts`:** useful convention, not semantic requirement.
10. **GPU profile:** an explicit compilation target selected before binding;
    transitive reachability determines the certified GPU closure.
11. **Parser reuse:** viable and proven through unchanged `SyntaxTree.Parse`.
12. **Interfaces/concepts:** erased compile-time capability evidence only.
13. **Generics:** closed monomorphization before backend emission.
14. **Templates:** reuse Copeland's bounded static evaluator; no second
    template language.
15. **Reflect:** compile-time and compiler-owned only; no shader runtime
    reflection.
16. **Records/layout:** records may provide nominal value shapes, but GPU ABI
    layout requires separate canonical certification.
17. **Payload enums:** defer runtime use until canonical tagged layout is
    represented and conformance-tested.
18. **Option/Result:** Option follows payload-enum layout; runtime Result is
    unsupported because shader fallibility is absent.
19. **Managed semantics:** GC references, allocation, exceptions, tasks,
    boxing, dynamic and virtual dispatch are forbidden.
20. **Captures:** immutable static specialization inputs only; no runtime heap
    closure.
21. **Intrinsics:** typed canonical intrinsic identities, never backend-name
    strings in source semantics.
22. **Vectors/matrices:** compiler-known nominal GPU families; preserve spaces,
    physical shape and recorded matrix convention.
23. **Resources:** canonical kinds, access and explicit set/binding; no new
    Vulkan model.
24. **Stages:** exactly compute, vertex and pixel; helpers have no stage.
25. **Entries:** concrete, closed and specialized, with complete stage metadata
    and validated interface/resource law.
26. **Storage/address spaces:** typed local/private, workgroup, stage boundary,
    uniform and storage categories without Rust ownership semantics.
27. **Layout:** Oct's material/fixed-shape rules are authority; unspecified
    matrix and push-constant details require canonical decisions.
28. **Control flow:** retain Copeland-shaped syntax while matching SDSL typed,
    bounded and exhaustive behavior.
29. **Recursion:** rejected/deferred until specified and covered canonically.
30. **Dynamic indexing:** only where the canonical type/resource capability
    permits it; descriptor/texture arrays remain unsupported.
31. **Compile-time evaluation:** deterministic, immutable and side-effect-free
    planning, never arbitrary host execution.
32. **Imports:** certify the transitive reachable symbol graph, fail closed on
    unsafe or unresolved dependencies.
33. **Shared definitions:** allowed symbol-by-symbol when their closed semantic
    representation is GPU-safe.
34. **Host/GPU boundary:** generate upload/binding types from certified semantic
    layout metadata, not HLSL or CLR layout.
35. **Oct IR coupling:** backend-neutral in intent but Go/internal in form;
    port its structure, not its source code.
36. **Neutral IR:** deterministic versioned JSON with resolved types, entries,
    resources, layouts, control, intrinsics and provenance.
37. **Backend pipeline:** Copeland bind/IR, Aurelian.Shaders HLSL/DXC/SPIR-V,
    Aurelian.Graphics native module/pipeline consumption.
38. **HLSL parity:** semantic and structural equivalence is required;
    byte-identical text is not.
39. **DXC ownership:** one Aurelian.Shaders subprocess wrapper in Copeland.
40. **SPIR-V ownership:** shader backend validates/reflects; graphics creates
    and caches native Vulkan objects.
41. **Diagnostics:** canonical rejection category and source sites must agree;
    frontend framing may differ.
42. **Provenance:** retain primary and related spans through IR, HLSL and backend
    diagnostics.
43. **Corpus:** Oct's `sdslv.conformance.v1` manifest is the cross-repo oracle.
44. **Prototype:** representative `.v.ts` compute-shaped syntax parsed through
    the real Copeland path; no fake binder claim.
45. **Annotations:** one readable annotation AST shape; GPU binding admits only
    the canonical closed metadata set.
46. **Extension/tooling:** `.v.ts` already receives TypeScript module/import
    behavior and may improve discovery, but must not select grammar.
47. **One-language doctrine:** specification, corpus and normalized semantics
    are shared; frontends are replaceable projections.
48. **Versioning:** keep `sdslv.conformance.v1`, add a bounded semantic feature
    level and reject unknown levels.
49. **Ownership:** Oct owns authority/reference; Copeland owns GPU binding/IR;
    Aurelian.Shaders owns backend; Aurelian.Graphics owns Vulkan realization.
50. **Sharing:** canonical spec, corpus and deterministic fixtures, with
    idiomatic Go/C# schema types and no cross-repo runtime coupling.
51. **Serialization:** deterministic JSON for audit/build interchange; not a
    required runtime compilation hop.
52. **Renderer prerequisites:** validated SPIR-V plus complete stage, binding,
    interface, layout, capability, identity and diagnostic metadata.
53. **Reflection/bindings:** semantic IR is the authority for generated host
    bindings and renderer inputs.
54. **Performance:** no evidence supports a new parser/IR architecture; measure
    pipeline phases only after semantic parity.
55. **Validation:** Oct SDSL tests, canonical graphics compilation and all three
    Copeland solutions pass; Oct's workspace ownership checker has the isolated
    pre-existing manifest/registry mismatch recorded above.
56. **Artifacts/docs:** four decision maps plus a hashed manifest, this audit,
    the GPU profile, architecture authority update and parser proof.
57. **Repository diff:** Oct is clean; Copeland has three tracked documentation
    updates and eight new audit/proof files. Final `git diff --check` is clean.
58. **Next milestone:** the bounded compute-only
    `AURELIAN-SDSLV-PORT-M1` vertical slice below.

## Minimal parser-reuse proof

`GpuProfileParserReuseAuditTests` parses a representative `.v.ts` compute shape
through the unchanged `SyntaxTree.Parse` path and proves that the suffix receives
module/import behavior plus existing function, record, interface, generic type,
indexing and control-flow nodes. It does not pretend that a GPU binder exists.
The deliberately unimplemented checks—host-only rejection and stage/resource
attachment—are isolated as the next milestone rather than false-green tests.

## Exact next milestone

**AURELIAN-SDSLV-PORT-M1 — canonical semantic-contract and Copeland GPU-binder
compute slice.**

Bound it to:

1. import/version Oct's `sdslv.conformance.v1` semantic schema and four compute
   acceptance/rejection cases without copying Oct implementation code;
2. add one readable annotation syntax/AST shape with exact spans;
3. add explicit `Gpu`/SDSL profile selection (suffix as convention only) and
   transitive safe-import certification;
4. bind scalar/vector types, one readonly and one readwrite storage buffer,
   dispatch-thread ID, `numthreads`, immutable/mutable locals, indexing,
   arithmetic and one compute entry;
5. serialize normalized SDSL IR JSON and compare it with the canonical compute
   semantic case;
6. feed that IR through one Aurelian-owned HLSL/DXC/SPIR-V path and `spirv-val`;
7. reject one host-only construct and one duplicate binding with canonical
   category/span evidence.

Do not add vertex/pixel, material, payload-enum runtime layout, templates,
reflection extensions, shader cache, renderer API or editor work in M1. This is
the smallest vertical slice that proves one language and one Copeland frontend.

## M1 closure update (2026-09-03)

`AURELIAN-SDSLV-PORT-M1` completed the bounded compute route described above.
The Oct authority revision remained `584bd176fd50664edadcb2bc3ae78431ac0f1e51`;
the pre-port audit found no implementation bug in the selected
`compute.no-regression` and `DuplicateResourceBinding` laws, so Oct required no
change. The canonical conformance manifest SHA-256 is
`a107f9d4291458f9d7c2a06e73578ec8e11a223a6acc9bed7c417cbe322b4406`.

Copeland now parses first-class annotations and binds a compute-only
`vdmir.semantic.v1` module. Aurelian consumes the module without invoking its
historical SDSL-V frontend. The checked-in five-file proof bundle records exact
resource/builtin facts, canonical diagnostics, HLSL, SPIR-V, validation,
structural disassembly evidence, tool provenance, timings, and repeat hashes.
The detailed audit and result are in
`docs/Aurelian/sdsl-v-port-m1-compute-report.md`.

## M2 graphics stream closure update (2026-09-03)

The Oct authority remained clean at
`584bd176fd50664edadcb2bc3ae78431ac0f1e51`. Its role, location, position,
target, and linkage laws were consistent for the untextured port. Copeland now
gives shader and layout streams separate syntax nodes, emits
`vdmir.semantic.v1` / `graphics.m2`, and Aurelian generates and validates both
graphics stages. Parity uses `graphics.minimal-vertex`,
`graphics.minimal-pixel`, and the linkage law of
`graphics.canonical-forward-textured`. See
`sdsl-v-port-m2-graphics-stream-report.md`.

## M3 forward-textured closure update (2026-09-03)

The final bounded production graphics slice is implemented without changing
ownership. Copeland owns semantic aliases, resource/builtin stream typing,
texture/sampler/Sample validation, material immutability and packing, linkage,
and renderer-facing `graphics.m3` metadata. Aurelian owns generated resource
HLSL, DXC, SPIR-V validation, and structural verification. Oct remained the
unchanged spec/conformance authority. Language expansion stops here; next is
native consumption of the qualified `CompiledGraphicsProgram` contract.
