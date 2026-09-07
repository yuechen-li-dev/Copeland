# Hollowflux semantic map — M20

## Method and inputs

The 727,282-byte production JavaScript bundle is primary evidence. The Claude audit is a secondary map: its proposed names and subsystem boundaries were used to locate strings, object shapes, adjacent functions, and call sites, then checked against the bundle. The 107,027-byte CSS bundle was read to distinguish interface styling from semantic code; it yielded no core pearl.

The mining tool reads every input in full and refuses truncated inputs. Evidence offsets and bounded original fragments are retained in `artifacts/hollowflux-pearl-mining-m20/semantic-map.json` and `decompilation-islands.json`.

## Classification

| Subsystem | Evidence pattern | Responsibility | Confidence | JTF owner | Class | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| Input, buffering, DOM/mobile controls | `attackBuffer`, pointer/touch handlers | Physical input and browser UI | High | InputMan already owns mapping | A/D | Keep InputMan; reject browser glue |
| Combat move table | `bs={sword:{kind:"sword"...startupTicks` | Generic move tuning | High | Aurelian.Combat | B | Adapt names and shape |
| Combat action functions | `function ja`, `function Ba`, `function sr` | Start, advance, and project a phased action | High | Aurelian.Combat + Dominatus transitions | B | Recover semantics; use OptFlow transition authority |
| Damage/world mutation | active-phase hit loop and player/enemy mutation | Application combat truth | High | TinyFarm resolver | A/C | Keep mutation application-owned |
| Fluid SoA | `class ha` and parallel typed arrays | Resident semantic field channels | High | Aurelian.Field2D | B | Adapt bounded channel set |
| Connected disturbances | `disturbanceVisit`, queue, liquid mask | Shape influence without wall leakage | High | Aurelian.Field2D | B | Retain generation-counter BFS principle |
| Charge | `chargeVisits`, `conductive` | Conductive liquid propagation/query | High | ReactiveFluid2D | B | Retain as second semantic consumer |
| Full theme cell behavior | `bloodroot`, `violet-static`, `emberglass` | Hollowflux biome meaning | High | Hollowflux application | C | Reject from reusable core |
| Shader wet-boundary correction | `liquidCoverage` | Prevent neutral dry texels bleeding into wet samples | High | Visual TS realization | B | Re-author in Visual TS |
| Canvas/WebGL renderer | duplicated painter/compositor classes | Browser presentation | High | None | D/E | Reject |
| Procedural actor/equipment painter | `paintHeldWeapon` and layered paint methods | Equipment-driven visual composition | Medium | Copeland.Profile | A/C | Existing Profile layering is stronger; no copy |
| Loot/affixes | `generationVersion`, slot/affix tables | Deterministic app-local progression | High | TinyFarm if pressured | C | Defer; combat/field higher leverage |
| Save regeneration and validation | `hollowflux.save.v1`, sparse charge cap | Seed-derived world plus bounded deltas | High | Deliverance/application | B | Document strategy; do not add serializer magic |
| Procedural WebAudio | oscillator/noise graph | Browser audio realization | High | Aurelian.Audio | D/B | Defer recipe layer; semantic cue ownership already exists |
| Telemetry/visual-test hooks | `dataset.hollowflux`, `visualTest` | Benchmark verification harness | High | Evidence tooling only | E | Do not ship as engine API |

## Recovered semantic names

- `bs` → `WeaponMoveDefinitions`.
- `ja` → `StartCombatAction`.
- `Ba` → `AdvanceCombatPhase`.
- `sr` → `ProjectCombatPose`.
- `ha` → `ReactiveFluid2D` (class boundary only; not a claim about its original source name).
- `disturbanceVisits`/`disturbanceQueue` → connected-liquid visit generations and BFS queue.

Names above are high-confidence because strings and field shapes agree across construction, update, rendering, and telemetry call sites. Theme-specific helper names remain deliberately unrecovered where meaning is not needed by M20.

## What was not taken

No ECMAScript parser, JavaScript runtime, general decompiler, renderer fork, Canvas painter, DOM/mobile glue, Hollowflux lore, exact art, exact balance table, RPG framework, material graph, compute-fluid solver, animation graph, or procedural-audio workstation was introduced.
