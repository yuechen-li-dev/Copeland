# A12 Audit — World Model Doctrine

## 1. Files changed

- `docs/architecture/world-model-doctrine.md`
- `docs/architecture/mvp-roadmap.md`
- `README.md`
- `docs/audits/0012-a12-world-model-doctrine.md`

## 2. Task scope

A12 is a docs-only architecture milestone. The task defines the Aurelian world/scene/component/actuator model before `Aurelian.World` implementation begins.

No production source, tests, project files, vendored Dominatus source, or `CodeReferences` material were modified.

## 3. Doctrine summary

The doctrine establishes locality of change as Aurelian's primary design law. It defines the world model around explicit data, composition, and logic boundaries; treats components as reusable local composition units; introduces `WorldUnit` as the conceptual locality boundary; and defines how world state relates to Dominatus orchestration, actuators, render extraction, assets, and shaders.

## 4. Key Stri-V lessons captured

The doctrine captures these Stri-V lessons:

- hidden global state destroys locality;
- nullable lifecycle semantics make every consumer duplicate lifecycle policy;
- callback-order behavior is fragile architecture;
- manager/processor objects easily become policy knots;
- renderer, asset, shader, world, and behavior responsibilities must remain separated.

## 5. Data/composition/logic model

The doctrine defines a universal decomposition:

- Data: what something is;
- Composition: what it is made of;
- Logic: what it does.

This model is applied uniformly to worlds, scenes, rooms, NPCs, players, documents, webpages, UI nodes, shader modules, and asset manifests.

## 6. Component and WorldUnit definitions

The doctrine defines an Aurelian component as a reusable local composition unit. A component should contain or declare data shape, child composition, local logic surface, inputs, and outputs.

The doctrine introduces `WorldUnit` as the conceptual locality boundary for Aurelian world implementation. Parents compose children without inspecting or mutating child internals, children affect the outside world only through declared outputs, parents affect children only through declared inputs, and boundary crossings should be typed.

## 7. Dominatus/actuator/render/assets relationship

The doctrine assigns responsibilities as follows:

- Dominatus owns policy/orchestration and emits acts.
- World owns data, composition, resolved documents, snapshots, hierarchy state, and typed queries.
- Actuators own mutation boundaries and return typed outcomes.
- Render extraction converts world data into render snapshots, command plans, and backend execution.
- Asset manifests and shader sources describe source-of-intent and build deterministic artifacts.

The doctrine keeps Dominatus from becoming the data store and keeps the world from becoming the behavior policy engine.

## 8. Anti-goals

A12 explicitly rejects:

- Stride ECS recreation;
- `EntityManager` as a core policy knot;
- processors as the core runtime abstraction;
- global service locators;
- null lifecycle state;
- children mutating unrelated state;
- parents inspecting child internals;
- renderer dependencies in world;
- editor-first design.

## 9. A13 recommendation

Next recommendation:

```text
A13 — World Unit M0
```

A13 should implement the smallest possible world model:

- IDs;
- descriptors;
- composition;
- resolver;
- snapshot;
- tests.

A13 should not include behavior runtime, renderer integration, asset integration, physics, LLM calls, full ECS, processors, entity manager, or deep inheritance.

## 10. Validation results

Docs-only validation commands:

```bash
test -f docs/architecture/world-model-doctrine.md
test -f docs/audits/0012-a12-world-model-doctrine.md
git status --short
```

The required files exist. `git status --short` shows only the expected docs changes for this milestone before commit.
