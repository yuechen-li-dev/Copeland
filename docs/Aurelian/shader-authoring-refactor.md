# Shader authoring refactor

VTS now enforces `const` as a compile-time binding in both GPU binders. Runtime
immutable values use `let`; mutable locals use `var`. This change applies to the
GPU profile. Ordinary Copeland TypeScript keeps its existing binding semantics.
See the [VTS language guide](visual-typescript-language-port.md) for examples and
the supported static subset.

The implementation uses the existing `GpuStaticEvaluation` adapter and bounded
Copeland evaluator. A lexical constant table preserves values across nested
scopes; runtime bindings are never promoted merely because their initializers
happen to be literal. The same evaluator supports closed vector construction
and fields without adding a second interpreter. Compiler-generated GPU flow
temporaries use `let` as well.

Runtime declarations in shader assets, shader-producing code and embedded shader
fixtures were migrated using the Copeland parser and Roslyn string-literal
boundaries. Module constants and explicit static expressions remain constants.
The migration is deliberately mechanical outside the targeted readability work.
The optional Aetheris bridge adapts runtime-local declaration tokens from its
`cir-visual-ts/2` producer through the same parser. `Program.FieldSource` retains
upstream text and identity; `CompilerFieldSource` exposes current VTS syntax.
Tests require all other tokens to remain identical. No Aetheris source was edited.

The targeted refactor changes `DynamicDiffuse3D.v.ts` to named phase/mode enums
and exhaustive phase matching. The numeric C# parameter ABI is decoded once at
the boundary. Convolution has a named helper and result record, interpolation
weights share a helper, and compact branches are expanded. The R3/R4 paths share
one packed-normal decoder. The area emitter sampler gives intermediate vectors
names while preserving the original arithmetic order. Ray-query commands remain
at their existing typed resource boundary.

## Qualification

Preserve a pre-refactor native qualification and run:

```powershell
./tools/aurelian-shader-refactor/Qualify.ps1 `
    -Baseline artifacts/local/rendering-gi/final `
    -Output artifacts/local/shader-refactor/final
```

This reuses the complete R4 qualification: solution build/tests, compiler tests,
reference/affine/temporal/hybrid/diffuse, R3 ray visibility/sampling/ReSTIR,
TLAS/raster motion, area/tail, and dynamic GI with native FLIP and Vulkan
synchronization validation. It additionally pins the shader source corpus and
GPU binding/lowering sources, then requires decoded-pixel equality for every PNG
in the preserved baseline. `capture-comparison.json` distinguishes byte identity,
pixel differences and missing artifacts. Comparison failure leaves the aggregate
qualification unaccepted.

The accepted run passed 3,380 tests: 1,641 solution tests and 1,739 compiler
tests, with zero failures or skips. All native/FLIP lanes passed with clean
Vulkan synchronization validation. All 681 PNG artifacts were byte-identical to
the preserved R4 baseline, with no missing captures or changed pixels. The 306
pinned source hashes and the proof/graphics binary hashes match the current
implementation. `git diff --check` passed.

Aggregate evidence is in
`artifacts/local/shader-refactor/final/qualification.json`. Earlier migration and
bridge failures remain in the exploratory artifact folders; acceptance was
rerun after their corrections. No Aetheris repository source was changed.
