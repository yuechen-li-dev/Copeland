# Hollowflux Claude audit cross-check — M20

The audit was intentionally used as a different-model pre-map. Status still comes from the production bundle.

| Audit claim | Status | Bundle evidence |
| --- | --- | --- |
| CPU fluid is a central shared structure | Confirmed | `class ha` owns height, velocity, material, current, masks, charge, projection pixels, and gameplay sampling |
| Roughly thirty parallel typed arrays / SoA | Confirmed | contiguous class-field declaration beginning at `heightField` and constructor allocations |
| Fixed 60 Hz and capped catch-up | Confirmed | accumulator path and six-step cap; retained as 60 Hz / six steps |
| Dry neighbors mirror center height | Confirmed | update kernel substitutes center for non-liquid neighbors |
| Disturbance uses generation-counter BFS over connected liquid | Confirmed | increment/wrap of `disturbanceVisit`, queue traversal, and liquid-gated enqueue |
| Point and line disturbance forms | Confirmed | `disturbanceInfluence` and weapon call sites |
| Arc and ring disturbance forms | Confirmed | sword/twinblade arc and hammer/bell ring call sites |
| Charge stays in conductive water | Confirmed | conductive mask, charge visit queue, and water-gated hazard queries |
| Combat is startup/active/recovery frame data | Confirmed | exact `ja` construction and `Ba` advancement function |
| Hit-once actor tracking | Confirmed | `hitActorIds` construction, membership check, and push on hit |
| Six weapon families with sparse variants | Confirmed | `bs` family records and neighboring variant tables |
| Enemy moves reuse the action structure | Confirmed | `da`, `Ka`, and enemy `combatAction=ja(...)` sites |
| Fluid drives enemy scent behavior | Confirmed | blood samples at four offsets and gradient steering call sites |
| Deterministic loot is keyed from floor/actor | Confirmed | integer hash path and item-generation records |
| Boss reward uses build-aware search | Confirmed | nested candidate iteration and build evaluation call path |
| Generation and rules versions are separate | Confirmed | both fields occur independently in generation, stats, load, and repair paths |
| Save regenerates world and validates sparse state | Confirmed | `hollowflux.save.v1`, seed/theme/entity validation, bounded charge list |
| Procedural WebAudio is field-driven | Confirmed | oscillator/noise methods and water/charge/boss intensity reads |
| Renderer has two near-duplicate giant forks | Partially confirmed | two large renderer paths and mode factory are present; the audit's exact 95% textual-similarity percentage was not independently recomputed |
| Visibility is one layer per torch, DDA-precomputed | Partially confirmed | visibility textures and ray traversal located; per-torch storage cardinality was not exhaustively re-derived |
| Echo buffer is 48 slots and astral-glass uses 19 ticks | Confirmed | five 48-slot typed arrays and theme `echoDelay:19` |
| `memory` is shader-only | Partially confirmed | shader reads are clear; proving no indirect gameplay read over the entire bundle would require a fuller data-flow analysis |
| Bundle is machine-authored | Not decidable | duplication and harness artifacts are observable; authorship is not executable semantics |

No material audit claim used for an implemented pearl was contradicted. Partial confirmations were not promoted into public APIs.
