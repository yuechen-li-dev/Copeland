# TinyFarm agent authoring tooling pass

Outcome: **Success for the authoring pass**. Reusable typed templates now instantiate independent character and object agents through TinyFarm's real state, Dominatus controller, projection and generated persistence paths. This is a bounded authoring substrate, not a claim that all historical player-specific gameplay has already been generalized.

## Audit and design

FishTank's `FishFactory` demonstrates reusable brain construction and per-instance initialization. TinyTown's `TownieProfile`, generated `Define().CreateBrain()` and `SeedTownie` demonstrate profile/instance separation. Those sibling sources were read as references; no sibling source was changed.

TinyFarm already has shared stable `ActorId` identity, actor-owned unique inventory, actor-keyed product stacks, placement and energy. It already lowers human/Dominatus/replay intents through the same resolver. Its schedules can address arbitrary actor IDs, but setup was scattered across several arrays and switches, and both decision and frame projection assumed every non-player actor had a schedule. Player HP/combat still live in slice state and player equipment still has its existing world-level owner.

The new ownership law is:

`Reusable template -> instance declaration -> validated world materialization -> controller intents -> existing resolver -> presentation/inspection`

The declaration is authoring input. It does not become another inventory or position authority. Core state remains authoritative; Dominatus brains own decision execution. Passive brains contain no copies of gameplay state in their blackboards.

## Delivered

- `TinyFarmAgentTemplate.Character` and `.Object` provide readable defaults and ordinary C# `with` overrides.
- `.Spawn` provides a stable ID, name and semantic placement. Local item keys produce per-instance item IDs and consistent ownership; product stacks use existing definitions.
- `TinyFarmAgentAuthoring.Build` compiles daily routines into existing schedule windows, fingerprints the authored cast, and returns matching definitions/state together. The lower-level `Compile` materializes against a caller-owned catalogue.
- Shared authored state carries optional health, level, conditions, loadout, object pose and appearance. Instance identity, inventory and position stay in their existing owners.
- Idle characters and objects share a source-generated Dominatus flow, with one persistent brain per actor. Scheduled characters reuse the existing navigation/schedule path. Human control remains the existing host binding.
- The opening cast now uses `PlaceExisting` declarations instead of separate semantic-location and spatial-placement switches. It keeps legacy actor representation and does not manufacture duplicate HP or equipment.
- Generic inventory inspection accepts an actor ID, so a character or object uses the same row projection. Item transfer clears equipment when an authored agent loses ownership.
- Slot capability belongs to the identity item, separately from its initial equipped flag, and survives transfer. A recipient can equip it through the existing slot reducer; custom tool/weapon action behavior is not invented by its slot category.
- Native presentation consumes an authored sprite/scale binding. It no longer applies the player's hurt tint to every Gardener-style NPC. Passive objects are excluded from conversations and energy decay.
- `TinyFarmAgentInspector` provides a structured, generated-JSON inspection of actual owners, with named enum values and a visible legacy-adapter flag.

The native development fixture adds Ivy with an independently owned hoe, level and condition, plus a supply-cache object owning three seeds. It reuses approved Gardener art and the existing well sprite as a static object marker. These instances are not added to normal campaign content. `Play-TinyFarm.cmd --agent-authoring` opens that fixture with a separate development save directory.

The practical authoring guide is `docs/research/tinyfarm-agent-authoring.md`; the executable sample is `Games/TinyFarm/TinyFarm.Runtime/TinyFarmAgentExamples.cs`. Another instance requires a template/spawn declaration rather than changes to renderer identity switches, inventory arrays, energy rows and placement arrays.

## Validation and compatibility

Compilation returns a new indexed state only after validation and never mutates the supplied source. Diagnostics identify the offending instance. Validation covers identity collisions, local-item keys, product references/counts, equipment slots/ownership, health/level/conditions, control/object-pose values and scene placement. Schedule compilation checks every minute of the current supported seven-day horizon; a gap fails at authoring time with the exact instance and minute.

New authored actor data uses save version 13 (`tiny-farm-agents@13`) through the existing generated chunk codec. Existing worlds and save versions retain their adapters. Player equipment edits preserve later versions rather than reverting 13 to 12. New agent appearance is excluded from its semantic hash. Authored definition identity includes the declaration fingerprint; changing authored content still deliberately changes content identity.

Final qualification:

| Check | Result |
| --- | --- |
| `dotnet build TinyFarm.slnx -c Release -m:1 -nodeReuse:false -p:JsonSerializerIsReflectionEnabledByDefault=false` | Passed, 0 warnings/errors |
| `dotnet test TinyFarm.slnx -c Release -m:1 -nodeReuse:false` | 380 TinyFarm + 27 Spatial2D tests passed |
| Authoring + inventory menu tests with JSON reflection disabled | 18 passed, included in the totals above |
| Runner `--agent-authoring` | Exit 0, generated inspection/save proof |
| Real native Vulkan `--agents-proof` | Exit 0, both instances activated/projected, world and inventory captures, application checkpoint save/load |
| Native diagnostics | Empty stderr, no Vulkan validation output |

Seven new authoring tests exercise independent instances and template isolation, generic bag/equipment projection, object and character controller activation, generated save/load, declaration-order determinism, legacy placement/hash preservation, atomic validation failures, new-ID scheduled navigation and missing coverage, generic item transfer, generated replay and presentation-only scale/hash isolation. Focused repeats are not counted as additional tests. Historical full suites retain their existing reflection defaults for older milestone evidence writers; the new runtime proof and focused paths run with reflection serialization disabled.

Artifacts under `artifacts/tinyfarm-agent-authoring` contain `authoring-proof.json`, `native-proof.json`, two actual 1920x1080 Vulkan PNG captures and `manifest.json` with validation, hashes and qualification boundaries. The native fixture activates two passive Dominatus agents; the inspection also contains the four existing actors. The application proof saves the cast, changes the player loadout, then loads and verifies restored equipment, authored character state, object inventory and version 13. The headless and native final hashes differ because the native host advances slice ticks before capture; each proof checks its own path rather than asserting those are identical frames.

## Explicit boundaries and next pressure

No ECS, new menu framework, parallel save format, reflection registry, art regeneration or campaign expansion was added.

The legacy adapters are deliberate: the existing Gardener's HP/combat and player loadout remain in their current authoritative owners. New authored HP, level and condition data persist, but this pass does not add generalized NPC damage, levelling or condition-effect mechanics. Conversation-sprite metadata is not new portrait/dialogue content. Object pose persists but the marker is static; container opening, loot UI, pose animation and blocking footprints remain unimplemented. The host rejects a second Human spawn rather than pretending possession or multiplayer already works.

The next architectural pressure is **controlled-agent ownership and action-state migration**: route input/camera/agent menus by a control binding, then move the remaining player-specific action state under the common agent owner with explicit legacy-save adapters. The authoring API can remain stable while that migration proceeds. That is a separate capability pass, not an unreported workaround inside this one.

Code scope against `bf4b93d4679299ee08f407023c648357e90ca64d`: 15 modified and 7 new source/test files. The evidence manifest records insertions/deletions separately from the two documentation files and generated artifacts.
