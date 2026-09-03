# AURELIAN-SDSLV-PORT-M1 compute report

## Outcome

**Outcome A — the bounded Copeland compute subset is semantically shared and production-portable.**

```text
Copeland .ts/.v.ts -> ordinary parser -> annotation AST -> Gpu binder
-> vdmir.semantic.v1 -> Aurelian compute HLSL -> DXC cs_6_0
-> Vulkan 1.3 SPIR-V -> spirv-val
```

No graphics stage, renderer API, payload-enum runtime, template/reflection
extension, shader cache, editor feature, or push-constant design was added.

## Revisions and authority

- Oct before/after: `584bd176fd50664edadcb2bc3ae78431ac0f1e51` (clean; no correction required).
- Copeland base before/after: `fc55dd68c870833f30b9c94f63e05ad1792bbdd0` plus this working-tree implementation.
- Canonical corpus: `sdslv.conformance.v1`, SHA-256 `a107f9d4291458f9d7c2a06e73578ec8e11a223a6acc9bed7c417cbe322b4406`.
- Positive authority: `compute.no-regression`. Its combined case supplies the minimal entry, storage read/write, dispatch/numthreads, and indexing facets. Arithmetic has an additional focused Copeland test.
- Negative authority: `DuplicateResourceBinding` / `SDSL-V4112`.

## Pre-port Oct compute audit

| Feature | Spec law | Conformance law | Oct behavior | Classification | Action |
| --- | --- | --- | --- | --- | --- |
| compute entry | compute entry; helpers stage-less | `ComputeNoRegression_CS` | typed entry | CONSISTENT | port |
| numthreads | three positive compile-time integers | `(8,1,1)` | retained through HLSL | CONSISTENT | port |
| dispatch builtin | backend-neutral `uint3` ID | `dispatch_thread_id:uint3` | typed HLSL mapping | CONSISTENT | port |
| scalars/vector | `f32`, `u32`, `bool`, `uint3` | case uses `f32/u32/uint3` | typed/lowered | CONSISTENT | port M1 types only |
| storage/access | readonly/readwrite storage arrays | bindings 0/1 | typed resource/access | CONSISTENT | port as `StorageBuffer<f32>` |
| binding | set zero, explicit index | ordered binding facts | source order retained | CONSISTENT | port |
| duplicate binding | reject with conflict context | `SDSL-V4112`, two sites | exact diagnostic | CONSISTENT | port code/category/spans |
| locals/mutation | initialized immutable `let`, mutable `var` | immutable `SDSL-V3701` | typed mutation | CONSISTENT | map `const`/`var` |
| index/operators | typed resource/scalar operations | case covers indexing | typed VD-MIR | CONSISTENT | port `[]`, `+`, `<` |
| `if`/return | structured control | source/HLSL proof | structured lowering | CONSISTENT | port |
| buffer length | not needed by selected case | absent | no selected law | AMBIGUOUS | defer |
| entry record parameter | may imply push constants | absent | under-specified | SPEC_GAP | defer push constants |
| host-only constructs | allocation/reflection/fallibility absent | language excludes them | fail closed | CONSISTENT | emit Copeland `SDSL-V4200` class |
| recursion/closures | no M1 runtime law | absent | not selected | CONFORMANCE_GAP | reject/defer |
| HLSL/DXC/SPIR-V | compute DXC/Vulkan plus validation | canonical artifacts | production path passes | CONSISTENT | reuse law, not Go code |
| provenance | stable primary/related spans | duplicate-binding sites | retained | CONSISTENT | retain in VD-MIR |

No selected feature was `IMPLEMENTATION_BUG`; the Oct cleanup gate did not
trigger. General `f16`, push constants, and buffer length were not invented.

## Frontend and semantic law

Syntax is one annotation node: `@name` with optional ordinary expression
arguments. M1 uses `@compute`, `@numthreads(8, 1, 1)`,
`@builtin(dispatchThreadId)`, and `@binding(0)`. Resource access is the explicit
`readonly`/`readwrite` parameter modifier, not a naming convention. Annotation
names/arguments, parameters, declarations, diagnostics, and related sites retain
file and exact offset/length.

The explicit profile is `CopelandCompilerProfile.Gpu`; filenames do not select
semantics. Tests compare `.v.ts` and `.ts` VD-MIR after normalizing only the
provenance filename. Reachability is symbol-level across ordinary modules: a
reachable safe helper binds; unreachable allocation is ignored; calling it
produces `COPE-GPU-CLOSURE-0001` / `SDSL-V4200` at `new`.

M1 accepts `f32`, `u32`, `bool`, `uint3`, `StorageBuffer<f32>`, functions,
initialized `const`/`var`, `+`, `<`, `if`, return, `thread.x`, and indexing.
Writes require a mutable local or readwrite resource. It rejects immutable or
readonly mutation (`SDSL-V3701`), duplicate binding (`SDSL-V4112`), unknown
metadata/types/builtins, reachable host operations, loops, recursion, open
generics, and non-compute entry shapes. Push constants, buffer length,
templates, interfaces/concepts, payload enums, Option, Result, closures, and
runtime reflection remain deferred or rejected.

## VD-MIR and backend

`Copeland.TS.Gpu.VdMir` owns frontend-neutral `vdmir.semantic.v1`. Its public
model references no Copeland syntax node, Oct Go type, HLSL AST, or Vulkan
handle. It records feature level, conformance schema, sorted sources/types/
resources/functions, typed control and expressions, entry metadata, bindings,
builtin identity, and provenance. JSON is the deterministic proof/interchange
projection; production remains in memory.

`Aurelian.Shaders.Compute` consumes VD-MIR directly. Backend-only spellings
(`SV_DispatchThreadID`, `StructuredBuffer`, `RWStructuredBuffer`,
`[[vk::binding]]`) never appear in Copeland source. The old Aurelian parser,
validator, stage extractor, emitter, and graphics smoke VD-MIR are retained for
history/tests but are not on this semantic path.

DXC 1.9.2602.24 compiled `cs_6_0` with `-spirv
-fspv-target-env=vulkan1.3 -HV 2021`; `spirv-val --target-env vulkan1.3`
passed. Disassembly proves `OpEntryPoint GLCompute`, `LocalSize 8 1 1`, set-zero
bindings zero/one, and `NonWritable` on Input. Host metadata emits the entry,
workgroup size, and resources. Runtime dispatch was not added because Aurelian
has no bounded compute execution seam; the next runtime step is descriptor
allocation/upload/dispatch/readback over the neutral compiled artifact.

## Evidence and validation

The five compact artifacts are `proof.json`, `vd-mir.json`, `diagnostics.json`,
`backend.json`, and `manifest.json`. `backend.json` embeds the small HLSL and
SPIR-V payload, structural facts, and tool provenance. Repeated HLSL/SPIR-V
hashes match. Timings cover parse, production bind (including its parse), JSON,
HLSL, and DXC plus validation; direct VD-MIR construction has zero separate
projection cost.

Tests cover annotation spans, suffix equivalence, profile selection, safe and
unsafe imports, scalar/vector/builtin/resource binding, duplicate related spans,
numthreads, mutation, indexing, arithmetic, control, deterministic JSON, HLSL,
DXC, validation, structure, repeated compilation, and legacy-path exclusion.

## Exact M2 recommendation

Port only minimal vertex/pixel linkage next: `float2/3/4`, vertex input
locations, one clip-position builtin, one varying, one pixel target, and sampled
texture plus sampler only if the canonical minimal program requires them. Extend
the same AST, binder, VD-MIR, and backend. Keep materials, interpolation
variants, multiple targets, broad semantic spaces, renderer APIs, payload enums,
templates, reflection, and caching out until directly required.

## M2 continuation

`AURELIAN-SDSLV-PORT-M2` completed that bounded untextured graphics slice while
preserving compute. Vertex/pixel streams, linkage, generated HLSL, DXC, and
validated SPIR-V use the same schema with feature level `graphics.m2`.
Texture, sampler, material, and renderer runtime remain deferred. See
`sdsl-v-port-m2-graphics-stream-report.md`.
