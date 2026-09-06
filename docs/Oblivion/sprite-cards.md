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

## Current edit boundary

M16 supports flex weight, minimum length, sampling mode, and compatible source region selection for explicit flex segment calls. It does not support arbitrary source formatting, segment reorder, painting, animation timelines, layers, shader graphs, or a general node graph. Animation frames, stackframes, and atlas subgrids remain compatible with future Region/Card adapters because SpriteForge retains ownership.

