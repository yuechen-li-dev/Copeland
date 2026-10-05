# VD-WGSL-X0 — direct Visual TypeScript graphics backend

Status: **Accepted**, 2026-10-05, for the existing bounded graphics profile and
the five CIR-DISPLAY-X0 witnesses. Both the frontend and direct backend ran inside
browser WASM and produced shaders executed by real Edge WebGPU. No native shader
compiler was present in that runtime. This is compiler qualification, not
Cadmata/Helios viewport integration.

## Architecture and API

The production-capable path is now:

```text
*.v.ts → Copeland TS graphics frontend → semantic VD-MIR
      → WGSL preparation → WGSL text → WebGPU
```

`src/Copeland/Copeland.TS.Backend.Wgsl/` is a separate managed backend assembly.
Its only project dependency is `Copeland.TS`; it does not reference
`Aurelian.Shaders` or the DXC package. The native HLSL/DXC/SPIR-V backend and the
Naga bridge remain in `Aurelian.Shaders` unchanged by this milestone. They remain
native backends and reference oracles, not dependencies of direct emission.

```csharp
using Copeland.TS.Gpu.VdMir;
using Copeland.TS.Gpu.Wgsl;

var result = WgslGraphicsBackend.Compile(
    new GpuCompilationRequest([new GpuSourceFile("part.v.ts", source)]));
// Alternatively: WgslGraphicsBackend.Lower(successfulGraphicsModule).
if (!result.Success)
{
    // Report result.Diagnostics, including original source spans.
    return;
}
var program = result.Program!;
// program.Code is one WGSL module, containing both stages.
// Use program.VertexEntryPoint and program.FragmentEntryPoint in WebGPU.
// program.Semantics retains canonical IO, resources, bindings and material layout.
```

The source entry functions become ordinary typed helpers. Generated `vd_vertex`
and `vd_fragment` wrappers convert explicit attributed IO structures into/out of
ordinary semantic stream values. This preserves helper calls taking streams
without attaching stage-only interface assumptions to their local values.

No filesystem, subprocess, native interop or reflection-based serializer exists
in the direct backend. Semantic identity uses the existing source-generated MIR
JSON serializer. The browser qualification bridge also uses source-generated
JSON. The frontend and backend assemblies are rooted in the qualification WASM
build; this is a managed interpreter build, not an AOT-size or trimmed-size claim.

Copeland owns syntax, shader semantics and both backend families. Aetheris owns
CIR specialization into `.v.ts` and future display consumption. There is no
Aetheris WGSL emitter or new field evaluator.

## Audit and LIR decision

No separate shared Shader LIR was added. Existing graphics VD-MIR already has
typed expressions, explicit local mutability, structured blocks/branches,
bounded `for` initializer/condition/increment/body, helper calls, linked stage IO,
canonical resource set/binding/visibility and material byte offsets.

A small **private prepared WGSL module** owns target legality and serialization
inputs. It clones the existing typed function/statement/expression model while
resolving names, physical type spellings, constructors, target intrinsic names,
stage IO wrappers and uniform layout attributes. It is not another semantic
compiler or public IR: it performs no type inference, optimization, CAD/CIR
interpretation, binding allocation or general CFG restructuring.

| Concern | Existing MIR / old backend | Direct ownership and result |
| --- | --- | --- |
| Source syntax | Ordinary Copeland parser and GPU binder | Reused unchanged; no Oct parser or new syntax |
| Scalar types | Explicit `f32`, `u32`, `bool` | Same semantic types; typed numeric literals |
| Vectors/semantic spaces | `float2/3/4`, nominal aliases, member physical type/space | `vecN<f32>`; physical erasure preserves nominal frontend checks |
| Structs | Ordered stream/material members | Ordinary structs plus explicit stage IO wrapper structs |
| Matrices/indexing/arrays | Not admitted by the current graphics binder | Not invented by the backend; named unsupported-type diagnostic |
| Mutable locals | `VdMirStatement.Mutable`; HLSL prints ordinary locals | `let`/`var` from MIR, not inferred from use |
| Loops | Already bounded and normalized by frontend | WGSL `for`; preparation requires explicit mutable initializer and assignment increment |
| Comparisons/unary/branches | Typed expressions and structured bodies | Preserved; branches receive separate lexical name scopes |
| Object returns | HLSL expands them into temporary assignments | Preparation resolves ordered typed struct constructors |
| Functions/calls | Closed helper graph, frontend rejects recursion | Mangled helpers and explicit wrappers; WGSL permits declaration-order independence |
| Entry stages | Vertex/pixel enum and input/output streams | Explicit `@vertex`/`@fragment`, no entry-name inference |
| Builtins | Canonical MIR identities; HLSL maps to system semantics | Position/depth preserved; vertex/instance IDs and front face mapped to WGSL builtin identities |
| Interpolation | MIR `linear` means HLSL default perspective; `noperspective` is distinct | `perspective` / `linear` / `flat`; never mechanically map MIR `linear` to WGSL `linear` |
| Resources | Explicit canonical set, binding, kind and visibility | Same group/binding numbers; texture, sampler, `var<uniform>` |
| Resource parameters | HLSL erases resource stream arguments into globals | Same semantic erasure during preparation; no allocation while printing |
| Material layout | Frontend owns offsets/size/alignment; DXC accepted the struct | Explicit `@align`/`@size` retains gaps and final size; 32-byte material executed |
| Address spaces | Graphics profile has readonly textures/samplers/materials | Explicit uniform variables; function-local variables; no graphics storage/workgroup subset invented |
| Numeric conversion | Explicit `ConvertU32ToF32` intrinsic | `f32(...)`; no implicit cross-type arithmetic added |
| Derivatives/sampling | HLSL/DXC enforce stage legality downstream | Reachable-function stage check rejects vertex derivative/implicit-LOD sampling; WebGPU checks uniformity |
| Fragment discard/depth | Existing M4 statements and `frag_depth` member | Fragment-only discard and `@builtin(frag_depth)`; depth read back on GPU |
| Names | DXC/HLSL tolerate different reserved vocabulary | Disjoint generated identifiers for types/functions/resources/locals/members |
| Traceability | MIR spans already exist | Generated function/statement line → `.v.ts` span map |

The WGSL [type/layout and IO rules](https://www.w3.org/TR/WGSL/) require explicit
host-shareable layouts and interpolation. Current graphics material profiles do
not admit boolean buffers. Preparation also rejects boolean uniform fields rather
than silently choosing a host encoding. Plain function-local booleans are supported.

Matrices are not a missing serializer spelling: the current graphics frontend
does not express matrix construction, indexing, orientation or matrix-vector
multiplication. An unsupported-matrix MIR test verifies a source-bearing stop.
Adding those semantics, `i32`, arrays or compute WGSL is a future frontend/profile
milestone. Existing native compute emission remains available.

## Identity, diagnostics and runtime boundary

`SemanticHash` hashes canonical semantic MIR plus `vd-wgsl/1`. Source files,
source spans and metadata spans are removed; typed numeric literal spellings are
normalized. Formatting emitted WGSL never enters this hash. Tests establish that
path/whitespace/`0.0100` versus `0.01` do not change identity, while a changed depth
expression does. A future Aetheris cache can combine its existing CIR structural
hash with the backend compatibility identity and semantic shader hash. No new
production cache or per-occurrence compiler is claimed here.

Known target failures return `COPE-WGSL-*` diagnostics with MIR source spans.
Frontend diagnostics retain their existing codes. Browser consumers must still
inspect `GPUShaderModule.getCompilationInfo()` and pipeline creation errors: a
managed text emitter cannot guarantee device-specific acceptance. The witness
records message/line/column and maps to the nearest generated statement/function
origin. This is useful traceability, not a token-perfect source map.

The negative GPU witness changes a generated `sqrt` call into `vd_missing` and
captures `unresolved call target 'vd_missing'`, mapped to `Sphere.v.ts`. It is an
expected failure assertion, not a swallowed error.

Build/runtime responsibilities are explicit:

- Native build tooling builds the managed assemblies and browser WASM bundle.
- Browser WASM parses the actual `.v.ts`, binds VD-MIR and emits WGSL dynamically.
- WebGPU creates/validates shader modules and render pipelines on the adapter.
- The differential oracle is previously compiled X0 WGSL, read as an artifact.
  No browser call invokes DXC/Naga, even in differential mode.

## Reproduction and evidence

From Copeland:

```powershell
pwsh -File scripts/qualify-vd-wgsl-x0.ps1 -X0Directory <actual-CIR-DISPLAY-X0-output>
```

This uses the five real generated X0 sources, not rewritten witness fields. It
builds a browser-WASM host with the direct backend only. Serve the printed
`wwwroot` directory on localhost. The normal page compares with `oracle.json`;
`/?directOnly` executes without requesting oracle artifacts. The latter works
with only the `.v.ts` sources; `programs.json` is optional when building the site.

No DXC/Naga project/package appears in the browser host's project closure or
served framework assets. Thus the direct-only run proves more than successful
emission on a machine with those tools installed: compilation executes in the
browser sandbox where native subprocesses are unavailable.

Qualification: Edge **153.0.0.0**, Windows, WebGPU available, adapter vendor
**NVIDIA**, architecture **Ampere**. Adapter description/driver were not exposed;
no more specific GPU or driver is inferred.

| Witness | Direct hit pixels | Oracle hit pixels | Coverage disagreement | Maximum depth difference |
| --- | ---: | ---: | ---: | ---: |
| Sphere | 22,864 | 22,864 | 0 | 0.00000370 |
| Cylinder | 25,868 | 25,868 | 0 | 0.00000334 |
| Cone | 21,566 | 21,566 | 0 | 0.00000337 |
| Torus | 16,416 | 16,416 | 0 | 0.00000328 |
| CSG | 25,867 | 25,867 | 0 | 0.00000334 |

Every common-hit RGB channel differed by at most **1/255**. The report also records
representative center/offset pixel values and exact depth samples. Full depth
attachments were copied/read as `depth32float`, so this checks fragment depth,
not merely visible color or assumed pass ordering.

Differential thresholds are explicit: coverage disagreement ≤131 pixels out of
65,536; mean depth error <0.00002; maximum depth error <0.0005; mean RGB byte error
<1. Measured results are substantially tighter than these limits. Direct-only
mode independently checks nontrivial coverage and finite depth in [0,1].

The actual ForwardTextured M3 shader executes texture/sampler/material bindings
0/1/2, vertex/instance builtins, front-facing selection, explicit u32→f32 conversion
and the 32-byte material ABI. Its non-white texture distinguishes sampling from
the unsampled tint branch. All six existing assets—SoftShockwave, SemanticFog,
ReactiveFluid2D, ProfileMsdf, MsdfText and AnalyticShape2D—also pass real WebGPU
shader-module validation with zero diagnostics. These additional six were
module-validated, not independently visually qualified.

Local regenerated evidence is ignored under `artifacts/local/vd-wgsl-x0/`:

- `browser-report.json`: differential samples, times, sizes, bindings, negative diagnostic.
- `direct-browser-report.json`: independent direct-only browser-WASM run.
- `browser-witness.jpg`: side-by-side direct/oracle primitive and CSG captures.
- `direct-witness.jpg`: direct-only capture.
- Build and full regression logs for Copeland, Aurelian and Aetheris.

![Direct and reference shader rendering](../../artifacts/local/vd-wgsl-x0/browser-witness.jpg)

## Performance and text size

The browser report measures backend preparation/hash/emission separately from
frontend+backend wall time, shader-module creation/compilation-info wait, and
pipeline creation. WASM's first compile costs more than subsequent compiles.
These are qualification observations, not a cross-platform performance promise.
On observed runs, direct backend generation was roughly 160–170 ms for the first
sphere and 50–79 ms subsequently. Module creation/diagnostic waits were roughly
0.3–3.5 ms after initial compilation; pipelines benefit from browser/driver caches.

The stored X0 native reference costs were 174.7–200.6 ms for DXC+Naga alone. Those
were measured on a different host execution path and omit frontend/HLSL-generation
time. They are reported separately; no native-versus-WASM speedup ratio is asserted.
The benefit qualified here is removing native runtime dependencies.

| Witness | Direct WGSL bytes, both stages in one module | Oracle WGSL bytes, two modules |
| --- | ---: | ---: |
| Sphere | 2,680 | 6,263 |
| Cylinder | 2,886 | 7,708 |
| Cone | 3,269 | 11,504 |
| Torus | 2,740 | 6,756 |
| CSG | 3,369 | 8,985 |

The difference largely reflects direct helper reuse versus DXC/Naga specialization
and module state. Smaller source does not establish lower GPU cost.

## Friction and cleanup

| Previously implicit concern | Old owner | Direct fix / evidence |
| --- | --- | --- |
| HLSL `linear` naming versus WGSL interpolation vocabulary | HLSL emitter/DXC | Preparation maps canonical default to perspective; linked GPU witness |
| HLSL struct-return assignments | HLSL emitter | Typed constructor normalization before serialization; real X0 early returns |
| Entry IO versus helper stream values | DXC/SPIR-V interface lowering | Explicit IO wrappers, ordinary value structs; resource/builtin shader execution |
| Uniform final padding | Canonical material layout + DXC | Explicit WGSL field sizes; actual 32-byte GPU uniform |
| Reserved target identifiers | Native compiler vocabulary | Deterministic mangling; WGSL reserved/source builtin-name test |
| Literal type ambiguity and alternate spelling | DXC conversion/constant handling | Typed literal normalization shared by emission preparation and semantic identity |
| Derivative stage constraints | DXC shader-profile validation | Reachability legality check and source-bearing diagnostic |
| Browser compiler payload including native shader tooling | Native `Aurelian.Shaders` package | Separate managed backend project; real browser-WASM direct-only execution |
| Reflection serializer trimming | Native host did not need browser trimming | Source-generated qualification/MIR JSON; browser build and dynamic compile |

The dedicated quality pass separated the backend from the frontend assembly,
kept one canonical semantic metadata object instead of parallel resource/material
copies, normalized literal identity independently of emitted formatting, made
stage/loop legality explicit before printing, and narrowed solution changes to
the new backend project. No renderer logic, field evaluator, optimizer, storage
binding allocator or frontend syntax changes were added. Existing X0 native
infrastructure was preserved.

Regression gates: Copeland TS solution **1,826** tests across three suites;
Aurelian **816** across 22 suites, including native HLSL/DXC and Naga bridge tests;
Aetheris fast lane **1,005**, then full serial lane **4,312** across 20 suites.
All passed with zero failures/skips. Managed backend/WASM and solution builds
passed. Aetheris retained its existing WebAssembly SQLite warning; this is not
hidden as a direct shader dependency.

Executive verdict: **Accepted**. The existing bounded `.v.ts` graphics profile
now emits and executes WGSL directly from VD-MIR, including all five X0 CIR
witnesses, without native compiler dependencies in its browser/WASM runtime.
The remaining shared display-host migration is outside this milestone.
