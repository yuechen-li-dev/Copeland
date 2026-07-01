# A13 Audit — World Unit M0

## 1. Files changed

- `src/Aurelian.World/Units/UnitId.cs`
- `src/Aurelian.World/Units/UnitKindId.cs`
- `src/Aurelian.World/Units/UnitLogicRef.cs`
- `src/Aurelian.World/Units/UnitChild.cs`
- `src/Aurelian.World/Units/UnitComposition.cs`
- `src/Aurelian.World/Units/WorldUnitDescriptor.cs`
- `src/Aurelian.World/Units/WorldDocument.cs`
- `src/Aurelian.World/Units/ResolvedWorld.cs`
- `src/Aurelian.World/Units/ResolvedWorldUnit.cs`
- `src/Aurelian.World/Units/WorldUnitResolver.cs`
- `src/Aurelian.World/Units/WorldResolutionResult.cs`
- `src/Aurelian.World/Units/WorldResolutionDiagnostic.cs`
- `src/Aurelian.World/Units/WorldResolutionDiagnosticSeverity.cs`
- `src/Aurelian.World/Units/WorldResolutionDiagnosticCodes.cs`
- `tests/Aurelian.World.Tests/WorldUnitM0Tests.cs`
- `README.md`
- `docs/architecture/mvp-roadmap.md`
- `docs/architecture/world-model-doctrine.md`
- `docs/audits/0013-a13-world-unit-m0.md`

## 2. Task scope

A13 implements World Unit M0 only. The milestone makes the first small world model slice real in `Aurelian.World.Units`: unit identity, unit kind identity, opaque logic references, immediate child composition, unit descriptors, authoring documents, resolved worlds, resolver diagnostics, and deterministic hierarchy queries.

The milestone does not implement a full ECS, processors, an entity manager, behavior runtime, Dominatus integration, actuators, renderer integration, asset integration, physics, editor workflows, LLM calls, deep inheritance runtime, or blackboards.

## 3. Implemented world unit model

The M0 model adds:

- `UnitId` as the world-unit identity value type;
- `UnitKindId` as a string kind identifier;
- `UnitLogicRef` as an opaque string reference to logic;
- `UnitChild` for immediate child references and optional slots;
- `UnitComposition` for immediate-only composition;
- `WorldUnitDescriptor` for authored units;
- `WorldDocument` for authoring/resolution input;
- `ResolvedWorldUnit` and `ResolvedWorld` for deterministic resolved hierarchy data;
- `WorldUnitResolver` and resolution diagnostics for validation and query materialization.

The existing `Aurelian.Core.EntityId` remains untouched as a legacy/core placeholder. A13 uses `UnitId` separately to align with the world doctrine's `WorldUnit` boundary.

## 4. Immediate composition rule

`UnitComposition.Children` declares immediate children only. Authoring descriptors do not list transitive descendants.

The tests use the hierarchy `OldWoman -> Woman + Elder`, `Woman -> Human`, and `Human -> Animal`. The `OldWoman` descriptor only lists `Woman` and `Elder`; `Human` and `Animal` are discovered through resolver traversal rather than duplicated in the authoring descriptor.

## 5. Resolver behavior

`WorldUnitResolver.Resolve` validates the input document and returns `WorldResolutionResult` rather than throwing for ordinary invalid world documents.

When the document is valid, the resolver produces a `ResolvedWorld` containing:

- the root id;
- resolved units reachable from the root;
- immediate children by unit id;
- parent lookup metadata;
- deterministic pre-order traversal.

Traversal preserves immediate child order from authoring composition. Transitive composition is computed from the resolved child map.

## 6. Diagnostics

A13 adds the following error diagnostic codes:

- `AW1001` / `RootUnitMissing`;
- `AW1002` / `ChildUnitMissing`;
- `AW1003` / `DuplicateImmediateChild`;
- `AW1004` / `DuplicateChildSlot`;
- `AW1005` / `CompositionCycle`.

A resolution result succeeds only when it has a non-null resolved world and no error diagnostics.

## 7. Query/snapshot behavior

M0 uses `ResolvedWorld` as the deterministic snapshot/query shape instead of adding a separate snapshot type. `ResolvedWorld` exposes:

- `GetImmediateChildren(UnitId)`;
- `GetTransitiveDescendants(UnitId)`;
- `TryGetParent(UnitId, out UnitId)`;
- `Contains(UnitId)`;
- `PreOrder` traversal.

This keeps M0 small while still providing the snapshot/query behavior needed by tests and downstream milestones.

## 8. Tests added

`tests/Aurelian.World.Tests/WorldUnitM0Tests.cs` adds coverage for:

- immediate-only descriptor composition;
- root and immediate child resolution;
- transitive descendant computation;
- deterministic pre-order traversal;
- missing child diagnostics;
- duplicate immediate child diagnostics;
- duplicate child slot diagnostics;
- cycle diagnostics;
- opaque logic references that are stored but not executed;
- constructing and resolving a world document without renderer, asset, or Dominatus dependencies.

## 9. Boundary checks

Boundary checks confirmed that `Aurelian.World` did not gain Dominatus, renderer, asset, shader, ECS-manager, processor, service-locator, blackboard, Stride, Machina, WyrmCoil, Copeland, or CodeReferences dependencies.

The first boundary regex reports one intentional test-method-name match: `WorldDocument_DoesNotRequireRendererAssetsOrDominatus`. The match is the required boundary test name, not a dependency or integration.

## 10. Validation results

Commands run:

```bash
dotnet build Aurelian.slnx -c Debug
dotnet test Aurelian.slnx -c Debug
rg -n "EntityManager|Processor|SystemBase|ServiceLocator|Blackboard|BbKey|AiWorld|Dominatus|GraphicsDevice|Render|Shader|Asset" src/Aurelian.World tests/Aurelian.World.Tests -g '*.cs' || true
rg -n "CodeReferences|Stride\.|Machina\.|WyrmCoil|Copeland" src/Aurelian.World tests/Aurelian.World.Tests -g '*.cs' -g '*.csproj' || true
git status --short
```

Results:

- Build passed with zero warnings and zero errors.
- Tests passed.
- Boundary regex found only the required boundary-test method name in world tests.
- The CodeReferences/Stride/Machina/WyrmCoil/Copeland boundary regex returned no matches.

## 11. Deferred features

Deferred features remain:

- typed world data stores;
- behavior runtime;
- Dominatus world observation bridge;
- actuator request/result contracts;
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
A14 — Actuator request/result M0 over WorldUnit
```

Mutation boundaries should be explicit before Aurelian adds richer typed stores, Dominatus observation bridges, or render snapshot extraction. A14 should define small typed request/result contracts over `WorldUnit` identities and resolved world boundaries without introducing behavior runtime, renderer integration, assets, physics, blackboards, or global managers.
