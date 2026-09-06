# RenC# visual-novel profile

RenC# is currently the small application profile proven by SUNKILL. It is not a
separate engine and it is not a promise to implement Ren'Py feature parity.

## Ownership

| Concern | Owner | Current RenC# use |
| --- | --- | --- |
| Dialogue program | Ariadne | `SunkillDialogue` uses the high-level C# dialogue API and lowers to ordinary OptFlow/HFSM states. |
| Flow lifecycle and checkpoints | Dominatus | `VnSession` runs the lowered brain and captures/restores its semantic checkpoint. |
| Durable save container | Deliverance | `VnPersistence` writes three fixed `.dlv` slots and returns a candidate before application commit. |
| Physical input mapping | InputMan | `RenControls` maps keys to logical actions; `RenApp` lowers those actions to typed VN intents. |
| Semantic UI | Machina.UI | `VnMachinaLayer` authors title, menu, settings, slot, choice, and dialogue surfaces. |
| Composition and native realization | Aurelian | Background, portrait, and Machina UI are composed in stable semantic order and submitted by the native Vulkan compositor. |
| VN application policy | RenC# app layer | `RenApp` owns screens, routing, selection, settings, save/load commit, quit request, and presentation snapshot policy. |
| Story and assets | SUNKILL | `SunkillDialogue.cs` and the two `Assets/sunkill-*.png` files contain product content. |

## Explicit app state and routing

`RenScreen` is the navigation state: `MainMenu`, `Game`, `PauseMenu`, `SaveMenu`,
`LoadMenu`, `Settings`, and `End`. `RenAppState` also projects validated settings,
three save-slot metadata records, selected item, notice, and `ExitRequested`.
There is no family of overlapping menu booleans.

Routing is centralized in `RenApp.Dispatch(RenIntent)`. Examples:

| State + intent | Result |
| --- | --- |
| `MainMenu + NewGameIntent` | clean `VnSession`, then `Game` |
| `Game + BackIntent` | `PauseMenu` |
| `Game + OpenSaveMenuIntent` | `SaveMenu` |
| `LoadMenu + LoadSlotIntent` | candidate restore, then `Game` |
| `End + ReturnToMainMenuIntent` | session disposal, then `MainMenu` |
| `MainMenu + QuitIntent` | `ExitRequested`; the host performs shutdown |

## Dialogue and presentation

`SunkillDialogue` is the only story definition. Its meaningful choice emits an
application-owned `SunkillConsequence`, and the consequence handler writes the
semantic Dawn Protocol facts. `DialoguePresentationProjector` reconstructs the
pending line or choice from the authoritative Dominatus state. `RenPresentationSnapshot`
adds only renderer-neutral screen, asset, menu, selection, settings, slot, and
notice facts.

The render order is:

1. `world-background`
2. `portrait`
3. `machina-vn-ui`

MachinaCanvas is not referenced by the runtime. The current established
Machina/Aurelian bridge rasterizes the semantic UI surface to an Aurelian texture;
the complete three-layer frame is composed and captured through the native Vulkan
path. There is not yet a direct native Machina text/control presenter.

## Save, load, and settings

Each Deliverance slot contains an explicit `RenGameSave` envelope with application
save version, scene identity, pending operation metadata, timestamp, and the
Dominatus-backed `VnSessionCheckpoint`. Renderer buffers, input state, hover,
audio device state, and presentation pixels are excluded.

Load follows one law:

`Deliverance candidate -> app validation -> authoritative checkpoint restore -> presentation rebuild`

The candidate session replaces the live session only after successful validation.
This prevents a corrupt slot from damaging live state. Restored pending operations
are attached to the dialogue surface without re-dispatch, so committed typed
effects are not replayed.

`RenSettings` currently contains three real, clamped Aurelian bus gains: master,
music, and SFX. A source-generated JSON context persists settings separately from
game saves. Malformed settings fall back to defaults. Text reveal speed is deferred
because dialogue currently appears instantly and adding a typewriter controller is
its own bounded product change.

## Deliberately absent

M13 does not contain history/backlog, auto, skip, rollback, transitions, portrait
expressions, layered characters, voice, gallery, achievements, chapter selection,
localization, a VN scripting DSL, Copeland authoring, generalized content pipelines,
animated titles, video, cloud saves, or modding.
