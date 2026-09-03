# AURELIAN-SDSLV-PORT-M2 graphics stream report

## Outcome and revisions

**Outcome A — graphics stream semantics port cleanly to Visual TypeScript.**

Oct stayed clean at `584bd176fd50664edadcb2bc3ae78431ac0f1e51` before and
after the audit. Copeland started at
`3d5d9f47688b329c16d25389aacda65801e8c528`; the result is the working tree
based on that revision.

```text
ordinary Copeland .ts/.v.ts parser
-> GPU graphics binder and linkage
-> vdmir.semantic.v1 / graphics.m2
-> generated vertex/pixel HLSL
-> DXC vs_6_0 + ps_6_0
-> Vulkan 1.3 SPIR-V
-> spirv-val and structural interface proof
```

## Pre-port Oct graphics audit

The audit inspected Oct's specification, reconciliation ledger, conformance
manifest and fixtures, validator, VD-MIR lowering, HLSL emission, graphics
bundle builder, DXC policy, and SPIR-V inspection.

| Feature | Spec law | Conformance case | Oct behavior | Classification | Action |
| --- | --- | --- | --- | --- | --- |
| stream role | one stage-value, resource, or builtin role | `MixedStreamRole` | infers role; rejects mixing with `SDSL-V4102` | CONSISTENT | port law |
| stages | compute, vertex, pixel only | minimal and forward graphics | separate entries | CONSISTENT | add vertex/pixel |
| locations | explicit preserved; omitted fill free slots | minimal/forward | deterministic source order | CONSISTENT | port inference/collisions |
| clip position | exactly one clip-space float4 vertex output | minimal/missing/duplicate | lowers to Position | CONSISTENT | TS sugar normalizes to same builtin |
| pixel targets | plain float scalar/vector target | minimal pixel/MRT | deterministic targets | CONSISTENT | bound to float4 target zero |
| interpolation | linear default; flat/noperspective explicit | linkage validator | type/mode must agree | CONFORMANCE_GAP | add focused Copeland tests; no Oct change |
| linkage | location/type/space/interpolation agree | `VaryingMismatch`; forward textured | checks before emission with related span | CONSISTENT | dedicated linkage phase |
| vectors | typed float vectors/constructors/components | minimal/forward | closed validation | CONSISTENT | required float2/3/4 subset |
| resources | binding streams do not link as varyings | forward textured | shared resource model | CONSISTENT | defer realization to M3 |
| texture/sampler/Sample | typed backend-neutral resources/intrinsic | forward textured/sampling negatives | canonical | CONSISTENT | defer to M3 |
| semantic spaces | nominal identity participates in linkage | coordinate corpus | exact compatibility | CONSISTENT | defer TS surface to M3 |
| material | record/uniform sugar | forward textured | deterministic layout/binding | CONSISTENT | defer to M3 |
| old interfaces/fallibility | not canonical | migration negatives | rejected | LEGACY/EXPERIMENTAL | do not port |

No implementation bug, specification gap, or ambiguity affected this slice, so
Oct required no spec, conformance, reference, or Go change. Stream binding is
PRODUCTION CORE. Temporary backend structs and semantic strings are
COMPILER-INFERABLE. Removed interfaces/fallibility are LEGACY/EXPERIMENTAL.

## Stream semantics and syntax

A stream is a compiler-owned typed boundary. VD-MIR retains stable stream ID,
source order, member name/type/role, location/builtin/target, interpolation,
and exact member/metadata provenance. Conflicting member markers and streams
mixing stage-value, resource, or invocation-builtin roles are invalid.

The canonical Visual TypeScript surface is:

```ts
stream VertexInput {
    @location(0)
    position: float3;
    @location(1)
    uv: float2;
}

stream VertexOutput {
    @builtin(position)
    position: float4;
    @location(0)
    uv: float2;
}

stream PixelOutput {
    @target(0)
    color: float4;
}
```

Copeland's unrelated layout form remains `stream Name<x, y> { ... }`. The
ordinary parser selects a dedicated `ShaderStreamDeclarationSyntax` only for
the unambiguous typed-field shape `stream Name { ... }`. This keeps the layout
and GPU binders separate without creating a shader grammar or suffix mode.

Stage-value role follows location, target, clip-position metadata, or stage
position. Explicit locations reserve numbers first; omitted members receive
the next free location in source order, matching Oct. `linear` is default and
`flat`/`noperspective` are semantic values. `@builtin(position)` is TS-shaped
sugar for Oct's `float4@space(clip.position)` output identity: it remains a
stage-value member, consumes no location, and lowers to Position. Resource
markers participate in mixed-role rejection, but resource emission is deferred.

## VD-MIR, stages, and linkage

The schema stays `vdmir.semantic.v1` with feature level `graphics.m2`.
`VdMirStream`/`VdMirStreamMember` preserve stream meaning.
`VdMirGraphicsEntryPoint` records stage and typed input/output boundaries.
`VdMirGraphicsProgram` pairs vertex and pixel entries and owns linked varyings,
vertex inputs, pixel targets, and the future resource projection.

Entries must be concrete, closed, stage legal, and fully linked. Vertex output
requires exactly one `float4` position. Pixel outputs require unique explicit
targets and are bounded to `float4`. Linkage checks both directions by location,
physical type, and interpolation before HLSL. Missing varyings use
`COPE-GPU-LINK-0001` / `SDSL-V4111`; mismatches use `COPE-GPU-LINK-0002` /
`SDSL-V4111` with pixel primary and vertex related spans. Duplicate locations,
duplicate targets, missing position, invalid stage builtins, and mixed roles
carry canonical codes and exact provenance.

M2 adds `float2`, `float3`, `float4`, only the constructors used by the example,
and bounded xyzw component/swizzle access. Stage-less imported helpers use the
same reachable GPU-safe closure; `PassUv(float2)` is called by both stages.
Texture/sampler/Sample, resource streams, semantic-space aliases, and material
are intentionally deferred. No payload enums, template/reflect expansion,
advanced memory features, additional stages, or renderer source syntax was
added.

## HLSL, SPIR-V, and host metadata

`Aurelian.Shaders.Graphics` consumes only successful linked VD-MIR. It generates
all HLSL structures, member assignments, `TEXCOORDn`, `SV_Position`,
`SV_Targetn`, and interpolation modifiers. Source authors maintain none of
those. Separate invocations use `vs_6_0` and `ps_6_0` with `-spirv
-fspv-target-env=vulkan1.3 -HV 2021`. Both outputs pass
`spirv-val --target-env vulkan1.3`.

Disassembly proves Vertex and Fragment execution models, exact entry names,
location zero for the linked varying and fragment output, and `BuiltIn Position`
for vertex output. `GraphicsProgram` supplies future host reflection facts for
vertex formats, stage linkage, outputs, resources, and entries without parsing
HLSL. No Vulkan pipeline, descriptor runtime, vertex-buffer API, swapchain,
renderer, camera, mesh, upload, or material runtime was added.

The historical `Aurelian.Shaders.Language` frontend remains only for historical
tests. Reflection tests prove it is not a parameter dependency of the M2
graphics backend. VD-MIR is semantic authority.

## Parity, determinism, performance, and boilerplate

Semantic—not source—parity uses Oct IDs `graphics.minimal-vertex` and
`graphics.minimal-pixel` for stage facts plus the linkage law of
`graphics.canonical-forward-textured`. The untextured paired example has the
same stages, stream roles, physical types, location-zero varying, position,
target zero, linear interpolation, and empty resource set. It does not claim
byte parity with differently shaped Oct sources.

Repeated compilation produces identical graphics JSON, generated HLSL, vertex
SPIR-V, pixel SPIR-V, and linkage metadata hashes. `proof.json` records hashes
and observational timings for parse/bind/link, JSON serialization, backend
compile/validation, and complete repeat. The current API does not split the
small in-memory binder/linker into artificial timing interfaces; M2 adds no
caching or performance architecture.

The source declares four semantic streams and three interface annotation kinds.
Generated HLSL contains four structs plus every semantic string and assignment.
Thus application code avoids duplicated stage structs, HLSL semantics, and a
manual linkage table. This is qualitative stream-value evidence rather than
mechanical LOC optimization.

## Tests, artifacts, and validation

Copeland tests cover stream parse/bind, preservation of the layout-stream node,
member provenance, stages, vector types/construction/access, location metadata
and inference, position, target, role inference/mixing, linkage, missing and
mismatched varyings, duplicate locations/targets, stage builtin rejection,
interpolation agreement, `.ts`/`.v.ts` equivalence, imported shared helper
closure, and deterministic JSON. Aurelian tests cover both HLSL stages,
generated structs/semantics, both DXC profiles, both SPIR-V validations,
structural facts, linked metadata, repeat hashes, and legacy exclusion. Existing
M1 compute tests prove compute preservation.

The compact artifact budget is exactly five files:

```text
artifacts/aurelian-sdslv-port-m2/
  proof.json
  vd-mir-graphics.json
  diagnostics.json
  backend.json
  manifest.json
```

Final validation covers `Copeland.TS.slnx`, `Aurelian.slnx`,
`JointTaskForce.slnx`, Oct `go test ./internal/sdslv/...`, canonical conformance,
real DXC and `spirv-val`, repeat artifact generation, artifact budget, and
`git diff --check`.

Observed final results: `Copeland.TS.slnx` passed 1,597 tests,
`Aurelian.slnx` passed 622, and `JointTaskForce.slnx` passed 3,214 with zero
failures/skips. Oct's full SDSL-V package tree and uncached conformance test
passed. A fresh canonical `ForwardTextured` bundle reproduced authoritative
vertex hash `cbd22a4e...` and pixel hash `890f4f67...`. During the broad lane an
existing compute validator launch exposed a process-PATH race; both compute and
graphics backends now prefer the absolute `VULKAN_SDK/Bin` tool when available,
and the complete Aurelian/JTF reruns passed. JSON parsing, the exact five-file
budget, and `git diff --check` also passed.

## Exact M3 recommendation

Port exactly the remaining facts needed by
`graphics.canonical-forward-textured`: nominal semantic-space aliases, separate
vertex/pixel builtin streams, one resource stream with `texture2d<float4>` and
`sampler`, shared set-zero bindings, typed `Sample(texture, sampler, float2)`,
and the existing canonical tint/roughness material uniform layout. Reuse M2's
`GraphicsProgram`, linkage, provenance, backend, and reflection paths. Stop at
one texture, one sampler, and one material block. Do not add MRT expansion,
descriptor allocation, Vulkan pipeline creation, renderer/material objects,
geometry/tessellation/mesh stages, broad templates, payload enums, or general
swizzles/intrinsics.
