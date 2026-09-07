# AURELIAN-RTS-PEARL-MINING-M18

## 1. Outcome B

Strong mining and a working integrated proof; **procedural vector realization remains proof-only**. Selection, visibility, projection and semantic HUD are reusable library capabilities. The art compiles through canonical Copeland Profile contours and renders in a real native window, but its sample-local flat-fill Skia adapter is not a reusable GPU/atlas realization contract. The visual result has coherent Wilderland-inspired hierarchy and palette, but does not yet match its polish. `wilderlandVisualProofQualified` is therefore false. This is meaningful progression with the next owner seam isolated, not a completed Outcome A.

Evidence lives in `artifacts/aurelian-rts-pearl-mining-m18`. See [corpus matrix](m18-rts-corpus-audit.md), [system research](../research/vibecode-rts-pearl-mine.md), [art research](../research/rts-procedural-art-mine.md), [landed API](../Aurelian/strategy-profile.md), and [art cookbook](../cookbook/procedural-strategy-art.md).

## 2. Corpus reviewed

Fable gist and the supplied Opus 5, Astra GPT-6, Sol GPT-5.6, Qwen 3.8 Max and Kimi K3 HTML sources were fetched and inspected. `corpus-audit.json` fingerprints each source and records symbol locations; the matrix compares 30 subsystems. Only Astra received browser visual inspection; six-source analysis is not six-game runtime certification. Original HTML is not included in the implementation or deliverable pack.

## 3. Fable findings

Seeded noise, deliberate starting clearing/resources and repeated flood/carve repair form a useful generate/validate/repair recipe. Its worker carry/dropoff/reacquire loop, queue supply reservation, control groups and contextual orders are complete genre examples. Direct occupant relocation during placement and a permissive production-spawn fallback are rejected. Repair rules protecting its mines/corridors remain application policy; no speculative map generator was extracted.

## 4. Opus findings

Traditional movement/gather/attack/attack-move/rally, queue/cancel/refund and multiple construction workers demonstrate systems breadth. Cached sprite frames, masonry courses, roof/crenellation detail and disk offsets are useful art/performance recipes. Construction throughput `1 + .55*(workers-1)` is game meaning. Allowing unit overlap at placement is not accepted as general collision policy. Its explorable-mask denominator usefully distinguishes an exploration objective from total map area.

## 5. Astra / Wilderland findings

The strongest presentation is a peaceful settlement game with isometric world art, a narrow resource header, mission card, bottom contextual dock and integrated minimap. Canvas primitives compose world assets; portraits reuse the same draw routines. Placement performs a temporary reachability check and rollback; blocked production waits. Its explored/visible/strength split separates discrete sight from fog softness. LocalStorage versioning is not a substitute for a validated save envelope.

## 6. Sol findings

Sol contributes an industrial rather than medieval interpretation: rounded chassis, strips/vents, radial crystal glows, repeated crystal fans and transform-based motion. Its semantic minimap and exploration-centered objective recur elsewhere. A comparable complete combat loop was not found. These observations motivate composition/palette templates, not a new general scene graph or glow subsystem in M18.

## 7. Qwen / Kimi marginal findings

Qwen corroborates conventional orders, queue refunds, speed modes and clearing held input on blur. Kimi corroborates shift/double-click selection, worker economy, rally and cached terrain. Unseeded randomness in these sources is a useful rejection case. No control groups were found in either inspected handler set; Kimi attack-move was not found. Absence findings are source-review limits, not runtime claims.

## 8. Cross-model convergence

Selection identity, contextual right-click, carry/dropoff, reserved production, explored/current visibility, minimap navigation and camera controls recur. Fable/Opus favor genre completeness; Astra/Sol have more differentiated visual products; Qwen/Kimi provide corroboration and narrower distinct cases. No model ranking or independent training provenance is inferred. `cross-model-convergence.json` preserves the comparison.

## 9. Rejected benchmark hacks

Renderer-owned truth, DOM-as-state, direct mouse mutation, unseeded semantic randomness, frame-dependent economy, duplicate projection math, broad global bags, raw string orders and permissive collision/placement shortcuts were rejected. InputMan actions are named IDs at the physical mapping boundary; dispatched game orders are typed records. The full rejection list specifies replacement owners rather than simply labeling an entire source bad.

## 10. JTF already-solved concepts

Reused CadenceScheduler, SpatialWorld2D overlap/sweep, Camera2D, InputManEngine/AurelianInputAdapter, Dominatus AiWorld/HFSM, Machina nodes/lowering/presentation, direct-outline text and Copeland Profile composition. SpriteForge's existing panel/atlas contracts were audited; a second sprite or vector format was not invented. No existing TinyFarm movement resolver or clock was duplicated inside a reusable engine module.

## 11. Newly integrated reusable concepts

`Aurelian.Strategy` provides identity selection and bounded visibility with no game/graphics dependencies. GameWorld2D gains invertible isometric projection. Aurelian.Machina gains a bounded HUD profile driven by facts, style and copy. `StrategyArt.ts` supplies ordinary first-party Profile templates reused across nine assets, including the fresh watchtower. Their current distribution/realization remains a proof pack, explicitly below production tooling qualification.

## 12. Selection model

Generic sorted identities support replace/add/subtract/toggle, detached snapshots, pruning and groups 1–9. Sample point/box selection uses Spatial2D; a broad isometric box query is filtered against exact projected screen coordinates. Shift and double-click semantics are app decisions. Input bindings expose groups 1–3; the generic kit supports 1–9. A non-game string-ID fixture proves selection has no unit dependency.

## 13. Order model

The sample dispatches typed selection, group, move, gather, construct, attack, build, produce and advance records. Construct orders are created only by accepted build transactions. IdleOrder implements stop. Batch validation rejects invalid identity/target/type before mutation. Attack-move, queued waypoints, hold-position distinctions and rally were audited but not implemented. Orders/economy stay sample-local until a second semantic consumer establishes a stable reusable law.

## 14. Economy / gathering model

Workers harvest two items per five semantic ticks, carry up to ten, return to headquarters and resume their target. Dominatus persists route/harvest/return behavior; the session applies staged facts/actions in stable identity order. Crystal and fresh wood retain separate balances and cargo kind. Switching resource orders first deposits existing cargo, preventing conversion of wood into crystal. This is a small concrete economy, not an economy scripting framework.

## 15. Production model

Recruitment reserves 25 crystal and one supply slot; queue bound is four and population cap twelve. Each unit takes fifty semantic ticks. A blocked building spawn holds the ready queue item. Worker and ranger use the same path. Unit-to-unit separation at spawn, cancellation/refund and rally are unqualified. The proof produces one worker through the actual queue and separately exercises ranger production.

## 16. Build placement

Preview is derived state and snaps to the same grid point used by commit. The session validates finite map bounds, the entire explored footprint, cost, building/unit/resource overlaps and an available idle worker. Rejection does not charge or alter semantic state. An accepted site reserves cost and assigns construction. Sweep/range interactions stop outside building geometry. Construction progress has a derived bar; geometry is still a simplified completed silhouette. The specific occupied-home and partially-unexplored-footprint cases have regression tests.

## 17. Fog / exploration

`VisibilityGrid` validates every source before clearing current visibility, reveals integer disks and retains explored cells. Restore copies exploration and clears visibility. The sample owns its one faction and calls reveal at 2 Hz; current visibility gates resource/enemy orders and enemy drawing. This qualifies bounded visibility/exploration, not occlusion, alliances, last-seen enemy snapshots, soft masks or scalable faction fog. The hard edge is a visible presentation limitation.

## 18. Minimap

The sample projects terrain/fog and semantic unit/resource positions; the camera outline derives from the inverse viewport. It does not shrink a world screenshot. Clicking changes only the camera and has a hash-preservation proof. It is intentionally sample-local: a generic tactical-map fact/interaction contract has not been extracted or tested with another world. The farming HUD fixture reuses the panel slot, not a farming minimap simulation.

## 19. Camera

The new small transform maps grid coordinates to diamond coordinates and back. Existing Camera2D owns zoom/bounds/follow in that projected plane. Selection inverts this same transform. Arrows, edge pan, wheel zoom, minimap jump and selection center are available. The sample's zoom is camera-centered; Wilderland's pointer anchoring was studied but is not claimed here. Isometric presentation never changes semantic coordinates.

## 20. Wilderland UI decomposition

Resource header, objective card, selection summary, four contextual cards, tactical area, notice and controls become named Machina nodes. Typed snapshots carry facts; records carry colors/copy. The logical canvas is 1280×800 and the native host scales uniformly. It is not a recreation of HTML, CSS gradients or responsive breakpoints. The farming restyle prompted a small shared copy record so application captions are no longer fixed RTS terminology.

## 21. Wilderland art-generation mechanism

Polygon/ellipse/line helpers, shaded boxes, gabled roofs, faceted foliage and transform/alpha composition generate world art. Offscreen world drawing supplies portraits; inline SVG supplies UI symbols; CSS supplies chrome. Sol adds rounded industrial detail, repetition and gradients. [The art research](../research/rts-procedural-art-mine.md) separates these mechanisms rather than calling them all sprites.

## 22. Procedural/vector asset toolkit

Existing Profile polygons, ellipses, circles, tubes, named layers and typed palettes do the geometric work. New ordinary functions `Paint`, `Block`, `Roof`, `Shadow`, `Figure` and `Lodge` supply reusable composition recipes. Source order is painter order; separate overlapping paint does not misuse disjoint geometric Add. Flat colors qualify; gradients, stroke realization, animation and soft fog do not. Sparse detail and static figures explain the visual qualification limit.

## 23. Copeland asset integration

The executable path is toolkit + `.profile.tsx` → `ProfileTsxCompiler.CompileComposition` → canonical paint contours/hash → cached native paths. The preferred `.obj.ts` suffix was not forced onto a different document kind. M17 Oblivion cards call ObjectAssetCompiler and require panels/atlas regions; they cannot inspect this composition without a deliberate owner adapter. No compiler, SVG parser, card editor or Vulkan source was modified. Nine assets appear in the generated pack through automatic discovery.

## 24. Strategy HUD profile

The profile accepts four resources/actions/objectives, stable node IDs, selection text, notice, typed style and application captions. The restyle verifies identical resolved node rectangles, text and operation order with changed paints. It remains a bounded profile, not a universal reactive HUD or input system. The native renderer uses existing Machina outline fonts for both measurement and text realization.

## 25. Integrated Aurelian proof

Run `dotnet run --project samples/Integrations/Aurelian.StrategyDemo -c Release` for a native playable window. The default path is the presenter; `--proof` is supplementary. `--launch-smoke` reached a visible window and three rendered frames and exited successfully. `native-launch-frame.png` is the application's framebuffer export, not an OS screenshot. Physical adapter → InputMan → selection/group/focus and minimap isolation are automated separately.

The 60-second semantic scenario completes gathering and a lodge and produces a worker. `deterministic-proof.json` records its hash, replay hash, cadence partitions and assertions. The sample has a target marker rather than an enemy AI, decorative water/trees rather than a terrain movement engine, and collision-aware straight movement rather than navigation planning. These limits are surfaced in the API documentation.

## 26. TinyFarm reuse analysis

Resource strip, objective card and contextual action profile can consume TinyFarmProjection/application facts. The farming fixture proves this presentation seam without changing TinyFarm truth or clock. Isometric assets can serve derived props/portraits if the TinyFarm presenter gains the canonical realization adapter. The tactical slot is reusable, but map facts/projection must come from TinyFarm and are not yet integrated. TinyFarmSimulationHost remains semantic-time owner; its resolver remains movement/inventory authority. No TinyFarm redesign or cross-game simulation extraction was attempted.

## 27. Dominatus integration

WorkerBehaviors uses existing AiWorld agents and HFSM states for persistent route/harvest/return behavior. It stages proposals and applies them after ticking all agents, ordered by unit ID. The application alone mutates resources, position, construction and health. Ranger and wood extend these same paths. Dominatus.Core tests and SpriteForge tests passed; no Dominatus source files were changed.

## 28. InputMan integration

Native callbacks record physical buttons/axes into AurelianInputAdapter; InputMan maps these to actions. The sample emits typed intents after HUD capture and contextual interpretation. Focus loss clears held input and drag state, and the proof verifies regain does not synthesize a stale command. Pointer capture and uniform-image coordinate conversion are host responsibilities. InputMan.Core tests passed; no second mapping system was introduced.

## 29. Spatial integration

Existing SpatialWorld2D supplies unit selection overlap, footprint exclusion and swept building collision. Resource/interaction proximity uses the existing spatial point/vector values and explicit app radii. The sample rebuilds small query worlds rather than adding caching infrastructure prematurely. It does not qualify crowd separation or path planning. The stress measurement isolates a 256-collider overlap query.

## 30. Determinism

The existing rational cadence receives both 100 ms partitions and 7+31+62 ms partitions for exactly sixty seconds; results and replay hashes match. A pause contributes no semantic ticks. Hash input includes units/orders/cargo/behavior phase, resources, buildings, production, current visibility and exploration. Typed order fields avoid culture-dependent record ToString hashing. Caller and reader arrays cannot mutate the stored tape. This is same-runtime deterministic replay, not cross-platform floating-point lockstep or a Deliverance save protocol. No serialized save was implemented.

## 31. Performance and validation

`performance.json` contains warmed mean time and current-thread managed allocations for 256-collider selection, five-unit simulation, fog, semantic minimap draw, one cached HQ vector and the whole frame. The full frame includes allocating an output bitmap; it is not an allocation-free presenter or GPU benchmark. Native allocations, compiler cold start, desktop copy/compositor time and large-army scaling are outside this measurement. No optimization claim is made from one local machine.

| Local operation | Mean milliseconds | Managed bytes per operation |
| --- | ---: | ---: |
| 256-collider selection | 0.0648 | 4,168 |
| Five-unit semantic update | 0.0147 | 3,113 |
| Fog update | 0.0168 | 240 |
| Semantic minimap projection and drawing | 0.1998 | 416 |
| Cached HQ vector drawing | 0.0530 | 0 |
| Full CPU world/HUD/minimap frame | 7.3033 | 70,970 |

`validation.json` records exact commands, local test counts and exits. Aurelian, Copeland, Machina.UI and JointTaskForce full lanes passed. Focused strategy tests passed again after the final fixes; Dominatus.Core, Dominatus.SpriteForge and InputMan.Core passed. Formatting and native smoke are recorded separately. Deliverance was not used; Oblivion tooling was inspected but unchanged. Remote CI was not run. The desktop sample follows the existing sample NU1903 suppression for transitive Tmds.DBus.Protocol; it is not a dependency-advisory remediation.

## 32. Fresh system extension

A fresh-context agent received the sample's public seams and added a ranger with 4.2 attack range, using existing production, orders, selection and groups. The proof demonstrates attack beyond soldier range without stepping into melee, normal move, rejection behavior and replay. No shared engine/input/renderer implementation was rebuilt by the extension. Root added the native F binding and asset selection projection to expose it in the playable sample.

## 33. Fresh presentation extension

A separate fresh-context agent added forest/farming facts and paint using StrategyHudSnapshot/Style. The proof compares IDs, rectangles, text and operation order and checks paint changes. Root subsequently exposed application copy and rendered `forest-farming-restyle.png`, with no simulation change. This is a second presentation fixture, not a second integrated farming game. The evidence distinguishes agent-local work from root integration corrections.

## 34. Fresh art extension

A third fresh-context agent authored a watchtower from existing Shadow/Block/Roof/Paint and a ranger palette variant from Figure: 53 source lines at handoff. No compiler, runtime or renderer changes were needed for discovery and asset-pack drawing. Root compiled both with the real compiler, recorded composition hashes and visually inspected the shared renderer output. `fresh-art-extension.json` records source hashes and approximate authoring effort.

## 35. Compounding-benefit comparison

`compounding-benefit.json` compares source sizes of complete benchmark HTML files against measured app/shared/tool/test source categories, and records extension locality. These scopes are unequal: the benchmarks contain more game content, while Aurelian depends on existing libraries. LOC is descriptive, not a productivity score. Agent time estimates are approximately eight minutes for system/wood, four for presentation and three for art; they are not instrumented baseline experiments. No inference-cost saving is measured. Demonstrated benefit is local second-feature work without reconstructing input, cadence, HFSM, layout or compiler infrastructure.

## 36. Owner-lane fixes

The actual visual blocker was a Machina pipeline overload that did not forward existing custom text-measurement options. The local fix forwards UiLoweringOptions while preserving the original overload, with a focused measurement regression. Application fixes cover carry-kind preservation, building reach, full-footprint exploration, detached tape records, consistent preview snap and matching font measurement/rendering. No broad engine/compiler or adjacent-repository refactor was required.

## 37. Deferred concepts

Principal deferred seam: reusable canonical Profile composition realization/preview with supported-paint validation, cache identity and second-game proof. Related polish work includes soft fog, density/detail, figure animation and richer terrain. Minimap facts/interaction remain sample-local. Additional evaluated but unneeded slice features are generated-map repair utility, navigation/separation, attack-move/rally, cancel/refund and serialized saves. These are explicit qualification boundaries, not hidden implementations or generic framework stubs.

## 38. Exact M19 recommendation

**M19: canonical Profile composition realization for two native consumers.** Start with the nine M18 assets, support a declared flat-fill paint profile with fail-fast diagnostics, preserve composition hashes/painter order/contour closure, and reuse an existing native raster/atlas realization path. Prove one strategy scene and one TinyFarm prop/portrait consumer before extracting a public adapter. Add source-linked preview/card projection through the appropriate document owner. Gate on current CPU parity, a usable default native command, measured cached rendering and a visibly improved Wilderland-style scene. Do not bundle fog semantics, pathfinding, economy scripting or a general vector editor into that task.

## 39. Diff stat

`diff-stat.json` records the final source/document/tool inventory, tracked line delta and added-file sizes, excluding build outputs, downloaded corpus and measurement logs. `compounding-benefit.json` separately counts app code, art source, shared mechanisms and tests. The only pre-existing tracked implementation file changed is MachinaPresentationPipeline; Aurelian.slnx registers the new projects. Other implementation files are additions. All work remains local and uncommitted.

Measured physical source lines: 2,120 sample C# including proofs, 177 Profile art, 317 shared C#, and 143 tests. The two existing tracked files have 9 insertions and 1 deletion. Documentation/tool additions are itemized separately in the diff artifact; generated binary images are inventoried by size and hash in the manifest.
