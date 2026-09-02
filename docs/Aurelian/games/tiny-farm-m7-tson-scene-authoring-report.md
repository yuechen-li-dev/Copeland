# TinyFarm M7: TSON scene authoring

## Outcome

**Outcome A — TSON cleanly becomes scene-content authority.** Production scene content now comes only from five human-readable Object TypeScript record tables. `TinyFarmDefinitionLoader` validates those nominal table roots, converts their cells into the existing typed scene records, and then invokes the existing TinyFarm semantic validator. Runtime reducers, DotRecast, persistence, projections, MonoGame, and LLM control consume `TinyFarmSceneCatalog`/`SceneDefinition`; none consume raw TSON.

The canonical content hash is `fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa`. M1 remains `dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333`; M2 remains `4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3`.

## Legacy inventory and source organization

The removed C# authority was `TinyFarmScenes.CreateAndValidate` plus five scene factories and `Object`, `Layout`, `Anchor`, and `Route` helpers. It authored 5 `SceneDefinition` values, 19 objects, 19 layout rows, 14 anchors, and 8 routes. The authored factory region was 216 lines / 9,208 UTF-8 bytes.

M7 uses five files because the production TSON reader requires one exact nominal table root per self-described document. Files are deterministically loaded by an explicit ordinal filename list; filesystem enumeration order is never consulted.

| Former C# structure | Production TSON authority |
| --- | --- |
| `CreateOverworld/Farm/Town/Store/Riverside` | `tiny-farm-scenes.obj.ts` / `Scenes` |
| `Object(...)` rows | `tiny-farm-scene-objects.obj.ts` / `SceneObjects` |
| `Layout(...)` rows | `tiny-farm-scene-layout.obj.ts` / `SceneLayout` |
| `Anchor(...)` rows | `tiny-farm-scene-anchors.obj.ts` / `SceneAnchors` |
| `Route(...)` rows | `tiny-farm-scene-routes.obj.ts` / `SceneRoutes` |

The five authored files total 7,484 UTF-8 bytes. They are grouped by scene for readability, while semantics remain independent of row order.

## Schemas and laws

| Table | Columns |
| --- | --- |
| `Scenes` | `id:string`, `label:string`, `width:number`, `height:number` |
| `SceneObjects` | `sceneId:string`, `objectId:string`, `kind:string`, `label:string`, `blocksMovement:boolean`, `semanticReference:OptionalText` |
| `SceneLayout` | `sceneId:string`, `objectId:string`, `x:number`, `y:number`, `width:number`, `height:number`, `layer:number` |
| `SceneAnchors` | `anchorId:string`, `sceneId:string`, `x:number`, `y:number`, `kind:string`, `semanticLocation:OptionalText`, `semanticObject:OptionalText`, `facing:OptionalText`, `arrivalRadiusUnits:number` |
| `SceneRoutes` | `routeId:string`, `sourceScene:string`, `triggerObject:string`, `targetScene:string`, `targetAnchor:string`, `interactionLabel:string` |

- Textual IDs are converted to `SceneId`, `SceneObjectId`, `SceneAnchorId`, and `SceneRouteId`. Row indexes never become identity.
- `SceneDefinition` sorts objects, anchors, and routes by ordinal ID and layout by layer then object ID. Source grouping is only an authoring aid.
- Optional cells use the explicit nominal enum `OptionalText.None` / `OptionalText.Some(value)`, not `null`, `undefined`, or an empty-string sentinel.
- Coordinates, extents, layers, and arrival radii must be finite exact 32-bit integers. Anchor tile coordinates become the same tile-centered `ScenePosition` values as M6.
- Object semantic references are checked against the current typed plot/shop identities. Anchor location/object references become `LocationId`/`SceneObjectId` before semantic validation.

## Loader and validation boundary

The loader sequence is:

```text
five .obj.ts sources
-> TsonDocumentReader.ReadSelfDescribed(ObjectTypeScript)
-> exact nominal root/ordered column/type checks
-> explicit enum, optional, integer, and typed-ID conversion
-> SceneDefinition values
-> TinyFarmSceneCatalog
-> TinyFarmScenes.Validate
```

Representation failures and semantic failures both throw `InvalidDataException` before a session exists. The focused fixture loop covers duplicate scene/object/anchor/route IDs, missing layout objects, missing route scenes/anchors, out-of-bounds and blocked anchors, invalid semantic references, unknown enum values, wrong numeric types, missing columns, and wrong roots. Production content and row-reordered content are also loaded directly.

Raw `TsonTable`/`TsonValue` types occur only in `TinyFarmDefinitionLoader`. `TinyFarm.Core` remains dependency-free, headless, and free of graphics/TSON references. The immutable catalog is carried by the existing `TinyFarmDefinitions` composition object, so reducers receive typed semantics rather than querying TSON.

## Provenance, compatibility, and parity

Each source records its authored filename, SHA-256, and byte length. The aggregate SHA-256 hashes the deterministic sequence `filename + NUL + authored UTF-8 bytes`. Provenance is inspection evidence and is not gameplay state. The existing M2 definition identity remains unchanged, so moving scene declarations does not invalidate old M6-format saves; save payloads still contain semantic IDs/state, not the catalog.

The checked proof measured 0.636 ms file read, 10.305 ms TSON parse, and 22.401 ms materialization plus semantic validation on this machine. These are evidence, not a performance contract.

Legacy-vs-TSON canonical comparisons all match:

| Evidence | Hash |
| --- | --- |
| M6 state | `d46e70e37c8775e503c3a7693fc14d952a6932a22be0c13172771e020ae65544` |
| M6 results | `ecb4181792717a393125e85416b148ca2242934d761b025498a45aa24af21a24` |
| M6 events | `4f8e8383683a38da695284fb6fd561d5fc32c12fd7feedeee1841e7a3b7364d7` |
| anchors | `f6dc1f5c8a9116122744e860fcd23267d7784f4c9452fd273ca934b55e79f535` |
| routes | `affb1c95d1745eaab9e9108b282ba5516ea16b1a1282f1a894c741149e8ccf72` |
| navigation | `07dde9ac2f6c957017abe151320ee0a7d5c900f51ecd7901331c9d21a480d8fa` |
| projection | `4c93db713e4da1a8ee47cec7f6a309adc23f19b7acee1d91b80e0c9c3d6b8434` |

This proves identical route graph, anchor catalog, DotRecast navigation evidence, NPC schedule anchor resolution, active/inactive handoff, save/load behavior, and graphical projection. The M7 scenario composes the existing real M4 route/farming journey and M6 semantic-navigation/handoff scenario after a production TSON load.

## TableScript and agent authoring proof

Existing tooling works without changes:

```powershell
dotnet run --project src/Copeland/Copeland.Cli/Copeland.Cli.csproj -- table list src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-scene-anchors.obj.ts --format json
dotnet run --project src/Copeland/Copeland.Cli/Copeland.Cli.csproj -- table query src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-scene-routes.obj.ts SceneRoutes --where 'targetScene == "town"' --select 'routeId, sourceScene, targetAnchor' --format json
dotnet run --project src/Copeland/Copeland.Cli/Copeland.Cli.csproj -- table query src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-scene-objects.obj.ts SceneObjects --where 'sceneId == "farm" && blocksMovement == true' --select 'objectId, kind, label' --format json
dotnet run --project src/Copeland/Copeland.Cli/Copeland.Cli.csproj -- table validate src/TinyFarm/TinyFarm.Runtime/Content/tiny-farm-scenes.obj.ts --format json
```

The bounded agent questions are direct table queries: `general-store.counter` is tile `(5,3)`; routes entering Town are `overworld-town` and `store-town`; blocking Farm objects are `farmhouse` and `fence`; General Store objects are `shelves`, `shop-counter`, and `store-exit`.

## Tests, CI, and artifacts

`TinyFarmM7Tests` adds production loading, a small golden fixture, exact counts and queries, scene-row reorder parity, authored-to-canonical-TSON reload, 14 invalid-content mutations, dependency isolation, and the canonical M7 proof. TinyFarm CI now runs `--m7`, checks every parity flag and exact M1/M2 hashes, and retains the repository artifact-budget gate.

Compact evidence is under `artifacts/tiny-farm-m7`: `proof.json`, `content.json`, `parity.json`, `provenance.json`, and `manifest.json`. There are no screenshots, canonical TSON duplicates, or scene snapshots.

## Recommended M8

The remaining pressure is no longer scene authoring. The current schedule decision still selects anchors through a game-local branch table before Dominatus utility evaluation. Based on the observed branch growth and the explicit role of Dominatus, the exact next milestone should be **TINY-FARM-M8 — Dominatus utility-authored schedule/transition selection**, bounded to replacing `ScheduledAnchor` branching with inspectable utility considerations while preserving the exact anchor sequence, hashes, resolver authority, and TSON scene boundary. It should not add schedules, NPCs, a generic planner, or move transitions into the scene loader.
