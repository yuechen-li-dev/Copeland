# Sprite Card structural editing

This is the practical M17 contract for constructing bounded graphical edge programs while Copeland source remains authoritative.

## Supported operations

`SpriteCardStructuralEditIntent` supports `InsertBefore`, `InsertAfter`, `Remove`, `MoveBefore`, and `MoveAfter`. Reorder is deliberately one adjacent swap per intent. Inserted segments reference an existing region and use explicit fixed/flex, minimum, weight, and sampling values. The new local ID becomes `parentPath.Child(localId)` and must be readable, deterministic, and collision-free.

For SUNKILL, use parent `panel.dialogue.top` or `panel.dialogue.bottom` with enclosing identity `horizontalEdge`; use `panel.dialogue.left` or `.right` with `verticalEdge`. Those functions are shared templates, so a structural change fans out to both corresponding edge instances. `ConceptsAdded` and `ConceptsRemoved` expose that fanout.

For example, insertion uses `InsertBefore`, parent `panel.dialogue.top`, anchor `panel.dialogue.top.center`, and `new SpriteCardNewSegment("clamp-decorative", "dialogue.top.clamp", Fixed, 7, 0, "crop")`. After every successful operation, use `result.RefreshedProjection` to create the next intent; reusing the previous projection correctly triggers stale-source rejection.

## Rewrite and validation sequence

1. Compare both intent and projection SHA-256 with the current source.
2. Resolve exactly one named edge function and exactly one `segments` array.
3. Parse only that array's top-level calls with a string/comment-aware scanner.
4. Apply the semantic operation to identified segment IDs.
5. Compile the complete candidate with `ObjectAssetCompiler`.
6. Validate unique paths, retained caps, minimum segment count, target lifecycle, and requested adjacent order.
7. Stage the `.obj.ts` and all four derived projections, replace them as one rollback-protected transaction, and rebuild Sprite Cards with the real allocator.

No generated TOML is read as authoring input. A failed step leaves the source and runtime projections unchanged.

## Source preservation

Insertion derives indentation and newline convention from the anchor. Move transports the complete segment item, including directly leading comments. Remove deletes the expression and comma but keeps leading comments. The enclosing function, tables, other edge helper, and call sites are not formatted or regenerated. The SUNKILL insert/move/move/remove proof returns to the exact original source bytes.

This is a bounded source-span strategy, not a syntax-tree editor or formatter. Unsupported expressions, duplicate-looking constructs, a missing template, or multiple `segments` lists fail with `OBLIVION-SPRITE-CARD-AMBIGUOUS-SOURCE` or a structural diagnostic.

## Safety and failure modes

- Stale projection or intent: `OBLIVION-SPRITE-CARD-STALE-SOURCE`; refresh and retry.
- Wrong parent edge: `OBLIVION-SPRITE-CARD-STRUCTURAL-TARGET`.
- Wrong enclosing identity: `OBLIVION-SPRITE-CARD-ENCLOSING-MISMATCH`.
- Missing/multiple source targets: `OBLIVION-SPRITE-CARD-AMBIGUOUS-SOURCE`.
- Invalid ID, region, policy, cap removal, or non-adjacent move: `OBLIVION-SPRITE-CARD-STRUCTURAL-INVALID`.
- Compiler failure: candidate diagnostics are returned as `compile-failed`; no replace occurs.
- Semantic mismatch after compile: `OBLIVION-SPRITE-CARD-SEMANTIC-VALIDATION`.

The result includes before/after source spans and snippets, both hashes, added/removed/moved paths, diagnostics, compile status, timings, and the refreshed projection. Successful results are retained as bounded in-memory semantic history; this is evidence for future undo, not an undo system.

## Unsupported operations

M17 does not edit `manifest.tsx`, region catalog rows, raw crop coordinates, guides/datums, arbitrary Copeland arrays or statements, arbitrary graph topology, or animation timelines. It does not offer drag-and-drop, general diff/merge, persistent history, or a general AST rewrite API.
