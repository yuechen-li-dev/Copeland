# AURELIAN-SPATIAL-2D-M3 report

## Outcome

**Outcome A — the small-game deterministic 2D spatial substrate is complete.**

`Aurelian.Spatial2D` answers overlap, continuous sweep, bounded sweep-and-slide, point,
trigger-transition, transient-volume, and debug-fact questions. It returns facts and
accepted displacement. It never changes a game entity, advances time, calls gameplay,
or persists a cache.

> Aurelian.Spatial2D produces deterministic spatial facts and accepted movement. It
> does not own gameplay state or rigid-body simulation.

## Existing spatial audit

| Concern | Existing implementation | Reuse? | M3 change |
|---|---|---:|---|
| TinyFarm targeting | `TinyFarmSpatialQueries` computes facing/range and app-specific priority | Partial | Keep gameplay priority local; prove engine overlap candidates can feed `AttackIntent` and `TakeIntent` |
| Movement authority | `TinyFarmResolver.ResolveSpatialMoveCore` alone validates and replaces `ActorSceneState` | Yes | Continuous movement now asks a cached `SpatialWorld2D` sweep before the same authoritative replacement |
| Tile walkability | `SceneDefinition.BuildSpatialIndex` and `TinyFarmScenes.IsBlocked` provide local point/tile truth | Yes, as parity oracle | `TinyFarmSpatialWorldAdapter` lowers blocking layout rectangles once into world-space AABBs per resolver |
| Navigation | Runtime-only `DotRecastNavigationPlanner` creates canonical paths | Yes | A path segment is a proposal; the spatial sweep validates physical displacement |
| World coordinates | `ScenePosition`, 1024 integer world units/tile; M1 presentation uses explicit `WorldPoint2` doubles | Yes | Spatial points/vectors use finite doubles and always mean world units; adapters convert typed app values explicitly |
| Camera/pixels | M1 `World2DUnitScale` converts world to presentation pixels | Boundary only | Spatial has no camera, pixel, DPI, or atlas dependency |
| Scene/world tables | TSON lowers to validated `SceneDefinition`, layout, anchors, routes | Yes | No new scene DSL; `TileRectangle` is the small authoring bridge |
| M21 attack placement | Enemy position is authored and resolver applies Sword rules | Yes | Spatial overlap returns opaque enemy candidate identity; resolver still owns range validation and damage |
| Geometry utilities | No reusable AABB/circle continuous-query package existed | No duplication found | Add the narrow engine-owned package |

## Package and dependency boundary

Ownership is `src/Aurelian/Aurelian.Spatial2D`. It targets .NET 10 and depends only on
the base class library. It has no TinyFarm, Dominatus, Machina, Vulkan, DotRecast,
Box2D, or platform-native dependency. TinyFarm depends inward on the spatial package;
the spatial package never depends on the game.

The closed M3 shape set is `Aabb2` and `Circle2`. Zero extent/radius is permitted for
point-like query adapters; negative values, NaN, and infinity are rejected. Capsule
is deferred because the top-down proofs do not benefit over circle/AABB. Raycast is
deferred because no M3 proof needs line-of-sight or projectile distance; continuous
shape sweep supplies the required movement law without widening the API.

## Deterministic laws

- Coordinate law: every shape, point, vector, displacement, contact, and epsilon is in
  world units. Tile authoring is multiplied once by the explicit units-per-tile scale.
- Identity law: solids use `SpatialColliderId`; triggers use `SpatialTriggerId`. Both
  compare by ordinal string value. Object addresses, dictionary order, and hash order
  never break ties.
- Overlap law: touching counts as overlap. Results order by squared center distance,
  then stable ID.
- Sweep law: `timeOfImpact` is normalized over `[0,1]`. Results order by time, then
  stable ID. Zero displacement is a no-op with no fabricated hit.
- Normal law: normals point from the obstacle toward the moving shape. Contact points
  lie on the moving shape at impact.
- Simultaneous-contact law: contacts within `SpatialMath2D.Epsilon` (`1e-9`) share a
  time, are reported by stable ID, and constrain slide in that order.
- Slide law: advance to the earliest contact, remove each negative normal component
  from the remaining displacement, and continue for at most four iterations by
  default (caller bound 1–16). No velocity, force, friction, restitution, or timestep
  is introduced.
- Penetration law: a sweep starting strictly overlapped reports an initial-overlap
  contact and accepts zero displacement. It does not depenetrate or teleport.
- Trigger law: triggers never participate in blocking. `DiffTriggers(previous,current)`
  returns ordinal `Entered`, `Stayed`, and `Exited` sets. The game owns persistence and
  interpretation.
- Layer law: typed bit masks filter collider layer and query layer. Category meanings
  such as World, Actors, Attacks, and Triggers remain application assignments, not
  engine enums or strings.

## Qualified behavior

The focused suite qualifies AABB/AABB, circle/circle, circle/AABB (including rounded
corner sweep), continuous AABB and circle sweep, thin-wall no-tunneling, wall stop,
diagonal slide, inner corner, outer corner, narrow passage, equal-TOI stable-ID tie,
initial overlap, zero displacement, trigger enter/stay/exit, masks, transient actor
volumes, debug facts, invalid inputs, attack candidates, pickup triggers, knockback,
and two identical 1000-step replays.

TinyFarm integration proves:

```text
InputMan Move
  -> SpatialMoveIntent
  -> TinyFarmResolver
  -> SpatialWorld2D.Sweep fact
  -> resolver-owned ActorSceneState replacement
  -> native/MonoGame world projection
```

Canonical blocked and unblocked cases match the former tile lookup exactly. The
M14/M15 fixed follower still uses the same public/internal resolver core; its warmed
allocation thresholds remain below 128 B/core reduction and 1024 B/full reduction.
An M21 attack overlap supplies the slime identity to the existing `AttackIntent`, and
the resolver alone emits `EnemyDefeated`. A pickup trigger supplies the Wild Mint ID
to the existing `TakeIntent`, and the resolver alone changes item ownership. Knockback
uses `SweepAndSlide` as a requested displacement and stops at the wall.

DotRecast remains separate:

```text
semantic destination -> DotRecast path proposal -> spatial sweep/slide validation
                     -> game resolver acceptance -> authoritative position
```

No pathfinder, navigation ownership, physics timestep, or mutable physics scene was
added. Actor-vs-actor blocking remains app policy; transient colliders make either
blocking or overlap queries possible without engine-owned actor simulation.

## Broadphase and performance

M3 retains an immutable, stable-ID-sorted flat scan. The Release evidence run on .NET
10.0.11 used 2,000 operations per sample. Representative measured ranges were:

| Collider count | Overlap us/op | Sweep us/op | Sweep+slide us/op | Trigger diff us/op |
|---:|---:|---:|---:|---:|
| 64 | 8.19 | 15.70 | 20.18 | 1.97 |
| 256 | 32.26 | 62.33 | 28.51 | 2.65 |
| 1024 | 24.02 | 30.68 | 37.98 | 0.52 |

These are bounded smoke measurements, not a cross-machine performance promise; tiered
JIT and host noise explain non-monotonic samples. Current authored TinyFarm scenes are
well below the counts that justify an index. Re-measure a real scene approaching 1024
repeatedly queried colliders; only then consider a deterministic uniform grid. A BVH,
dynamic tree, mutable spatial hash, and cache serialization are not justified.

## Debug, replay, and artifacts

`SpatialWorld2D.DebugFacts` exposes collider/trigger shapes and stable IDs.
`SpatialDebugFacts2D.ForSweep` adds the sweep segment, contact point, normal, and the
same collider IDs returned by queries. It is renderer-neutral and creates no UI.

The 1000-step replay was run twice with exact contact/position trace equality. Its
SHA-256 is `a47164134afc2f2d291821d6fcdf9c6f0a001f035a0b7272bf1e1f5608448138`.
Compact evidence is under `artifacts/aurelian-spatial-2d-m3/` in the six required JSON
files. The generator is `tools/Aurelian.Spatial2DM3`.

## Scope decisions and future coexistence

There is no gravity, rotation, joint, material, force, velocity, acceleration, or
rigid-body ownership. A future dynamics engine can coexist as a separate integration:

```text
Aurelian.Spatial2D -> deterministic query and character-movement facts
optional dynamics engine -> separate simulation integration owned by its application
```

M3 is pure managed .NET and has no OS-specific code. The exact next milestone is
`AURELIAN-GAME-AUDIO-M4`.
