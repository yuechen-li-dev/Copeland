# Visual TypeScript graphics M4: bounded implicit display support

This extension supports shader specialization for Aetheris CIR. Copeland owns syntax,
binding and `vdmir.semantic.v1`; Aurelian.Shaders owns HLSL emission, DXC invocation
and browser artifact conversion. Aetheris owns CIR operation mapping. No CIR interpreter
or alternate shader compiler is introduced.

## Source and semantics

The existing Copeland TS parser handles `*.v.ts`. Graphics M4 admits:

- Mutable `var` locals and typed local assignment; parameters, `const`, material
  inputs and loop counters inside the body remain immutable.
- Scalar `f32`/`u32` comparisons, boolean equality/logic, scalar negation and `!`.
- Lexically scoped blocks and branch statements, including local declarations.
- `for (var i: u32 = 0; i < 256; i = i + 1)` with literal unsigned bounds,
  a fresh counter, ascending unit step and no more than 4096 iterations per loop.
  Nested loops multiply work; admission is a finite per-loop bound, not a GPU-time guarantee.
- `break` within loops and `Discard()` within pixel-stage code.
- A pixel output `@builtin(frag_depth) depth: f32` alongside `@target(0) color: float4`.

Fragment depth maps to HLSL `SV_Depth`, SPIR-V `FragDepth` and WGSL `frag_depth`.
The shader supplies normalized depth in the browser's [0, 1] convention. It must
derive this from the actual hit and the camera's projection. The compiler does not
silently clamp values or infer a projection. Duplicate builtins, wrong types and
vertex-stage depth are rejected. Compiled graphics metadata retains a separate
optional `FragmentDepth` descriptor; depth is not exported as a color attachment.

Oct's current `internal/sdslv/validate/graphics.go` explicitly rejects builtins in
pixel output. This is a Visual TypeScript extension, not a claim of implemented Oct
fragment-depth conformance. Oct range/step/descend grammar was not added to the
shared TS parser: the existing C-style parser covers bounded traversal without a
second grammar or changes to host TypeScript syntax.

## Browser artifact path

`WebGpuGraphicsBackend.Compile(module)` uses the existing VD-MIR HLSL emitter,
DXC's Vulkan 1.1 SPIR-V target, `spirv-val`, and Naga CLI 27.0.0. It translates
both stages to WGSL, then reparses and validates each WGSL output. The normal
Vulkan backend retains its Vulkan 1.3 default. Browser compilation deliberately
uses Vulkan 1.1 because Naga 27 rejects DXC's Vulkan 1.3
`DemoteToHelperInvocation` discard instruction. No instruction rewriting occurs.

`--keep-coordinate-space` preserves the supplied clip coordinates and depth.
Callers must supply the WebGPU camera convention. Tool errors, missing tools,
empty outputs and timeouts fail with diagnostics; subprocesses have a 15 second
limit and temporary artifacts are cleaned up. Unsupported SPIR-V capabilities
fail translation; arbitrary existing Vulkan shaders are not promised compatible.

Install the pinned translator locally (requires Rust/Cargo):

```powershell
./scripts/install-webgpu-compiler.ps1
dotnet test tests/Aurelian/Aurelian.Shaders.Tests -c Release -m:1
```

The script sets `AURELIAN_NAGA` for the current process. In another shell set that
variable to the installed executable, or put `naga` on PATH. The WebGpuToolchain
test intentionally fails when the translator is absent. DXC and SPIR-V tools use
their existing discovery mechanisms. No sibling-repository paths are encoded in
the production compiler.

## Qualification

The implicit shader tests compile a bounded sphere traversal with local mutation,
discard and fragment depth through DXC, and validate both emitted WGSL stages.
Negative tests cover excessive/dynamic bounds, invalid steps, counter mutation,
immutable locals, invalid/duplicate depth, vertex-stage misuse, escaped branch
locals and break outside a loop. Aetheris's separate qualification script lowers
real CIR sphere/cylinder/cone/torus and a rigid CSG expression through this path.
The generated shaders rendered in Edge 153 on an NVIDIA Ampere WebGPU adapter.

This proves compiler and browser artifact support. Cadmata/Helios scene integration,
shared occurrence programs, mixed-object depth, CAD picking and topology overlays
need their own qualification; shader compilation is not evidence for those claims.
