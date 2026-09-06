# Oblivion Sprite Cards

Sprite Cards are semantic visual projections of programmable graphical assets. They are not miniature image editors, persisted asset records, generated TOML editors, or renderer-state inspectors.

Each card exposes a concept path, kind/role, authored source location, atlas source rectangle where applicable, authored policy, resolved placement, lowering status, relationships, diagnostics, and supported edits. Edge programs use an ordered strip with exact extent, minimum demand, used/unused length, deficit, status, boundaries, labels, and selected highlight. The view remains legible with crop previews hidden.

## Authority and refresh loop

The authority chain is:

`*.obj.ts -> ObjectAssetCompiler -> semantic asset IR -> SpriteForge/Machina runtime projection`

Cards consume semantic IR plus the real `SpanAllocator` result. A supported edit produces a typed intent containing concept path, property, source span, before/after values, and expected source hash. Oblivion creates and compiles a candidate before changing the file. On success it writes source, regenerates object/runtime projections, and rebuilds cards. It never patches generated TOML.

External source edits invalidate existing cards. A mismatched SHA-256 produces `OBLIVION-SPRITE-CARD-STALE-SOURCE`; the edit is rejected and the user or LLM must refresh/recompile.

## Diagnostics

Compiler and allocation diagnostics retain stable codes and severity. Relevant failures include missing/invalid regions, invalid or duplicate concept paths, impossible allocation minima/deficit, unsupported edits, failed recompilation, and stale projections. Filters can focus one path and its ancestry, one concept kind, or only cards carrying diagnostics.

## Guides, datums, and blockouts

SUNKILL authors these as `record table AssetConcepts`. They are semantic authoring geometry, appear as cards/overlays, and are deliberately absent from SpriteForge runtime TOML. This recovers the useful MachinaCanvas behavior without restoring TOML sidecars as authority.

## Structural editing

M17 adds a bounded semantic API for ordered programmable-panel edge programs:

- insert a fixed or flexible segment before or after an existing segment;
- move a segment across exactly one adjacent segment;
- remove a non-cap segment while retaining at least three segments.

Each intent carries the parent edge Concept Path, target and/or anchor Concept Path, expected source hash, and expected enclosing construct identity. A new segment supplies a readable collision-checked local ID, an existing compatible region, allocation policy, minimum, weight, and sampling policy. The card surface projects explicit `+ Before`, `+ After`, `←`, `→`, and `Remove` affordances. It does not treat ordinal position as identity.

The current SUNKILL source authors top/bottom through one `horizontalEdge` template and left/right through one `verticalEdge` template. Editing either shared ordered program intentionally changes both instances, and the edit result reports every added or removed path. The service rejects a missing or multiply located template rather than selecting the first text match.

Structural transformation is comment/string aware and rewrites only the selected `segments: [...]` contents. Inserted source follows the enclosing indentation and newline style. Moves carry leading comments with their segment. Removal retains leading comments, which can leave harmless whitespace. Unrelated record-table rows and surrounding functions remain byte-identical. The candidate is compiled and its Concept Paths, caps, segment minimum, requested order, and target lifecycle are validated before atomic replacement.

## Current edit boundary

M16 parameter edits remain unchanged: flex weight, minimum length, sampling mode, and compatible source region selection use explicit call arguments. M17 structure edits are limited to the existing `horizontalEdge` and `verticalEdge` ordered lists. They do not edit manifests, create atlas crops, manipulate arbitrary arrays/statements/object graphs, provide drag-and-drop, or implement general undo/redo. Animation frames, stackframes, and atlas subgrids remain compatible with future Region/Card adapters because SpriteForge retains ownership.
