# OBLIVION-NOTEBOOK-SPRITE-CARDS-M16 report

## 1. Outcome

Outcome A — first real LLM-native visual programming surface. Compiler-authoritative SUNKILL assets now project into readable Sprite Cards, accept four bounded semantic edits with stale protection, rebuild deterministic runtime metadata, and render both notebook SVG/PNG evidence and the native panel. The exact deferred boundary is structural card editing: source-level segment insertion is proven, but arbitrary insertion/reorder is not yet a card control.

## 2. Cross-project archaeology

The audit covered M15 object source/manifest/allocator/runtime artifacts, SpriteForge regions/panels/loaders, MachinaCanvas guide/blockout/sprite sidecars and overlay/focus workflows, Aetheris/Firmament Concept Struct/Path/selector/erasure laws, Copeland Profile spans/selectors, and Oblivion Model/App/UI ownership. See `m16-concept-synthesis-audit.md`.

## 3. Concept lineage

MachinaCanvas supplied practical guide/datum/blockout and focus-overlay pressure; Aetheris supplied stable semantic-local identity and authoring-erasure laws; M15 supplied the current compiler IR and one allocator. `concept-lineage.json` records the decisions.

## 4. Concepts retained

Stable names, regions, ordered edge segments, authored constraints, resolved geometry, source correlation, allocation diagnostics, focus overlays, and authoring erasure remain.

## 5. Concepts merged

Guide, datum, and blockout sidecar families become one `ObjectAssetAuthoringConcept` record family. M15 cards and Machina overlays become one Oblivion card/overlay projection.

## 6. Concepts rejected

TOML authority, raw integer-only identity, React porting, global ontology, general query/RDF, arbitrary node graphs, workflow command frameworks, painting, and animation editing were rejected.

## 7. Unified graphical concept model

The implemented law is `Concept -> Concept Path -> authored state -> resolved state -> runtime projection`, with a Notebook Card as a non-authoritative view. Details are in `docs/architecture/graphical-concepts.md`.

## 8. Concept Path model

`GraphicalConceptPath` validates readable dot-separated composition, deterministic equality, bounded length, ancestry, and source/diagnostic suitability. Duplicate authoring paths fail as `COPE-ASSET-0115`.

## 9. Concept Struct adaptation

No Aetheris API was copied. Ordinary Copeland records/tables lower to immutable C# records. Flat regions and authoring aids use `record table`; hierarchical ordered edges remain arrays.

## 10. Authoring/resolved/runtime distinction

Typed card states separately carry policy, concrete span/bounds, and runtime survival/projection. Datum/guide/blockout cards explicitly report `runtime: erased`.

## 11. Sprite Card model

Cards include concept path, kind/role, source location, atlas crop, authored policy, resolved placement, runtime state, relationships, diagnostics, and edit capabilities. Source hash and compile version live on the projection.

## 12. Card kinds

The six justified kinds are Panel, Region, EdgeSegment, Guide, Datum, and Blockout. Allocation is an edge summary rather than a giant general card.

## 13. Relationship model

The bounded vocabulary is parent, source-of, resolves-to, attached-to, and constrained-by. M16 emits only relationships backed by current IR.

## 14. Edge allocator projection

The App converts compiler segments to typed requests and calls `Copeland.SpanAllocation.SpanAllocator.Resolve`. The strip shows source crops, fixed/flex, minimum, weight, sampling, offset/length, extent, used/unused/deficit, and status. No duplicate math exists.

## 15. Guide/datum/blockout reconciliation

SUNKILL now authors content-safe guide, text-baseline datum, and content blockout rows in `AssetConcepts`. The notebook overlay draws their geometry and selected cards expose their bounds.

## 16. Authoring scaffolding erasure

The compiler retains `AuthoredConcepts` in semantic IR/JSON but the TOML emitter never writes them. `guide-datum-proof.json` and the focused test establish erasure.

## 17. Source-edit strategy

M16 uses bounded source-span replacement for explicit edge constructor arguments. The locator is string/comment-safe for balanced call arguments. A candidate compiles before an atomic same-path replacement; a general formatter/refactoring engine was not introduced.

## 18. Supported edit intents

Flex weight, minimum length, sampling mode, and compatible source region are live. Guide visibility is modeled as a capability but table-cell editing remains deferred; segment reorder is deferred.

## 19. Stale-source protection

Every projection carries source SHA-256. `ApplyEdit` rejects mismatches as `OBLIVION-SPRITE-CARD-STALE-SOURCE` without changing source. The proof uses an external append against a stale projection.

## 20. Compile/repreview loop

Edit intent -> candidate source patch -> Copeland compile -> semantic IR -> object/runtime TOML emission -> rebuilt cards -> refreshed SVG/native preview is proven. Generated TOML is never an edit input.

## 21. OblivionNotebook UI

`OblivionSpriteCardRenderer` is a deterministic renderer-neutral SVG surface with real atlas crops, ordered allocation overlay, cards, source facts, diagnostics, and authoring overlays. PNGs are browser captures of that same SVG.

## 22. Focus/filter UX

The model supports selected-path ancestry focus, concept-kind filtering, diagnostics-only filtering, and hidden crop previews. Selected cards use a visible highlight and unrelated cards dim.

## 23. SUNKILL dogfood

All four nine-segment edges, 25 atlas regions, three authoring scaffolds, and narrow/nominal/wide allocations project from the current SUNKILL asset. Native rendering still uses generated SpriteForge metadata.

## 24. Four required SUNKILL edits

The idempotent proof performs center weight `2 -> 3`, top glow sampling `stretch -> tile`, center minimum `30 -> 44`, and center region `dialogue.top.center -> dialogue.top.glow`; the compatible region is then restored to preserve the visual motif. Each trace records spans/hashes and recompiles. The final intentional dogfood retains weight 3, minimum 44, and tiled glow.

## 25. Fresh authoring proof

A fresh agent independently selected `panel.dialogue.top.center`, `FlexWeight`, and `2 -> 3`, located the source-aware API and the real allocator, and predicted `291 -> 343` at width 800 without touching renderer internals. Its fixture-state warning led to an idempotent proof baseline and test-local baseline normalization.

## 26. Fresh structure proof

A fresh agent located the ordered source insertion for `clamp-decorative`, predicted the new concept paths and exact +7 minimum-demand effect, and identified structural card editing as the bounded future seam. A regression test applies that source edit on a copy, recompiles, observes the new card/runtime projection, and confirms no hand-authored TOML change.

## 27. Fresh guide proof

A fresh agent found the baseline datum and correctly identified that the initial evidence serialized it but did not draw it. M16 then added the spatial authoring overlay and selected-datum PNG. Runtime erasure remained intact.

## 28. Fresh bug proof

The fresh bug audit traced `segment -> SpanAllocationRequest -> SpanAllocator.Resolve -> placement.Length -> card.Resolved.Length`. The weight-edit regression asserts the exact refreshed length, so a divergent preview allocator or stale join fails.

## 29. MachinaCanvas classification

Recovered: guides, datums, blockouts, sprite crops, focus and audit overlays. Superseded: sidecar attachment and authored/runtime TOML duality. Tooling-only: alignment marks, reference grids, sketch overlays. Obsolete here: broad workflow API and freeform scene editing.

## 30. Aetheris classification

Direct laws: stable semantic identity, current-state selection, erasure. Adapted: Concept Path and non-materialized Concept Struct. CAD-specific: B-rep/topology selectors, units, construction planes. Rejected: a second language/constraint system.

## 31. C#/Copeland ownership split

Copeland owns authored records/tables/functions and manifest composition. C# compiler IR owns decoded semantics; Oblivion App owns projection/edit orchestration; UI owns SVG; SpriteForge/Machina/Aurelian retain runtime compilation/lowering/rendering.

## 32. Performance sanity

Final proof measurements: card projection 3.339 ms; candidate recompile/edit 4.661–5.203 ms; allocation visualization 0.096 ms; combined preview refresh 3.434 ms. The raw measurements are recorded in `performance.json`. No tiny edit approaches seconds.

## 33. Test/build totals

1,624 scoped executions pass: Oblivion 243, Copeland TS 1,285, Machina Presentation 43, SUNKILL 27, and SpriteForge 26 across net8/net10. The focused M16 suite passes 7/7. `Copeland.slnx` builds with zero warnings/errors; changed files pass scoped formatting verification.

## 34. Native regression results

M15 programmable-panel proof: Outcome A, 25 regions, nine edge segments, underflow deficit 72, seam maximum error 0. M14 compatibility proof: Outcome A, zero resize texture reuploads, seam maximum error 0. The final native screenshot is `native-sunkill-after.png`.

## 35. Deferred systems

Structural card insertion/reorder, guide table-cell edits, animation/frame editing, persistent card semantic data, undo/redo, arbitrary selectors, Vulkan embedding, node graphs, raster painting, and artist-tool layers remain deferred.

## 36. Exact M17 recommendation

Choose one milestone: source-preserving structural EdgeSegment card edits (insert adjacent segment and reorder adjacent segments) over the existing explicit edge-call seam, with stale protection and compile rollback. Do not combine it with animation or a graph editor.

## 37. Diff stat

The implementation is local to Copeland plus regenerated existing proof outputs. At report time the tracked diff was 28 files, 356 insertions, and 68 deletions before adding the new source/docs/artifact files; the final `git diff --stat` and status are the authoritative review inventory.
