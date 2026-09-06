# Graphical concepts

## The model

The common flow is:

`Concept -> Concept Path -> authored constraints -> resolution/lowering -> runtime projection`

Oblivion adds a second projection from the same semantic result:

`compiler IR + allocator result -> Notebook Sprite Cards`

A graphical concept is a renderer-neutral semantic part. A `GraphicalConceptPath` is its stable, readable, compositional identity, such as `panel.dialogue.top.center`, `region.dialogue.top.glow`, or `guide.dialogue.datum.text-baseline`. Paths are case-sensitive dot-separated identifiers, bounded to 24 components and 240 characters. They are not a global ontology.

## Three states

- Authored state records intent: region reference, fixed/flex policy, minimum, weight, sampling, guide geometry, or visibility.
- Resolved state records a concrete span or bounds and the allocator outcome.
- Runtime state records what survives lowering. Regions, panel programs, sampling, and placements survive. Guide, datum, and blockout scaffolding erases.

The distinction is enforced in types (`SpriteCardAuthoredState`, `SpriteCardResolvedState`, and `SpriteCardRuntimeState`). A card displays these facts; it never owns them.

## Ownership

Copeland source (`*.obj.ts`) owns asset meaning and `manifest.tsx` owns composition. `ObjectAssetCompiler` owns semantic decoding and validation. `SpanAllocator` owns all finite edge resolution. SpriteForge owns runtime sprite metadata validation. Machina owns programmable-panel lowering. Oblivion App owns card projection, source edit orchestration, stale detection, and evidence. Oblivion UI owns renderer-neutral SVG presentation.

No notebook allocator exists. The card service calls `SpanAllocator.Resolve` with the same typed requests used by Machina. Generated TOML and PNGs are derived artifacts.

## Concept structures

M16 does not copy Firmament syntax. Ordinary Copeland records/tables and ordinary immutable C# records supply the useful Concept Struct law. Flat region catalogs and authoring scaffolds use columnar `record table`; ordered edge programs remain arrays. The first card kinds are Panel, Region, EdgeSegment, Guide, Datum, and Blockout. Allocation is a summary on an edge strip rather than a seventh giant card kind.

Relationships are deliberately bounded to parent, source-of, resolves-to, attached-to, and constrained-by. M16 uses only relationships supported by actual data and does not create an RDF/query layer.

## Selection and edits

Selection is exact path or path ancestry. Filtering is by concept kind or diagnostics presence. This recovers the useful focus-mode behavior without a query language.

Writable edge cards support four bounded properties: flex weight, minimum length, sampling mode, and source region. The editor locates explicit arguments in a supported edge constructor call, checks the projection's source SHA-256, builds a candidate in memory, recompiles it, and replaces source only after successful compilation. It then emits compiler projections. Failed compilation or a stale hash leaves source unchanged.

The SUNKILL source makes policy arguments explicit per edge so a top-edge edit does not silently edit the bottom edge. General AST rewriting, arbitrary reorder, animation editing, and freeform graph editing remain deferred.

## Lineage

MachinaCanvas contributed visual guide/datum/blockout and focus-overlay pressure. Aetheris contributed the stable semantic/local identity and authoring-erasure laws. M15 supplied the authoritative object IR, region table, explicit edge program, and single allocator. M16 consolidates those laws at existing owners instead of porting historical storage or UI architectures.

