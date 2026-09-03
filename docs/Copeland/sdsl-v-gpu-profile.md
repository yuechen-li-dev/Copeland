# Copeland SDSL-V GPU profile

## Status

This is the AURELIAN-SDSLV-AUDIT-M0 profile decision, not an implemented
compiler feature. SDSL-V semantics remain owned by Oct's canonical language
specification and conformance corpus. Copeland supplies a second frontend.

## Selection and source files

The compiler target selects the profile before binding. `.v.ts` is the
recommended source convention and is already parsed as a TypeScript module, but
it is not semantic authority. Ordinary `.ts` compiled under the same profile is
equivalent. Parsing does not fork.

A GPU root closes transitively over every reachable function, type, constant and
import. An ordinary module is reusable when each reachable symbol is statically
certified GPU-safe. Unreachable host code need not poison an otherwise safe
shared definition. Diagnostics show the root-to-unsafe-symbol path.

## Closed runtime subset

Allowed after the relevant port slice implements them:

- canonical SDSL scalar, vector, matrix, fixed-array and resource types;
- value records with certified SDSL layout;
- initialized immutable `let` and initialized mutable `var` locals;
- typed arithmetic/comparison/boolean operations and compiler-known intrinsics;
- structured conditionals, bounded/profile-legal loops, break/continue and
  return;
- explicit resource and local mutation permitted by access/mutability law;
- closed, specialized generic functions and compile-time-erased interfaces;
- explicit immutable captures used only as specialization inputs.

Always rejected from shader runtime:

- GC references, arbitrary objects, heap allocation and managed arrays;
- exceptions, shader fallibility, `Result<T,E>` propagation and throwing;
- tasks, threads, async/generators and synchronization outside SDSL intrinsics;
- runtime reflection, `dynamic`, boxing and CLR metadata access;
- classes/virtual dispatch/interface dispatch or open generic dictionaries;
- heap closures, mutable captures and escaping function environments;
- filesystem/network/process/I/O and host package calls;
- raw HLSL semantics or backend string escape hatches unless a separately
  canonical foreign-target contract explicitly admits them.

## Static features

Copeland `interface` maps to an erased structural capability constraint. It has
no GPU runtime representation or dispatch table. Generic functions and template
outputs must be statically closed and monomorphized. Copeland's existing bounded
template evaluator is the only compile-time engine. `reflect` remains
compile-time observation and may later expose typed SDSL layout/binding/stage
facts; no runtime shader reflection is added.

Payload enums require the canonical SDSL tag/payload layout before GPU runtime
admission. Therefore the language decision is SUPPORTED and the implementation
gate is DEFER. `Option<T>` follows that explicit tagged representation.
`Result<T,E>` shader-runtime semantics are UNSUPPORTED because SDSL-V removed
fallibility; a domain status payload enum remains available after layout support.

## Metadata and entries

One small annotation syntax/AST extension is required. It must retain exact
source spans and carry structured constant arguments. The GPU binder recognizes
only the canonical closed set: stage, numthreads, binding, builtin, location,
target, interpolation and semantic space. Unknown annotations are rejected in
the GPU profile.

An entry is an explicitly annotated ordinary function. Compute, vertex and pixel
signatures lower to the same semantic law as Oct. Helper functions are not
entries. Resource parameters/fields are semantic boundaries and do not become
ordinary HLSL parameters. Vertex/pixel linkage is validated before backend
emission.

## Types, resources and layout

GPU primitive/vector/matrix/resource types are compiler-known nominal types,
not CLR library structs and not emitter string matches. Constructors, swizzles,
operators and intrinsics are resolved and typed by the GPU binder.

Every resource records kind, element/value type, readonly/readwrite access,
descriptor set, binding and stage visibility. Current canonical graphics set is
zero; binding is explicit. Storage buffers, uniforms, textures, samplers,
workgroup memory and builtins retain distinct semantic categories.

Copeland records become SDSL value/interface/material structs only after layout
certification. Material follows canonical HLSL-compatible 16-byte register
packing; fixed multidimensional storage follows the canonical row-major rules.
CLR layout is irrelevant. The semantic manifest emits offsets, sizes,
alignments, matrix convention, bindings and interface locations.

## Control and indexing

Control-flow syntax is reused. The profile rejects nonterminating/unbounded
forms where canonical SDSL law requires bounds. Function recursion is rejected
until the language specification and conformance corpus state an explicit law.
Dynamic scalar/vector/resource indexing follows canonical target legality;
descriptor arrays, texture arrays and other unsupported resource shapes remain
rejected.

## IR and diagnostics

The GPU binder emits frontend-neutral versioned SDSL semantic IR rather than
host Copeland MIR or Aurelian's old SDSL AST. IR retains source and related spans
for all semantic/backend failure sites. Diagnostics classify parse, binding,
type, static closure, stage, resource, layout and backend failures and expose a
canonical SDSL category/code for conformance cases.

## Build/tooling contract

The future build invocation may accept `copeland build shader.v.ts --target
sdsl-v`, but the resolved project target is authoritative. `.v.ts` uses existing
TypeScript highlighting/formatting/language service behavior and ordinary module
resolution. Build discovery may prefer the suffix; imports remain normal module
edges and are checked by transitive GPU certification.

Compiler output is deterministic semantic IR plus shader artifacts and binding
metadata. It is never runtime-compiled by `Aurelian.Graphics` and does not depend
on whether the host compiler runs under RyuJIT or NativeAOT.
