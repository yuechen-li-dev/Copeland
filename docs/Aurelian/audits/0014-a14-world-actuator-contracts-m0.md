# A14 — World actuator request/result contracts M0

## 1. Files changed

- `src/Aurelian.Actuation/Aurelian.Actuation.csproj`
- `src/Aurelian.Actuation/World/WorldActuationStatus.cs`
- `src/Aurelian.Actuation/World/WorldActuationDiagnosticSeverity.cs`
- `src/Aurelian.Actuation/World/WorldActuationDiagnostic.cs`
- `src/Aurelian.Actuation/World/WorldActuationDiagnosticCodes.cs`
- `src/Aurelian.Actuation/World/WorldActuationResult.cs`
- `src/Aurelian.Actuation/World/WorldUnitActuator.cs`
- `src/Aurelian.Actuation/World/Requests/SpawnUnitRequest.cs`
- `src/Aurelian.Actuation/World/Requests/DestroyUnitRequest.cs`
- `src/Aurelian.Actuation/World/Requests/AttachChildRequest.cs`
- `src/Aurelian.Actuation/World/Requests/DetachChildRequest.cs`
- `src/Aurelian.Actuation/World/Requests/ReplaceUnitDescriptorRequest.cs`
- `tests/Aurelian.Actuation.Tests/WorldUnitActuatorM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/world-model-doctrine.md`
- `docs/audits/0014-a14-world-actuator-contracts-m0.md`

## 2. Task scope

A14 implements the first typed mutation boundary over A13 world unit documents. The milestone adds request records, result/status/diagnostic contracts, and a small actuator service for deterministic mutation of `WorldDocument` composition and descriptors.

A14 does not add Dominatus integration, blackboards, behavior runtime, render/asset/shader integration, physics, editor workflows, LLM calls, ECS managers/processors, or in-place `WorldDocument` mutation.

## 3. Dependency direction

`Aurelian.Actuation` now references `Aurelian.World` so actuation contracts can name `WorldDocument`, `WorldUnitDescriptor`, `UnitId`, and `UnitChild`.

`Aurelian.World` does not reference `Aurelian.Actuation`, Dominatus, render, asset, or shader projects. The world project remains data/composition/resolution only.

## 4. Request/result model

The M0 request records are pure data:

- `SpawnUnitRequest(WorldUnitDescriptor Unit)`
- `DestroyUnitRequest(UnitId UnitId)`
- `AttachChildRequest(UnitId ParentId, UnitChild Child)`
- `DetachChildRequest(UnitId ParentId, UnitId ChildId)`
- `ReplaceUnitDescriptorRequest(WorldUnitDescriptor Unit)`

The result model returns a `WorldActuationStatus`, a `WorldDocument`, and diagnostics. `Applied` results carry the new document. `Rejected` and `NoOp` results carry the original document.

## 5. Mutation semantics

- Spawn rejects duplicate unit IDs, otherwise adds the descriptor and validates the resulting document.
- Destroy rejects root units, missing units, and units with immediate children. Leaf destruction removes the unit and any immediate parent references to it.
- Attach requires an existing parent and existing child unit, rejects duplicate child attachments, rejects duplicate non-null slots, appends the child to the parent's immediate composition, and validates the resulting document.
- Detach requires an existing parent, removes only the requested immediate child, and returns a no-op diagnostic if the child is not attached.
- Replace descriptor requires the replacement ID to already exist, replaces the descriptor at that ID, preserves the document root ID, and validates the resulting document.

## 6. Diagnostics

A14 adds stable actuation diagnostic codes:

- `AAW1001` / `UnitAlreadyExists`
- `AAW1002` / `UnitNotFound`
- `AAW1003` / `ParentNotFound`
- `AAW1004` / `ChildNotFound`
- `AAW1005` / `ChildAlreadyAttached`
- `AAW1006` / `ChildNotAttached`
- `AAW1007` / `CannotDestroyRoot`
- `AAW1008` / `InvalidMutationWouldBreakWorld`
- `AAW1009` / `DuplicateChildSlot`
- `AAW1010` / `CannotDestroyUnitWithChildren`

Resolver-invalid applied candidates are rejected with `InvalidMutationWouldBreakWorld` and mapped resolver diagnostics.

## 7. Immutability/locality behavior

The actuator treats input documents as immutable-style values. It validates null reference arguments at the API edge, clones unit dictionaries before applying changes, creates new child lists for composition updates, and returns the original document for rejected/no-op outcomes.

Tests assert that applied spawn, attach, detach, destroy, and replace operations do not mutate the source document and that attach/detach preserve immediate-only composition locality.

## 8. Tests added

`tests/Aurelian.Actuation.Tests/WorldUnitActuatorM0Tests.cs` covers:

- spawn success and duplicate rejection;
- attach success, missing parent, missing child, duplicate child, duplicate slot, cycle rejection, and immediate-only behavior;
- detach success and missing-child no-op behavior;
- root destroy rejection, leaf destruction, and child-bearing destroy rejection;
- descriptor replacement of kind/logic references;
- resolver validation of mutated documents.

## 9. Boundary checks

Boundary checks verify project reference direction and scan `Aurelian.World`, `Aurelian.Actuation`, and their tests for forbidden ECS manager/processor, service-locator, blackboard, Dominatus, render, shader, asset, reflection, CodeReferences, Stride, Machina, WyrmCoil, and Copeland references.

The broad boundary regex reports one pre-existing documentation-like boundary test method name, `WorldDocument_DoesNotRequireRendererAssetsOrDominatus`, in A13 world tests; that match is not a dependency or forbidden integration.

## 10. Validation results

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "ProjectReference" src/Aurelian.World src/Aurelian.Actuation -g '*.csproj'
rg -n "EntityManager|Processor|SystemBase|ServiceLocator|Blackboard|BbKey|AiWorld|Dominatus|GraphicsDevice|Render|Shader|Asset|Reflection|Activator|GetType\(|Type\." src/Aurelian.World src/Aurelian.Actuation tests/Aurelian.World.Tests tests/Aurelian.Actuation.Tests -g '*.cs' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.World src/Aurelian.Actuation tests/Aurelian.World.Tests tests/Aurelian.Actuation.Tests -g '*.cs' -g '*.csproj' || true
git status --short
```

Results:

- Build passed.
- Tests passed.
- Project reference direction is `Aurelian.Actuation -> Aurelian.World`; no reverse actuation reference was added.
- Boundary scans found no forbidden production dependencies.

## 11. Deferred features

Deferred features remain:

- typed world data stores;
- behavior runtime;
- Dominatus world observation bridge;
- blackboards as logic/runtime state;
- renderer integration;
- render snapshot contracts;
- asset integration;
- shader integration;
- physics;
- editor workflows;
- LLM calls;
- deep inheritance runtime;
- full ECS/processor/entity-manager architecture.

## 12. Next recommendation

Recommended next milestone:

```text
A15 — World typed data stores M0
```

Typed mutation contracts now exist, so the next locality risk is data richness. A15 should add typed local data-store contracts before Dominatus observation or render snapshot bridge work so downstream systems observe structured world data rather than inventing ad hoc state carriers.
