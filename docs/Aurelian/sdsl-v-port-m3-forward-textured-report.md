# AURELIAN-SDSLV-PORT-M3 forward-textured report

## Outcome and revisions

**Outcome A — the bounded production ForwardTextured language surface is complete.**
Oct stayed clean at `584bd176fd50664edadcb2bc3ae78431ac0f1e51`.
Copeland started at `83ef70561fb9708e2c3e09b1d3f48166dac11346`; the
result is the working tree on that revision.

```text
Visual TypeScript
-> Copeland GPU bind/link/layout
-> vdmir.semantic.v1 / graphics.m3
-> Aurelian HLSL
-> DXC vs_6_0 + ps_6_0
-> Vulkan 1.3 SPIR-V
-> spirv-val
```

No renderer, pipeline, descriptor upload, texture asset, material instance, or
shader-cache runtime was added.

## Mandatory Oct semantic audit

The audit read the current language specification, canonical conformance
manifest and ForwardTextured source/bundle, validator, lowering, VD-MIR,
material packer, HLSL emitter, DXC policy, and SPIR-V facts.

| Feature | Spec law | Conformance law | Oct behavior | Classification | Action |
| --- | --- | --- | --- | --- | --- |
| semantic-space aliases | `(physical type, dotted space)` is nominal; no implicit spaced/unspaced or cross-space conversion | coordinate assignment/call/return/linkage negatives | retained through validation and erased for HLSL storage | CONSISTENT | port prefix annotation sugar |
| builtin/resource/stage roles | exactly one role | `MixedStreamRole` | inferred and recorded before lowering | CONSISTENT | extend M2 role path |
| texture | readonly `texture2d<T>`; ForwardTextured uses `float4` | binding 0, set 0 | typed texture resource | CONSISTENT | port only `Texture2D<float4>` |
| sampler | readonly sampler handle | binding 1, set 0 | typed sampler resource | CONSISTENT | port handle only |
| `Sample` | texture, sampler, plain `float2`; vertex/pixel legal; result `T` | `SampleWithoutSampler`, `WrongSampleCoordinate` | typed intrinsic lowered to HLSL method spelling | CONSISTENT | port `Sample2D` identity |
| material | immutable shader-local uniform record | ForwardTextured tint/roughness | generated readonly constant buffer | CONSISTENT | use annotated record plus resource member |
| material layout | HLSL-compatible 16-byte registers | bundle offsets/sizes | upstream layout pass, not HLSL reflection | CONSISTENT | port exact packer |
| bindings | explicit, set zero, collision checked | bindings 0/1/2 and duplicate negative | source order retained | CONSISTENT | reuse one collision table |
| visibility | stage resource parameters establish use | pixel SPIR-V has bindings 0/1/2; vertex has none | shared declarations emitted; unused vertex resources removed by DXC | CONSISTENT | record pixel visibility |
| UV space | canonical UV is plain `float2` | varying location 0 | `Sample` rejects spaced coordinates | CONSISTENT | do not invent TextureUv |
| matrix convention | not used by ForwardTextured material | absent | no M3 matrix field | CONSISTENT | exclude |

No `IMPLEMENTATION_BUG`, `SPEC_GAP`, or blocking ambiguity was found; therefore
Oct required no code, spec, conformance, or golden change.

## Source and type laws

Copeland uses its existing prefix annotation architecture:

```ts
@space(object.position)
type ObjectPosition3 = float3;

@material
@binding(2)
record SurfaceMaterial {
    tint: float4;
    roughness: f32;
}
```

Semantic aliases retain alias name, dotted space, physical vector, and source
span. Constructors and explicitly typed helper returns establish a space;
implicit cross-space and spaced/unspaced assignments are rejected. Linkage
compares location, physical type, semantic space, and interpolation, with both
producer and consumer spans.

Each stream member remains exactly one of stage value, builtin, or resource.
M3 admits canonical vertex ID, instance ID, position, and front-face builtins,
but no source `SV_*` spelling. Resource stream parameters are semantic compiler
boundaries and disappear from HLSL entry signatures.

## Texture, sampling, resources, and material

The bounded resource types are exactly `Texture2D<float4>`, `Sampler`, and the
canonical material. All are readonly, set zero, explicitly bound, source
ordered, and collision checked together. `Sample(texture, sampler, uv)` binds
to VD-MIR `Sample2D`; coordinates must be unspaced `float2` and the result is
`float4`. Raw `texture.Sample`, sampler configuration, other texture shapes,
derivatives, and implicit LOD controls remain absent.

Material writes receive immutable-binding diagnostic `SDSL-V3701` with the
declaration as related evidence. The packer aligns each field naturally,
starts a field at the next 16-byte boundary if it would cross a register, and
rounds final size to 16 bytes.

| Field | Type | Offset | Size | Alignment |
| --- | --- | ---: | ---: | ---: |
| `tint` | `float4` | 0 | 16 | 16 |
| `roughness` | `f32` | 16 | 4 | 4 |

Total size is 32 bytes; material is set 0/binding 2 and pixel-visible in the
canonical program. Semantic metadata, not CLR layout or SPIR-V reflection, is
the future host upload authority.

## VD-MIR, backend, and renderer metadata

The schema remains `vdmir.semantic.v1`; additive feature level is
`graphics.m3`. It adds semantic-space records, physical and nominal stream
types, texture/sampler/material resources, stage visibility, `Sample2D`, exact
material layout/provenance, and material resource identity. Stable source
sorting and canonical field/resource order keep JSON deterministic.

`GraphicsProgram` is the compact renderer-facing projection: entries, vertex
inputs, linked varyings, pixel targets, resources, bindings, kinds, visibility,
semantic spaces, and material layout. A future `CompiledGraphicsProgram` can
combine this with the two SPIR-V blobs; the renderer must not reparse source or
HLSL and must not replace semantic metadata with SPIR-V reflection.

Aurelian generates stage structs, HLSL semantics, `Texture2D<float4>`,
`SamplerState`, `ConstantBuffer<SurfaceMaterial>`, Vulkan binding attributes,
and `albedo.Sample(...)`. Both stages compile with `vs_6_0`/`ps_6_0`,
`-spirv -fspv-target-env=vulkan1.3 -HV 2021`, and pass `spirv-val`. Disassembly
proves Vertex/Fragment entry models, locations, vertex Position, and pixel set
zero bindings 0, 1, and 2. Vertex resource bindings are absent after ordinary
unused-resource elimination, matching canonical use.

Hashes and timings are in `artifacts/aurelian-sdslv-port-m3/proof.json`. The
current binder reports bind, linkage, space validation, material layout, and
projection as one small in-memory phase rather than introducing timing-only
architecture.

## Diagnostics, regressions, and archaeological classification

Focused diagnostics cover non-texture/non-sampler/wrong-coordinate sampling,
duplicate bindings with related spans, resource/location role conflicts,
unsupported material shape, immutable material mutation, nominal assignment,
and semantic-space linkage mismatch. `.ts` and `.v.ts` are equivalent. M1
compute and M2 untextured graphics remain unchanged; imported helpers work.

| Inspected Oct feature | Classification |
| --- | --- |
| richer ordinary graphics math/body | PORT SOON only when production needs it |
| payload enums, match, flow, concepts/templates | ESCAPE HATCH LATER |
| HLSL structs, semantics, registers, material offsets | COMPILER-INFERABLE |
| tile/register-tile/manual staging forms | CANDIDATE FOR DEPRECATION from ordinary production authoring |
| f16, push/specialization constants, texture arrays/storage images | ESCAPE HATCH LATER |

## Tests, artifacts, and next milestone

Copeland adds four focused tests for the positive program, aliases/linkage,
resource/sample/material diagnostics, `.ts`/`.v.ts`, and deterministic JSON.
Aurelian adds three focused tests for generated HLSL, both DXC/SPIR-V lanes,
descriptor facts, deterministic output, and renderer metadata. Existing suites
provide compute and M2 regression coverage.

The artifact budget is exactly five files:

```text
artifacts/aurelian-sdslv-port-m3/
  proof.json
  vd-mir-forward-textured.json
  diagnostics.json
  backend.json
  manifest.json
```

The exact next milestone is **AURELIAN-NATIVE-FORWARD-TEXTURED-M0**: make the
existing native Vulkan path consume one `CompiledGraphicsProgram` containing
M3 vertex/pixel SPIR-V and renderer metadata; create one pipeline layout from
bindings 0/1/2, upload one 32-byte material and one fixture texture/sampler,
and draw one offscreen textured triangle/quad with a canonical readback hash.
Do not add a material object model, asset cache, frame graph, camera, mesh
system, shader recompilation, or broader language features.

## Final validation snapshot

- Oct `go test -count=1 ./internal/sdslv/...`: all packages passed, including
  uncached conformance.
- Fresh Oct ForwardTextured: replay
  `270415a98a26d4e7a449054b9174f0d20377b8151401805d39caea263ab4eac3`,
  vertex `cbd22a4...`, pixel `890f4f67...`, both SPIR-V validated.
- `Copeland.TS.slnx`: 1,601 passed, zero failed/skipped.
- `Aurelian.slnx`: 625 passed, zero failed/skipped.
- `JointTaskForce.slnx`: all test projects passed with zero failures/skips.
- M3 hashes: VD-MIR `e6fb665e...`, HLSL `305a7b4e...`, vertex SPIR-V
  `13d86b3a...`, pixel SPIR-V `e9d019d2...`; repeat hashes matched.
- The five JSON artifacts parse, total 71,810 bytes, and `git diff --check`
  passes. Oct has no diff.

## Native renderer qualification update

AURELIAN-NATIVE-FORWARD-TEXTURED-M0 is complete with Outcome A. The production
exporter creates one renderer-neutral `CompiledGraphicsProgram` from graphics.m3
VD-MIR and validated DXC stages. The existing Aurelian Vulkan path derives
vertex and descriptor layouts, uploads the compiler-described 32-byte material
and a 2x2 texture, draws a 64x64 offscreen quad, and reads back canonical RGBA
bytes. Ten reused-device runs and one fresh-device run produced SHA-256
`521e2788a769bb98bd3cc8f966fba3940e2d5a7ad0cd0ff06ac52ceea16c60f7`
with semantic texture/tint assertions and Khronos validation clean. See
`docs/Aurelian/aurelian-native-forward-textured-m0-report.md` and
`artifacts/aurelian-native-forward-textured-m0/`.
