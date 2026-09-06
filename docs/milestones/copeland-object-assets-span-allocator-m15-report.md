# COPELAND-OBJECT-ASSETS-SPAN-ALLOCATOR-M15 report

## 1. Outcome

Outcome A. Programmable asset authoring, TSX asset composition, a sprite-independent generic span allocator, Sprite Cards, SpriteForge/Machina/Aurelian integration, and real SUNKILL native proof all use one authority chain.

## 2. M14 pressure

M14's nine-slice could preserve corners and one edge region, but SUNKILL's authored border is cap → clamp → glow rail → clamp → center rail → clamp → glow rail → clamp → cap. Flattening that structure into one stretch would discard the authored motif.

## 3. Copeland allocator audit

The audit is in `m15-allocator-language-audit.md`. Existing `Span<T>` is a typed contiguous carrier; existing `layout` is compiler-owned spatial structure. Neither should be redefined opportunistically.

## 4. MVP recommendation

Use an ordinary generic runtime kit with finite integer extent, ordered fixed requests, minimum-weighted flexible requests, deterministic placement, result status, and diagnostics. Keep allocation independent from sprite sampling.

## 5. Implemented allocator features

`Copeland.SpanAllocation` supplies typed `SpanAllocationRequest<T>`, `SpanPlacement<T>`, `SpanAllocationResult<T>`, and `SpanAllocator.Resolve`. Exact fit, weighted surplus, unused surplus, underflow clipping, invalid rejection, stable remainder order, and inspectable totals are implemented.

## 6. Rejected and deferred brainstorm features

Intrinsic/preferred/maximum sizing, alignment, optional/priority requests, fractional extents, holes, relocation, pinning, and ownership were not needed by SUNKILL. A generic `layout` syntax or intrinsic was also rejected for M15.

## 7. Final allocator ownership

The owner is `src/Copeland/Copeland.SpanAllocation`. It references no sprite, texture, PNG, UI, Machina, Aurelian, or SpriteForge type.

## 8. Relationship to `Span<T>`

Both preserve ordered typed elements, but allocator output is a result record with placement metadata and diagnostics rather than a language `Span<T>`. Generic payload is preserved without `unknown`.

## 9. Relationship to `layout` and `layout type`

There is no syntax or semantic change. Current layout continues to own immutable 2D nodes, origins, boxes, layers, streams, binding, and contracts. Any domain-general layout thesis waits for a dedicated language milestone.

## 10. Generic payload proof

Tests resolve strings and a nominal payload record with the same implementation. Machina attaches a typed edge-segment payload. The project dependency graph proves the core has no sprite dependency.

## 11. Failure and underflow

Invalid requests return `Rejected` with `COPE-SPAN-ALLOC-0001`–`0007` and no placements. Underflow returns `COPE-SPAN-ALLOC-0100`, preserves request order, clips to the finite extent, and emits no negative or overlapping spans. SUNKILL's 220px proof reports a top-edge deficit of 58 and still renders.

## 12. Allocator visualization

The artifact set contains exact, underflow, and surplus SVG strips plus JSON totals. Sprite Cards add region preview, ID, source rect, allocation, sampling, offset, length, and diagnostics.

## 13. Memory return path

`docs/research/span-allocator-return-path.md` classifies what carries forward. Alignment is the exact M16 candidate; holes/fragmentation can follow. Pinning, relocation, lifetimes, and borrowing require real systems authority first.

## 14. `*.obj.ts` authoring

The explicit asset compiler uses ordinary Copeland binding and static evaluation. Exactly one `const $asset = static ...` becomes bounded semantic asset IR. Filename alone does not steal the existing `.obj.ts` TSON meaning.

## 15. Semantic asset IR

IR contains texture, regions, panels, corners, four ordered edge programs, center policy, padding, and scale. Per segment, allocation and sampling are separate. The region catalog is correctly authored as columnar `record table AssetRegions`; ordered edge programs remain arrays.

## 16. TOML projection

Deterministic semantic TOML contains resolved regions and panel programs, not AST. `*.obj.toml` identifies `generated-obj-ts`; `*.runtime.toml` identifies `runtime-toml`; both say to edit the Copeland source.

## 17. Legacy TOML compatibility

SpriteForge retains all legacy loading. Files without a source classification remain `LegacyAuthoredToml`. The generated projection includes a nine-slice compatibility view, while the production SUNKILL path consumes the programmable panel.

## 18. `manifest.tsx` syntax and model

The established restricted manifest binder now accepts nested `<Assets>`, `<Texture>`, `<Object dependsOn={...}>`, and `<AssetOutputs>` nodes. It is intentionally not an extensible XML/build DSL.

## 19. Manifest IR

`ManifestAssetGraph` owns source root, typed texture registrations, object registrations, and dependencies. `ManifestAssetOutputs` owns requested projections. Build consumers use this immutable IR, not raw TSX.

## 20. Manifest projections

`ObjectAssetManifestProjection` emits deterministic generated JSON with sorted entries and an explicit do-not-edit notice. The CLI emits TOML, JSON, runtime, audit, and the manifest file list.

## 21. SpriteForge integration

SpriteForge now validates region bounds, stable references, allocation and sampling modes, center policy, corners, padding, scale, and edge presence. It consumes generated or legacy TOML and exposes immutable programmable panel metadata.

## 22. Programmable panel model

Machina owns renderer-neutral corners, edge programs, center policy, tint, source/destination rectangles, resolved edge summaries, quads, and diagnostics. Aurelian only maps those quads to the existing ordered native path with half-texel inset UVs.

Nine-slice is now a prebuilt over explicit edge allocation. Each side is the 3-slice case with one flexible middle segment; the compatibility `MachinaNineSliceLowerer` delegates to the programmable lowering. There is one edge layout engine.

## 23. Sprite Card / MachinaCanvas proof

MachinaCanvas gained a bounded `spriteCards` module, immutable projection model, and pure `SpriteCardStrip`. It joins compiler/allocator-owned placements to asset metadata, rejects gaps/overlaps, and remains read-only. It does not add a scene-object kind, AST editor, pixel painter, or central `App.tsx` switch. PNG evidence is generated from the same SUNKILL semantic runtime state.

## 24. SUNKILL migration

`sunkill-dialogue-panel.obj.ts` is authoritative. It declares 25 atlas regions in a Copeland columnar record table and uses ordinary functions/static evaluation to build four nine-segment edges. `manifest.tsx` registers its texture and object. Runtime starts from generated metadata, not the legacy M14 TOML.

## 25. Narrow, nominal, wide, and odd proof

Native screenshots cover a 220px underflow panel, 800px nominal panel, 1200px wide panel, and 800px logical panel on a 1537×864 framebuffer. Mathematical checks require contiguous non-negative placement coverage. Visual inspection preserves caps, clamps, glow rails, central rail, corners, and center.

## 26. Underflow and surplus

Underflow clips in request order and reports deficit; it never silently claims minimum satisfaction. At 800px, top flex lengths resolve to 161, 291, and 160 around stable 42px caps and 7px clamps. The central weight-2 rail receives twice the surplus share subject to deterministic integer remainder.

## 27. Seam and color parity

The M14 periodic high-contrast seam fixture now runs through `MachinaPanelPrebuilt.NineSlice` and explicit edge allocation. Native R8G8B8A8 readback reports maximum and mean channel seam error 0 and boundary color error 0; half-texel inset and clamp behavior are unchanged.

## 28. Fresh allocator edit proof

Recorded after the fresh-context audit in `artifacts/copeland-object-assets-m15/fresh-context-proof.json`.

## 29. Fresh asset authoring proof

Recorded after the fresh-context audit in `artifacts/copeland-object-assets-m15/fresh-context-proof.json`.

## 30. Fresh projection proof

Recorded after the fresh-context audit in `artifacts/copeland-object-assets-m15/fresh-context-proof.json`.

## 31. Owner-lane fixes

- User correction: region rows were replaced by Copeland's columnar `record table`.
- Nine-slice was demoted from a second lowerer to an allocator-backed prebuilt; 3-slice is the one-edge form.
- Runtime and inspection TOML classifications are distinct.
- Generated manifest lists itself and carries a warning.
- `Crop` now computes the visible source length using border scale instead of behaving like stretch.
- Production SUNKILL rendering uses programmable panels; M14 proof can still select the compatibility façade.

## 32. Validation totals

The final sweep passed 1,972 test executions: 1,285 Copeland TS, 78 Copeland CLI, 43 Machina Presentation, 27 SUNKILL integration, 26 SpriteForge across net8/net10, and 513 MachinaCanvas. `Copeland.slnx` and the SUNKILL sample both built with zero warnings and zero errors. Changed C# files pass scoped `dotnet format --verify-no-changes`; MachinaCanvas passes format across 194 files, lint across 190 files, and its production build. The repository-wide Copeland formatter still identifies unrelated pre-existing whitespace outside the M15 change set.

Both M15 and M14 proof executables report Outcome A with seam error 0; the M14 path also reports zero resize texture reuploads. The native SUNKILL smoke opened a window, rendered a frame, exited cleanly, and left no new reusable MSBuild process.

## 33. Deferred systems

No memory allocator, free list, buddy/slab/arena allocator, compaction, pinning, borrow/lifetime model, general 2D allocator, graph panel authoring, generic visual programming framework, AST editor, hot reload architecture, dynamic plugin system, world tilemap, raster painter, theme engine, programmable corners, or animated edges was added.

## 34. Exact M16 recommendation

Choose one bounded allocator simulation milestone: add alignment to the sprite-independent request/result law, visualize padding waste in Sprite Cards, and prove it with non-sprite payload tests. Do not add holes, relocation, or ownership in the same milestone.

## 35. Diff stat

The final implementation spans 80 files across the three authorized repositories: Copeland has 26 modified and 45 new files, including 14 binary proof artifacts; Dominatus has 2 modified and 1 new file; MachinaCanvas has 2 modified and 4 new files. The textual diff is approximately 6,803 insertions and 138 deletions after including new untracked source, documentation, and evidence files.
