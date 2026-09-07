# M21 field production baseline

| Concern | M20 state | Production owner | M21 action |
| --- | --- | --- | --- |
| Field lifetime | Standalone proof fixture | `TinyFarmSession` | Own one independent `TinyFarmFieldRuntime` per session |
| Authored water | Riverside river was a blocking decoration | TinyFarm content plus session recipe | Reuse its 6 x 10 tile placement, make it traversable, add a wet-safe border, and accept hosted sword motion while standing in water |
| Base topology | Proof-created masks | TinyFarm field recipe | Regenerate dimensions, liquid/conductive masks, dry border, and base current deterministically |
| Mutable channels | `ReactiveFluidSnapshot` proof | TinyFarm session | Persist height, velocity, foam, charge, flow, masks for validation, tick, and debounce state |
| Cadence | Internal 60 Hz accumulator | `TinyFarmSimulationHost` | Schedule the field at 60 Hz through the existing `CadenceScheduler` |
| Movement | No default routing | TinyFarm session | Accepted player locomotion in wet cells emits a subtle point/short-line disturbance |
| Combat | Atomic resolver spin-loop | Hosted session over `Aurelian.Combat` and Dominatus transitions | Advance one phase tick per semantic cadence tick; motion plus stronger contact disturbance |
| Gameplay feedback | Bounded query fixture | TinyFarm application | Conductive charge above 0.35 applies a 12-tick movement penalty with a 30-tick debounce |
| Save/load | Recommendation only | Deliverance bridge | Module schema 3 carries field snapshot v1; schema 2 regenerates the default field |
| Replay/hash | World state only | TinyFarm replay | Add initial and per-record field hashes; exclude GPU bytes and overlays |
| Native rendering | Shader proof only | TinyFarm native presenter | Project RGBA, compile reusable Visual TS, upload on semantic generation change, draw via Vulkan |
| Inspection | Static evidence PNGs | `TinyFarm.Oblivion` integration | Register two read-only live surfaces backed by the running host |
| Extra channels | Rejected | Future pressured consumer | Add none; blood-scent remains explicitly deferred despite strong Hollowflux evidence |

The ownership law is: `TinyFarmSession` owns semantic truth; combat and movement request changes; Deliverance snapshots it; replay hashes it; Oblivion observes it; native rendering uploads a derived projection only.
