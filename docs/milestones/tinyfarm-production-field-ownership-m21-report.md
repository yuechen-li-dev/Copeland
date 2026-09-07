# TINYFARM-PRODUCTION-FIELD-OWNERSHIP-M21 report

## 1. Outcome

**Outcome A — Field2D is ordinary TinyFarm world infrastructure.** The default game now has a session-owned Riverside stream. It advances on the existing scheduler, responds to traversal and real hosted combat, applies one bounded charged-water rule, survives Deliverance and replay, renders through native Vulkan, and is exposed through read-only live Oblivion surfaces.

## 2–6. M20 seam, ownership, pond, regeneration, session

M20 proved the mechanisms but stopped before application ownership. M21 assigns the live field to `TinyFarmSession`; neither the renderer nor a static fixture owns it. The existing Riverside `river` placement remains 6 x 10 tiles and becomes traversable. A 14 x 22 half-tile field adds a one-cell dry border around the authored 12 x 20 wet region. A selected sword can perform a normal hosted swing while the player stands in water, so combat motion reaches the field without adding an enemy or redesigning the map.

The deterministic recipe regenerates definition ID, dimensions, origin, cell size, liquid/conductive masks, dry border, and base downstream flow. Mutable channel values, tick, charged-water cooldown, penalty, and contact count belong to the session snapshot. Two sessions construct independent fields; `Field2D<T>` remains generic and unchanged by TinyFarm concepts.

## 7–10. Cadence, determinism, movement, combat routing

`TinyFarmSimulationHost` adds a 60 Hz field cadence to its existing `CadenceScheduler`; it adds no loop, thread, or timer. The underlying six-step catch-up cap remains. Ten 100 ms partitions and twenty 50 ms partitions produce the same 60-tick field hash.

Accepted wet locomotion emits a subtle point or short directional line. TinyFarm has no dash, so no synthetic dash system was added. Combat shapes route from move data: sword and hoe use arcs, spear uses a line, hammer uses a ring. The law is both: an accepted attack creates a low-strength motion disturbance, then a stronger contact disturbance.

The second Claude audit correctly identified that M20's TinyFarm adapter spun all 18 phases inside one resolution. M21 removes that behavior from the default hosted path: a combat action persists in the session and `CombatResolver.Advance` runs once per existing semantic tick. Contact occurs during Active and recovery remains observable. Direct resolver calls stay atomic for deterministic command/replay compatibility, but now evaluate contact once instead of simulating decorative time.

## 11–13. Charged-water gameplay and authority

Only conductive wet cells carry charge. Once per field tick, the application samples the player's actual semantic position. Charge above 0.35, when the 30-tick cooldown permits, applies a 12-tick movement penalty and records a semantic contact. The cooldown prevents per-tick damage behavior. The renderer only visualizes the result. Enemy charge response and current-driven displacement are deferred: the first would add another mechanic, and the second would require a resolver-owned movement proposal path.

Foam remains presentation-supporting semantic data; it has no invented gameplay rule. Recent field facts retain only 16 diagnostic entries and do not create a generic bus.

## 14–18. Snapshot, save, migration, replay, hash

`TinyFarmFieldSnapshot` version 1 wraps the bounded `ReactiveFluidSnapshot` and application debounce state. The full snapshot is small enough to prefer correctness over premature sparse-delta infrastructure; restore validates its topology against the regenerated authored recipe. Deliverance module schema 3 includes it. The schema 2 migration supplies a deterministic default field, so old saves need no fake payload.

The exact proof disturbs and charges water, triggers the gameplay contact, captures, mutates further, and restores the same field hash and debounce/contact state. TinyFarm's hosted attacks are not persisted mid-phase: saving is explicitly rejected until the short action reaches its safe boundary. This is application policy, not a missing serializer trick.

Replay envelopes now carry the initial field snapshot/hash and optional expected field hashes per semantic input. Movement and attack inputs are replayed into the field; GPU pixels, interpolation, and diagnostic overlays are excluded. Existing format-1 envelopes without field members deterministically begin from the default recipe.

## 19–23. Native projection and correctness

The native path is semantic field → RGBA packing → reusable `ReactiveFluid2D.v.ts` Visual TS → VD-MIR → HLSL/SPIR-V → `VulkanOrderedQuadRenderer`. The promoted shader retains M20's bounded wet-coverage reconstruction and now multiplies RGB by wet coverage explicitly, closing the dry-texel precondition noted by Claude. The texture is linear RGBA8 semantic data and the native pass uses straight alpha; dry foam, wet mask, and charge are zero, and dry RGB cannot contribute after shader masking.

The presenter tracks `ProjectionGeneration` and uploads only on semantic change. The production texture is 1,232 bytes. Native proof measured four uploads / 4,928 bytes over the full walkthrough's relevant field changes. Normal launch uses this presenter; no proof-only renderer exists. Readback images cover calm, hammer-ring, and charged water.

## 24–26. Live Oblivion surfaces and identities

`TinyFarm.Oblivion` registers `tinyfarm.live.field` and `tinyfarm.live.combat` against the actual running host. Every capture is fresh. The field card shows dimensions, cell size, owner, tick, hash, snapshot version, wet-cell count, min/max for height/velocity/foam/charge/flow X/Y, deterministic presets, and recent disturbances. The combat card shows active move, phase, phase tick, facing, hit IDs, contact count, and the atomic-save law. Both are read-only.

Stable concept paths are `TinyFarm.World.Pond.Fluid.Charge`, `TinyFarm.World.Pond.Fluid.Disturbance`, and `TinyFarm.Player.Combat.ActiveAction`.

## 27–30. Fresh-context extension checks and user experience

- A watering-can-style point disturbance needs only `ApplyMovement`/a point `FieldDisturbance`; it does not require a field rewrite. TinyFarm does not currently carry a watering can, so none was invented.
- A two-stage hammer ripple is two existing ring disturbance records with different strengths/timing; no shape or renderer change is needed.
- Wet-cell count was added solely in the inspector projection.
- Field snapshot versioning and the old schema-2 fixture use the normal Deliverance migration path.

The stream occupies the existing coherent Riverside feature, is reachable and traversable, visibly reacts, and has no debug text in normal play. TinyFarm did not acquire Hollowflux loot, bosses, biome fluids, lighting, or weapon-family sprawl.

## 31. Performance and field-size pressure

The generated `performance.json` contains current-machine timings for calm 14 x 22 updates, hammer disturbance, charge, RGBA projection, and measured 2x-dimension / 4x-cell pressure fields. The production update measured zero managed allocation across 600 steady-state ticks. Projection intentionally allocates one 1,232-byte payload; native upload is generation-gated.

## 32–34. Multi-field, generic non-regression, deferrals

The independent-session fixture proves no singleton assumption. `Field2D<T>` gained only snapshot restore support in `ReactiveFluid2D`; TinyFarm definitions, rules, identities, and events live above it. No blood, memory, emission, density, vorticity, or theme behavior channel was added.

Claude's confirmed blood-scent finding is now reconciled into both semantic map and pearl ledger. It is a high-value field-to-AI feedback pearl, explicitly deferred to a game that has a real blood/scent mechanic; adding it to TinyFarm would violate M21's channel-pressure gate. The artifact formerly called `decompilation-islands.json` is now `semantic-anchors.json`, with an explicit naming law that the semantic names are human-assigned and source anchors are machine-verified.

The remaining Claude findings were implemented: Startup/Recovery no longer allocate hit sets or sorted arrays, presentation reports the phase that produced contacts, the future multi-contact `SingleOrDefault` crash is removed, contacts are no longer overwritten by a phase spin-loop, and dry shader output is masked. The historical commit message saying “enemy attacks” is inaccurate—this is player-to-enemy combat—but history was not rewritten under an active dirty worktree; this report is the durable correction.

## 35. Exact M22 recommendation

Use the production field in a second application before generalizing persistence or inspection infrastructure. If that application owns a scent-like channel, add a generic scalar gradient-sampling helper to Field2D and keep the channel and AI meaning application-owned. Do not add it speculatively to TinyFarm.

## 36. Diff and validation

Machine-readable evidence is under `artifacts/tinyfarm-production-field-ownership-m21/`. The tracked diff is 55 files, 1,065 insertions, and 387 deletions, plus 34 new files (1,751 text lines and eight PNGs). This includes regenerated native evidence from the existing M9/M10/M11 proof path as well as the new M21 artifacts.

Validation completed on 2026-09-06:

- `dotnet test TinyFarm.slnx`: 329 passed.
- `dotnet build Aurelian.slnx`: succeeded with zero warnings/errors; `dotnet test Aurelian.slnx --no-restore`: 795 passed, including 139 shader/Visual TS tests.
- `dotnet test Dominatus.slnx`: all test projects passed; only the eight credentialed Stripe live tests were skipped.
- `dotnet test Deliverance.slnx`: 30 passed, one golden-file generator skipped.
- M20 and M21 evidence generators: passed.
- Native project build: zero warnings/errors. Current-code full walkthroughs completed the functional run and wrote the three visually inspected Vulkan field captures. The 120-frame native sample measured 7.61 ms mean, 11.66 ms p99, and 13.13 ms worst. Its older single-sample changed-UI gate was intermittently noisy in this run (18.77 ms dialogue-open versus a 16.67 ms threshold); this is outside Riverside and did not affect the field pass, but it is retained here rather than hidden.
- `git diff --check`: no whitespace errors (line-ending normalization warnings only).
