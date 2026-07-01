# A16 World Typed Data Stores M0 Audit

## 1. Files changed

Production world files added:

- `src/Aurelian.World/Stores/UnitName.cs`
- `src/Aurelian.World/Stores/UnitNameStore.cs`
- `src/Aurelian.World/Stores/Transform2.cs`
- `src/Aurelian.World/Stores/Transform2Store.cs`
- `src/Aurelian.World/Stores/WorldDataDocument.cs`
- `src/Aurelian.World/Stores/WorldDataSnapshot.cs`
- `src/Aurelian.World/Stores/UnitDataSnapshot.cs`
- `src/Aurelian.World/Stores/WorldDataSnapshotBuilder.cs`

Production actuation files added or changed:

- `src/Aurelian.Actuation/World/Requests/SetUnitNameRequest.cs`
- `src/Aurelian.Actuation/World/Requests/RemoveUnitNameRequest.cs`
- `src/Aurelian.Actuation/World/Requests/SetUnitTransform2Request.cs`
- `src/Aurelian.Actuation/World/Requests/RemoveUnitTransform2Request.cs`
- `src/Aurelian.Actuation/World/WorldDataActuator.cs`
- `src/Aurelian.Actuation/World/WorldActuationDiagnosticCodes.cs`
- `src/Aurelian.Actuation/World/WorldActuationResult.cs`

Tests added:

- `tests/Aurelian.World.Tests/WorldTypedStoresM0Tests.cs`
- `tests/Aurelian.Actuation.Tests/WorldDataActuatorM0Tests.cs`

Docs changed:

- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/world-model-doctrine.md`
- `docs/audits/0016-a16-world-typed-data-stores-m0.md`

## 2. Task scope

A16 implements the first small typed world data stores. The scope is intentionally narrow:

- unit name/label data;
- library-free 2D transform data;
- a small document shape that groups these stores with the existing `WorldDocument`;
- a snapshot shape that resolves the world and exposes unit data in deterministic pre-order;
- typed actuation requests/results for setting and removing store entries.

A16 does not implement ECS, processors, systems, an entity manager, behavior runtime, blackboards, renderer integration, Dominatus integration, asset/shader integration, physics, navigation, service locators, reflection, or generic component abstractions.

## 3. Store model

The store model lives in `Aurelian.World.Stores` and represents explicit typed data, not logic.

`UnitName` is a small value record. `UnitNameStore` maps `UnitId` to `UnitName` and exposes:

- `Empty`;
- `TryGet`;
- `Set`;
- `Remove`.

`Transform2` is a 2D transform record struct with `X`, `Y`, `RotationRadians`, `ScaleX`, and `ScaleY`, plus `Identity`. `Transform2Store` maps `UnitId` to `Transform2` and exposes:

- `Empty`;
- `TryGet`;
- `GetOrIdentity`;
- `Set`;
- `Remove`.

Both stores return new store instances from `Set` and `Remove`. They clone their dictionaries before applying changes so the prior store instance is not mutated in place.

## 4. WorldDataDocument / snapshot model

`WorldDataDocument` groups the composition document with typed stores:

```text
WorldDocument + UnitNameStore + Transform2Store
```

It does not replace `WorldDocument`; it is a higher-level data document for world data M0.

`WorldDataSnapshotBuilder.Create` resolves `WorldDataDocument.World` through `WorldUnitResolver` and requires the world to resolve successfully. If resolution fails, it throws `InvalidOperationException`. This is acceptable for M0 because resolver and actuator tests already validate world structure, and A16 snapshots are intended for valid authored data.

`WorldDataSnapshot` contains:

- the resolved world;
- unit snapshots in resolver pre-order;
- kind and optional logic reference;
- optional name;
- transform, defaulting to `Transform2.Identity` when no transform is set;
- immediate children.

## 5. Actuation requests/results

A16 adds typed request records under `Aurelian.Actuation.World.Requests`:

- `SetUnitNameRequest`;
- `RemoveUnitNameRequest`;
- `SetUnitTransform2Request`;
- `RemoveUnitTransform2Request`.

These requests operate on `WorldDataDocument`, not just `WorldDocument`.

A generic `WorldActuationResult<TDocument>` was added while preserving the existing non-generic `WorldActuationResult` used by `WorldUnitActuator`. `WorldDataActuator` returns `WorldActuationResult<WorldDataDocument>`.

## 6. Validation/diagnostics

A16 adds these world actuation diagnostics:

- `AAW1011 InvalidUnitName` for null/empty/whitespace unit names;
- `AAW1012 InvalidTransform` for non-finite 2D transform values;
- `AAW1013 UnitNameNotSet` for no-op name removal;
- `AAW1014 UnitTransformNotSet` for no-op transform removal.

Existing `AAW1002 UnitNotFound` is reused when store requests target a unit that is not present in the world document.

Transform validation is in actuation rather than the store constructor. The actuator checks every `Transform2` double with `double.IsFinite`.

Remove-missing name and transform operations return `NoOp` with an info diagnostic. Rejected and no-op operations return the original `WorldDataDocument` instance.

## 7. Immutability/locality behavior

The implementation preserves locality and immutable-style mutation:

- stores clone dictionaries before changing entries;
- `WorldDataActuator` returns new `WorldDataDocument` instances for applied store mutations;
- rejected store mutations return the original document;
- no-op remove-missing operations return the original document;
- `Aurelian.World` does not reference `Aurelian.Actuation`;
- mutation policy remains in `Aurelian.Actuation.World`.

This demonstrates the intended split: world descriptors define composition, typed stores attach structured data by `UnitId`, and actuators provide named mutation boundaries.

## 8. Tests added

World store tests cover:

- `UnitNameStore_Set_ReturnsNewStoreWithoutMutatingOriginal`;
- `UnitNameStore_Remove_ReturnsNewStoreWithoutMutatingOriginal`;
- `Transform2Store_GetOrIdentity_ReturnsIdentityWhenMissing`;
- `Transform2Store_Set_ReturnsNewStoreWithoutMutatingOriginal`;
- `WorldDataDocument_FromWorld_UsesEmptyStores`;
- `WorldDataSnapshot_IncludesNameAndTransformWhenPresent`;
- `WorldDataSnapshot_UsesIdentityTransformWhenMissing`.

Actuation tests cover:

- `WorldDataActuator_SetUnitName_AppliesToExistingUnit`;
- `WorldDataActuator_SetUnitName_RejectsMissingUnit`;
- `WorldDataActuator_SetUnitName_RejectsEmptyName`;
- `WorldDataActuator_RemoveMissingName_ReturnsNoOp`;
- `WorldDataActuator_SetTransform_AppliesToExistingUnit`;
- `WorldDataActuator_SetTransform_RejectsMissingUnit`;
- `WorldDataActuator_SetTransform_RejectsNonFiniteValues`;
- `WorldDataActuator_RemoveMissingTransform_ReturnsNoOp`;
- `WorldDataActuator_DoesNotMutateOriginalDocument`.

## 9. Boundary checks

Boundary commands run:

```bash
rg -n "EntityManager|Processor|SystemBase|ServiceLocator|Blackboard|BbKey|AiWorld|Dominatus|GraphicsDevice|Render|Shader|Asset|Reflection|Activator|GetType\(|Type\.|IComponent|ComponentStore" src/Aurelian.World src/Aurelian.Actuation tests/Aurelian.World.Tests tests/Aurelian.Actuation.Tests -g '*.cs' || true

rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.World src/Aurelian.Actuation tests/Aurelian.World.Tests tests/Aurelian.Actuation.Tests -g '*.cs' -g '*.csproj' || true

git status --short
```

The first boundary scan reported one pre-existing test method name:

```text
tests/Aurelian.World.Tests/WorldUnitM0Tests.cs:165:    public void WorldDocument_DoesNotRequireRendererAssetsOrDominatus()
```

This is a test name asserting the absence of renderer/assets/Dominatus requirements, not a production dependency or integration.

The second boundary scan returned no matches.

## 10. Validation results

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "EntityManager|Processor|SystemBase|ServiceLocator|Blackboard|BbKey|AiWorld|Dominatus|GraphicsDevice|Render|Shader|Asset|Reflection|Activator|GetType\(|Type\.|IComponent|ComponentStore" src/Aurelian.World src/Aurelian.Actuation tests/Aurelian.World.Tests tests/Aurelian.Actuation.Tests -g '*.cs' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.World src/Aurelian.Actuation tests/Aurelian.World.Tests tests/Aurelian.Actuation.Tests -g '*.cs' -g '*.csproj' || true
git status --short
```

Results:

- Build passed.
- Tests passed.
- Boundary scan found no forbidden production dependencies.
- No Dominatus dependency was added to `Aurelian.World`.
- No renderer/assets/shader dependency was added to `Aurelian.World`.
- No reflection use was added.
- No generic component framework, ECS manager, or processors were added.
- `CodeReferences/*` and `vendor/Dominatus/*` were not modified.

## 11. Deferred features

Deferred features remain:

- full ECS;
- processors/systems;
- entity manager;
- behavior runtime;
- blackboards;
- Dominatus world observation bridge;
- renderer integration;
- asset/shader integration;
- renderable/camera stores;
- 3D transform/store M1;
- hierarchy/world-transform propagation;
- physics/nav libraries;
- service locators;
- reflection-based runtime registration.

## 12. Next recommendation

Recommended next milestone:

```text
A17 — Render snapshot contracts M0
```

A16 now provides enough structured world data to start defining renderer-independent snapshot contracts. A17 should keep the same boundaries: `Aurelian.World` can expose data snapshots, but renderer, asset, and shader dependencies should remain outside world, with render snapshot contracts acting as the next explicit boundary.
