# M15 allocator language audit

## Decision

M15 does not change Copeland syntax or redefine `layout`. The smallest principled owner is the ordinary, sprite-free `Copeland.SpanAllocation` runtime kit. Asset compilation and Machina adapt typed payloads to that kit; sampling remains a separate realization decision.

## 1. What `Span<T>` means today

`Span<T>` is a compiler-known, immutable, contiguous ordered region of `T`. `SpanTypeSymbol` preserves its element type, MIR lowers it to `MirArrayType`, and the current concrete carrier is an immutable array value. The qualified `Span<int>` and `Span<ProfileSegment>` proofs establish typed indexing/composition, not resource allocation. Profile spans additionally obey owner and stale-generation rules because Profile owns contour semantics.

It is therefore wrong to make allocator placements themselves mutate a Copeland `Span<T>` or to borrow Profile's ownership rules. An allocator consumes an ordered request collection and returns ordinary inspectable placement records.

## 2. What `layout` means today

Copeland `layout` is finite immutable spatial data: named nodes, origins, boxes, layers, bindings, streams, contracts, and derivation provenance. `layout type` is a closed named-node contract. `LayoutDataCompiler` normalizes that spatial graph, and `tscl layout inspect` projects its compiler facts.

## 3. Is `layout` appropriate for 1D allocation?

Not in M15. Both ideas involve bounded placement, but current `layout` syntax and diagnostics are explicitly two-dimensional structural declarations. Teaching it fixed/flex 1D resource allocation would broaden its meaning, parser, binder, normalization, MIR, and inspection contracts without pressure from a second language consumer.

## 4. Correct execution boundary

Resolution belongs in a small standard/runtime kit expressed as ordinary generic C#. `*.obj.ts` uses ordinary Copeland records, functions, tables, arrays, and `static` evaluation to author semantic requests. The compiler decodes the resulting closed value. Runtime Machina calls the same deterministic resolver for concrete destination extents.

No general evaluator ships in Aurelian. Compile-time evaluation resolves the asset program; runtime allocation resolves only the width-dependent finite spans.

## 5. Generic payload practicality

The Copeland language has closed generics and monomorphization, but adding a new generic language intrinsic would require a separate language milestone. The runtime kit is fully generic now: `SpanAllocationRequest<T>`, `SpanPlacement<T>`, and `SpanAllocationResult<T>` retain strongly typed payloads. Tests use both `string` and a nominal payload record. There is no `object`/`unknown` escape hatch.

## 6. Output shape

The result is an ordinary immutable record containing extent totals, status, diagnostics, and an ordered list of placement records. That is more truthful than returning `Span<T>`: the result carries offsets, lengths, result state, and diagnostics in addition to payloads.

Within `*.obj.ts`, tabular region data uses Copeland's columnar `record table`, not a JavaScript-style array of row objects. Ordered edge programs remain arrays because sequence is their semantic meaning.

## 7. Failure model

Invalid inputs reject with `COPE-SPAN-ALLOC-0001` through `0007` and no placements. Minimum demand above extent returns `Underflow`, diagnostic `COPE-SPAN-ALLOC-0100`, and deterministic request-order clipping. Lengths never become negative and offsets never overlap. Surplus with flex is distributed by integer weight with stable request-order remainder ties; surplus without flex remains explicit `SurplusUnused`.

## 8. Machinery reused unchanged

- Ordinary Copeland records, functions, arrays, strings, enums-as-validated strings, and static evaluation.
- Columnar `record table` binding and constant evaluation for region catalogs.
- Existing diagnostics with stable IDs, source path, position, and length.
- The explicit manifest TSX binder and its restricted structural-expression rules.
- Existing SpriteForge TOML loading and atlas bounds validation.
- Existing Machina presentation primitives and Aurelian ordered-quad renderer.
- Existing half-texel inset, native color, viewport, input, and M14 seam paths.

## 9. Separate future language milestone

Native allocator syntax, a generic `layout` domain abstraction, Copeland-authored generic resolver execution, richer enum/union asset decoding, generic table joins, alignment, optional/priority requests, maximum/preferred/intrinsic sizing, and allocation-specific MIR would each require broader compiler work. None is disguised as M15 support.

## 10. M15 MVP

The implemented MVP is a finite non-negative integer extent plus ordered `Fixed(length)` and `Flex(minimum, integer weight)` requests. It returns typed placements and the explicit statuses `Exact`, `SurplusDistributed`, `SurplusUnused`, `Underflow`, or `Rejected`.

Allocation is independent from sprite realization. Machina maps the resolved destination span to `Stretch`, `Tile`, or `Crop`. A 3-slice edge is the high-level prebuilt consisting of fixed endpoints around one flexible edge segment; 9-slice is four such edge programs plus fixed corners and a bounded center policy. The old nine-slice API now delegates to this allocator-backed lowering rather than maintaining a second engine.

Alignment and optional/priority removal are deferred because SUNKILL does not need them and current `layout` makes neither free.
