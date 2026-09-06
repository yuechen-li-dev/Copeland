# M16 graphical-concept synthesis audit

## Finding

The recurring law is smaller than any source system: a readable semantic identity names an authored graphical concept; compilation resolves that concept into geometry; runtime metadata retains only facts needed to draw; inspection projects every stage without becoming authority. M16 therefore retains M15's asset IR and allocator, adopts Aetheris's stable semantic/local identity and erasure laws, and recovers MachinaCanvas guides and overlays without its TOML sidecar ownership.

| Historical concept | Origin | What problem it solved | Still useful? | New owner/model |
| --- | --- | --- | --- | --- |
| Region ID | SpriteForge | Named atlas crops instead of pixel coordinates at call sites | Yes | `ObjectAssetRegion` and `region.*` card |
| Frame/animation ID | SpriteForge | Stable animation metadata | Compatibility only in M16 | Future card adapter; SpriteForge remains owner |
| UI panel metadata | SpriteForge | Nine-slice runtime validation | Yes, superseded form | programmable panel IR plus compatibility projection |
| Sprite Card strip | MachinaCanvas M15 | Join resolved placements to region facts | Yes | Oblivion typed card projection and SVG view |
| Sprite sidecar | MachinaCanvas | Attach frames to imported images | No as authority | `*.obj.ts` plus compiler-owned asset IR |
| Guide sidecar | MachinaCanvas | Inspect reference regions, grids, datums, dimensions | Semantics yes, sidecar no | `AssetConcepts` compile-time table |
| Blockout sidecar | MachinaCanvas | Coarse spatial decomposition | Yes, bounded | authoring-only `Blockout` concept |
| Datum | MachinaCanvas | Stable alignment reference | Yes | authoring-only `Datum` concept/path |
| Alignment mark | MachinaCanvas | Relate geometry across objects | Deferred | add only with a real second attachment consumer |
| Focus/audit overlays | MachinaCanvas | Reduce visual noise and expose errors | Yes | exact-path focus, kind filter, diagnostics-only view |
| Sketch overlay | MachinaCanvas | Compare artwork and structure | Tooling only | optional external reference; no asset authority |
| Workflow API | MachinaCanvas | Coordinate editor commands | Too broad for M16 | App-owned explicit edit intents only |
| Concept Struct | Aetheris/Firmament | Typed, non-materialized semantic construction data | Law yes, API no | ordinary Copeland records/tables decoded to C# records |
| Concept Path | Aetheris/Firmament | Stable readable identity across lowering | Adapt | `GraphicalConceptPath` |
| Named feature selectors | Aetheris/Firmament | Avoid unstable B-rep identities | Yes | exact concept path plus bounded kind/diagnostic filters |
| Semantic-local geometry | Aetheris/Firmament | Keep feature meaning distinct from kernel topology | Yes | authored policy distinct from resolved span/rect |
| Authoring scaffold erasure | Aetheris/Firmament | Remove construction aids from product output | Yes | guide/datum/blockout omitted from runtime TOML |
| Profile `Span<T>` owner/stale law | Copeland Profile | Reject selectors against replaced geometry | Adapt | source SHA-256 rejects stale card edits |
| Columnar `record table` | Copeland | Typed finite catalogs | Yes where flat | regions and authoring concepts; edges stay ordered arrays |
| `layout` / `layout type` | Copeland | Closed 2D structure and contracts | Not an allocator owner | unchanged; no new syntax |
| Fixed/flex allocator | M15 | Deterministic ordered finite span allocation | Yes, unchanged | `Copeland.SpanAllocation.SpanAllocator` |
| Three-slice/nine-slice | M15/Machina | Convenient panel construction | Yes as prebuilt | lowers through the same programmable allocator |
| Generated TOML | SpriteForge/M15 | Runtime compatibility and loading | Projection only | generated from `*.obj.ts`; never card authority |
| Raw integer-only identity | legacy tools | Cheap indexing | No | integers remain geometry; paths name meaning |
| Freeform node graph | generic editors | Arbitrary visual wiring | Rejected | ordered strip, region grid, selected detail |

## Consolidation

`GuideRegion`, datum, and blockout are not three storage architectures. They are three kinds of authoring-only graphical concept with a path, bounded geometry, and an erasure policy. Edge segment and region cards are not persisted notebook objects; they are rebuilt views of compiler IR and the actual allocator result. A selector language is unnecessary: exact path, concept kind, selected ancestry, and diagnostics-only filtering cover the motivating work.

## Cross-project return paths

MachinaCanvas concepts are classified as: recovered (`guides`, `datums`, `blockouts`, focus/audit overlays), superseded (`sidecar attachment`, authoring/runtime TOML dual authority), tooling-only (`reference grids`, sketch overlays, alignment marks), and obsolete for this lane (central workflow command vocabulary). No React code was ported.

Aetheris concepts are classified as: directly reusable laws (stable semantic identity, current-state selection, authoring erasure), adapted for graphics (Concept Path and non-materialized Concept Struct), CAD-specific (B-rep face/edge selectors, construction planes, dimensional units), and unnecessary here (a second language or general constraint solver).

