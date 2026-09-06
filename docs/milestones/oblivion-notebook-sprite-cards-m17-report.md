# OBLIVION-NOTEBOOK-SPRITE-CARDS-STRUCTURAL-EDITING-M17 report

## 1. Outcome

Outcome A — Sprite Cards support bounded visual construction of authored programmable-panel edge programs. Insert, remove, and adjacent reorder operate on Copeland source, compile before replacement, refresh compiler IR/cards/allocator/runtime outputs, reject stale or ambiguous edits, and preserve Concept Path identity under moves. The SUNKILL insert/move/move/remove sequence returns to byte-identical source and semantic output.

## 2. M16 baseline

| Existing edit kind | Source strategy | Stable? | Structural implications |
| --- | --- | --- | --- |
| Flex weight | replace the explicit edge-call integer argument | yes; segment ID/path unchanged | remains M16 code |
| Minimum length | replace the explicit edge-call integer argument | yes | allocator recompiles and refreshes |
| Sampling | replace the explicit edge-call string argument | yes | runtime projection regenerates |
| Source region | replace the explicit compatible region argument | yes | region card/path remains independent |

M16's hash check, balanced call-argument locator, candidate compilation, atomic replacement, output emission, allocator projection, guide/datum/blockout erasure, and UI projection were retained rather than redesigned.

## 3. Structural edit model

`SpriteCardStructuralEditIntent` carries kind, parent path, target, anchor, optional typed new segment, expected SHA-256, and expected enclosing construct. `SpriteCardStructuralEditResult` carries status, paths, before/after spans and snippets, hashes, concept deltas, diagnostics, compile state, timings, and refreshed projection. The service records successful results as bounded in-memory evidence.

## 4. Supported edit intents

The public enum provides `InsertBefore`, `InsertAfter`, `Remove`, `MoveBefore`, and `MoveAfter`. `SpriteCardNewSegment` makes local ID, existing region, fixed/flex allocation, minimum, weight, and sampling explicit. Raw patches are private implementation detail.

## 5. Source representation audit

Regions and authoring concepts are columnar `record table` declarations. Ordered edge programs are `segments: AssetEdgeSegment[]` array literals returned by `horizontalEdge` and `verticalEdge`. Panel construction calls those helpers. SUNKILL shares one horizontal template across top/bottom and one vertical template across left/right.

## 6. Chosen source rewrite strategy

M17 uses a bounded comment/string-aware source-span rewrite of the uniquely named helper's single `segments` array. It parses only top-level call items and preserves the enclosing declaration. This is less invasive than compiler-wide syntax-tree rewriting or whole-function regeneration and fits the actual source shape.

## 7. Comment/trivia preservation behavior

Insertion derives newline and indentation from the anchor. Move carries the complete source item, including directly leading comments. Remove retains leading comments rather than silently deleting them. Unrelated region-table bytes remain unchanged. A removed commented item can leave whitespace; no formatter is run.

## 8. Concept identity law

Identity remains the authored segment ID lowered through `SegmentPath`, not list position. Move compares the pre/post concept sets and reports one moved path with no add/remove churn. Insert must introduce a new path; remove must eliminate its target.

## 9. Concept Path insertion law

The new path is `parentPath.Child(LocalId)`. `GraphicalConceptPath` validates readability and bounds, the source program rejects duplicate local IDs, and post-compile validation rejects duplicate panel paths. SUNKILL uses `panel.dialogue.top.clamp-decorative` (and reports the shared-template bottom counterpart).

## 10. Order versus identity

`MoveAfter(panel.dialogue.top.clamp-decorative, panel.dialogue.top.center)` swaps adjacent authored items. A second move across `clamp-c` retains `panel.dialogue.top.clamp-decorative`. Non-adjacent requests reject; no delete/recreate implementation exists.

## 11. Insert workflow

The proof selects existing region `dialogue.top.clamp`, fixed allocation, intrinsic authored length 7, crop sampling, and inserts before center. Compilation produces a new card/runtime segment and increases top minimum demand from 216 to 223. The shared horizontal template also introduces the corresponding bottom path, reported in `ConceptsAdded`.

## 12. Reorder workflow

Two `MoveAfter` intents cross center and then clamp-c. Each rewrites the ordered source list, compiles, validates adjacency and identity, emits runtime outputs, and rebuilds the strip. Allocation quantities remain governed by the same allocator while visual order changes.

## 13. Remove workflow

Removal is allowed only for a non-cap entry when at least three entries remain. The proof removes the decorative clamp, reports both shared-template removed paths, releases seven pixels of minimum demand, rebuilds runtime/cards, and restores the exact original source bytes.

## 14. Compile-before-replace validation

The service resolves source, transforms an in-memory candidate, runs `ObjectAssetCompiler`, validates unique paths, retained caps, minimum count, target lifecycle, and requested order, then stages source plus object/runtime/JSON/audit projections. Originals are backed up and restored if any replacement fails, so source and derived outputs advance together.

## 15. Stale-source handling

Both projection SHA-256 and intent SHA-256 must equal the current file hash. A mismatch returns `OBLIVION-SPRITE-CARD-STALE-SOURCE`, performs no compile or write, and requires refresh.

## 16. Ambiguity handling

The locator requires exactly one named helper definition and one `segments` list within it. Multiple matching constructs return `OBLIVION-SPRITE-CARD-AMBIGUOUS-SOURCE` with the count and no write. Strings and comments are masked before matching, so decoys do not become targets.

## 17. Edit result and evidence model

Results expose applied/status, edit kind, target/anchor, enclosing before/after span, source snippets, hashes, added/removed/moved paths, diagnostics, compile outcome, transformation/compile/card refresh timings, and the refreshed projection. `StructuralHistory` retains successful semantic entries only.

## 18. Source diff projection

The required `source-diff-insert.txt`, `source-diff-reorder.txt`, and `source-diff-remove.txt` show the bounded before/after construct. This intentionally stops short of a merge editor.

## 19. Card UI controls

Edge cards project explicit `+ Before`, `+ After`, `←`, `→`, and `Remove` labels according to legal capabilities. Caps omit remove and outward movement. Successful edit screenshots show the complete refreshed ordered strip rather than notebook-local reorder state.

## 20. Region picker and defaults

M17 implements the typed insertion boundary, not a universal picker. The caller supplies an existing region visible through Region Cards. Proof defaults are fixed length 7 and crop sampling, immediately visible through the new card's authored state. Raw crop creation is deferred.

## 21. Allocator recomputation

All projection placement uses `Copeland.SpanAllocation.SpanAllocator.Resolve`. The fixed insertion raises horizontal minimum demand by seven and redistributes flexible lengths; removal returns the original summary. Reorder performs no notebook-local allocation calculation.

## 22. Guide/datum dependency handling

Current guide/datum/blockout relationships attach to the panel, not individual edge-segment paths, so moves and removal do not create dangling references. M17 does not invent dependency vocabulary. Future segment-targeted relations must add explicit rejection or preservation before becoming editable.

## 23. Optional authoring-scaffold edit

Deferred. Adding a datum/guide would require safe columnar record-table row mutation and is not evidence needed for the ordered-edge capability.

## 24. Fresh insertion proof

A context-free agent located `SpriteCardStructuralEditIntent`, selected the existing clamp region, described the exact insert call, and independently passed the focused M17 suite 5/5. It recommended Outcome A.

## 25. Fresh reorder proof

The review identified the exact two `MoveAfter` operations and correctly required chaining each result's `RefreshedProjection`. It verified that the stable path, rather than an ordinal, is the target.

## 26. Fresh removal proof

The review identified the non-cap removal contract, stale/ambiguity failures, and the byte/semantic roundtrip evidence without prior task context.

## 27. Comment preservation proof

The fixture adds line comments and a blank line around clamp-b/center, performs insertion, and asserts both comments survive and the region-table prefix is byte-identical. `source-preservation-proof.json` records the policy and limitation.

## 28. Stale edit proof

`stale-edit-proof.json` records an external append after projection, rejected insertion, the stale diagnostic, and unchanged post-attempt bytes.

## 29. Ambiguity proof

`ambiguity-proof.json` uses two `segments` properties in the bounded construct. The locator reports two locations before candidate mutation and the source remains unchanged.

## 30. Concept Path stability proof

`concept-path-stability.json` records the same moved path across both swaps, no add/remove churn during moves, stable repeated compilation, and explicit shared-template fanout.

## 31. SUNKILL structural dogfood

The real SUNKILL `.obj.ts` was edited through `OblivionSpriteCardService`, derived runtime TOML was regenerated, and the native M15 renderer produced `native-sunkill-structural-edit.png`. A protected runner backed up and restored authoritative source, generated projections, and pre-existing M15 evidence after capture.

## 32. Semantic roundtrip proof

`semantic-roundtrip-proof.json` records equal compiler-emitted semantic hashes before/after. The improved removal policy also makes the proof source SHA-256 identical before/after.

## 33. Runtime seam/color/resize regressions

Native dogfood runs the real M15 suite against the ten-segment program. The captured `native-runtime-proof.json` includes the passing output and seam metrics; the fixed proof card renderer now derives card width from segment count and cycles its display palette instead of assuming exactly nine entries.

Final validation passed: Oblivion 250/250 (including focused M17 7/7), Copeland TS 1,285/1,285, Machina Presentation 43/43, SUNKILL 27/27, SpriteForge 26/26 across net8/net10, and the full Aurelian solution 779/779. `Copeland.slnx` and `Oblivion.slnx` build with zero warnings/errors. Scoped `dotnet format --verify-no-changes`, `git diff --check`, the native M15 ten-segment proof, and the hardened SUNKILL window/first-frame smoke all pass.

## 34. Performance sanity

`performance.json` records source transformation, Copeland compile, and card refresh per edit plus total sequence time; SVG timing sidecars record view refresh. All bounded edits remain millisecond-scale on this machine.

## 35. Compiler/owner-lane fixes

No compiler change was required. Structural dogfood exposed a narrow M15 proof-renderer assumption: two palette reads and fixed card width assumed nine segments. The renderer now supports the actual bounded segment count. Runtime panel lowering itself already supported the insertion.

## 36. Deferred systems

Manifest editing, record-table structural editing, new atlas crops, guide/datum insertion, duplicate convenience, arbitrary Copeland structure, arbitrary graph editing, drag-and-drop, persistent history, general undo/redo, animations, and a general formatter/AST editor remain deferred.

## 37. Exact M18 recommendation

Choose animation/frame-sequence Sprite Cards as the single M18 candidate. M17's bounded edge construction is qualified; frame ordering now offers a second ordered-program pressure without expanding into manifest or arbitrary graph editing.

## 38. Diff stat

Final inventory is 6 tracked files changed (234 insertions, 30 deletions) plus 32 new files, including 27 M17 evidence files. The implementation is confined to Oblivion Model/App/UI/Standalone, focused Oblivion tests/docs/artifacts, and the narrow SUNKILL M15 proof-renderer fix.
