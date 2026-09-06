# RENC#-VN-M13 — SUNKILL minimal native VN report

## 1. Outcome

**Outcome A — the real minimal VN shell works.**

The default command opens a native 1280×720 SUNKILL window at a usable title
screen. New Game, the short dialogue, its choice, three save slots, load,
settings, the end/return path, and clean quit all execute through their real
owners. The proof runner captured the native output and checked the semantic
laws described below.

Launch:

```powershell
.\Play-Sunkill.cmd
```

Qualification:

```powershell
dotnet run --project samples/Integrations/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj -- --proof
dotnet test tests/Integrations/Sunkill.Tests/Sunkill.Tests.csproj
```

## 2. Existing stack audit

| Concern | Existing owner | Reuse as-is? | M13 work |
| --- | --- | --- | --- |
| High-level dialogue authoring | Ariadne | Yes | `SunkillDialogue` uses `Dialogue.Define` and `DialogueLowerer`; no second dialogue runtime was added. |
| Pending dialogue projection | `DialoguePresentationSnapshot` / Ariadne presentation | Yes | `RenPresentationSnapshot` wraps the existing dialogue projection with app-screen facts. |
| M7b VN proof | Aurelian/Ariadne integration sample | Yes | The existing qualified sample became the bounded SUNKILL product rather than being duplicated. |
| M7b2 second consumer | TinyFarm | Yes, as architectural evidence | M13 retains renderer-neutral dialogue projection and application-owned meaning. |
| Flow and checkpoint lifecycle | Dominatus | Yes | `VnSession` captures/restores the authoritative checkpoint and reattaches pending presentation without dispatch. |
| Save container and compatibility | Deliverance | Yes | Added a small app envelope and three fixed file-backed slots. |
| Physical input mapping | InputMan | Yes | Added a SUNKILL action map and lowering to typed `RenIntent` values. |
| Semantic UI | Machina.UI | Yes | Added title, menu, slot, settings, dialogue, and choice documents. |
| Composition/native output | Aurelian native composition | Yes | Reused ordered semantic layers and the Vulkan compositor/readback path. |
| Images | Existing Aurelian sprite-resource path | Yes | Added two normalized original PNG assets. |
| MachinaCanvas | Tooling only | Not needed | There is no runtime or build dependency on MachinaCanvas. |

No qualified engine feature was reimplemented in the app layer.

## 3. Final app state model

`RenAppState` contains:

- one explicit `RenScreen` value;
- validated `RenSettings`;
- the three projected `RenSaveSlotMetadata` entries;
- selected item;
- `ExitRequested`;
- a short user-facing notice.

`RenScreen` is `MainMenu`, `Game`, `PauseMenu`, `SaveMenu`, `LoadMenu`,
`Settings`, or `End`. The optional authoritative `VnSession` belongs to
`RenApp`; overlapping navigation booleans were not introduced.

## 4. Screen routing model

All routing is centralized in `RenApp.Dispatch(RenIntent)`:

| Current state + intent | Result |
| --- | --- |
| `MainMenu + NewGameIntent` | Dispose an old session, create a clean session, enter `Game` |
| `MainMenu + OpenLoadMenuIntent` | `LoadMenu` |
| `MainMenu + OpenSettingsIntent` | `Settings` |
| `Game + BackIntent` | `PauseMenu` |
| `Game + OpenSaveMenuIntent` | `SaveMenu` |
| `LoadMenu + LoadSlotIntent` | Validate a candidate, replace live session, enter `Game` |
| `End + ReturnToMainMenuIntent` | Dispose/clear the session, enter `MainMenu` |
| `MainMenu + QuitIntent` | Set `ExitRequested`; the host closes the window |

Back navigation retains only the explicit screen to return to. Returning to the
title clears dialogue and portrait state because no active session remains.

## 5. Main menu

The native title screen displays **SUNKILL**, “NIGHT HAD A GOOD RUN.”, New
Game, Load, Settings, and Quit. Selection is visible. Keyboard and mouse are
qualified; the current presenter has no existing gamepad host mapping, so M13
does not add a platform-specific one.

## 6. Settings

The settings screen has three real values: master, music, and SFX volume.
Values move in 0.1 steps, normalize to finite values in `[0, 1]`, persist on
change, and fall back to defaults for malformed JSON. Settings are stored
separately from game saves with a source-generated `System.Text.Json` context.

Text speed is deliberately absent: text currently appears instantly, and a
correct reveal controller is a separate bounded feature.

## 7. Save/load

There are three fixed Deliverance `.dlv` slots. Save overwrites the chosen slot.
Each menu entry projects availability, a compact pending-operation label, and a
UTC timestamp. Load distinguishes empty and corrupt slots. A failed candidate
never replaces the current live session.

Interactive saves and settings live under `%LOCALAPPDATA%\SUNKILL`; the proof
uses repository-local isolated paths.

## 8. Deliverance save schema

The required module is `renc.sunkill.session`, schema version 1. Its explicit
`RenGameSave` payload contains:

| Field | Meaning |
| --- | --- |
| `ApplicationSaveVersion` | Application semantic save version |
| `ActiveScene` | `sunkill.dawn-engine` |
| `PendingOperation` | Exact pending line/choice, or terminal marker |
| `SavedAtUtc` | Slot metadata timestamp |
| `Session` | `VnSessionCheckpoint`, including Dominatus/dialogue and semantic blackboard state |

The Deliverance application metadata also fixes application ID, build ID, and
definition hash. GPU resources, pixels, hover/capture, physical input, and audio
device state are not saved.

## 9. Restore semantics

The implemented law is:

`Deliverance candidate -> application validation -> Dominatus checkpoint restore -> presentation rebuild`

`VnSession.Restore` rebuilds the semantic world, restores the checkpoint, and
reattaches the dialogue surface to the already-pending operation. It does not
redispatch that operation. The app commits the candidate session only after the
whole load succeeds.

## 10. Dialogue definition

`SunkillDialogue` is a compact high-level Ariadne definition with five beats
before the choice, one result beat per branch, a converged Oppenheimer line, and
one ending narration. It uses the movie-associated historical names J. Robert
Oppenheimer, General Leslie Groves, and Lewis Strauss in an unmistakably absurd
alternate-history vampire parody. The art is original and does not imitate an
actor or movie still.

## 11. Choice/effect proof

The choice offers:

- open the shutters now;
- wait for Strauss.

The branches have different dialogue and emit an app-owned typed
`SunkillConsequence`. The handler writes `DawnProtocol`,
`DawnEngineTested`, and `StraussWaitedFor` facts to the authoritative
blackboard. Both branches were tested through completion.

## 12. Presentation snapshot

`RenPresentationSnapshot` is renderer-neutral. It carries the screen, title,
subtitle, menu entries, selection, Ariadne dialogue snapshot, background and
portrait asset keys, settings, save-slot metadata, and notice. It contains no
Machina nodes, renderer handles, texture state, or pixels.

## 13. Input intent model

Input follows:

`physical control -> InputMan logical action -> typed RenIntent -> RenApp reducer`

The action map covers navigation, confirm, back, quick-save, and quick-load.
Game operations use specific types including `AdvanceDialogueIntent`,
`ChooseDialogueOptionIntent`, `SaveSlotIntent`, and `LoadSlotIntent`. Machina
pointer actions lower at the app boundary; save/load/routing logic is not coded
into key handlers.

## 14. Native composition

The stable Aurelian order is:

1. `world-background`
2. `portrait`
3. `machina-vn-ui`

Images and the Machina surface are submitted as native Vulkan textured quads,
and screenshots come from the native compositor result. The established M7b
bridge still rasterizes Machina's semantic UI to a texture before Vulkan
composition because no direct native Machina text/control presenter exists.
This is recorded plainly rather than claiming an unavailable backend. Static
atlases and the UI raster are cached until their semantic input changes.

## 15. Audio/settings integration

`RenAudioSettingsProjection` applies the three validated values to the actual
Aurelian.Audio Master, Music, and SFX buses. The zero-SFX case is proven. The
demo intentionally ships no audio content, so it uses the null output backend;
bus behavior and persistence are real while device playback is out of scope.

## 16. Runtime screenshots

All six captures were produced by the proof runner through `VnNativeRenderer`:

- `artifacts/renc-vn-m13/main-menu.png`
- `artifacts/renc-vn-m13/settings.png`
- `artifacts/renc-vn-m13/scene-line.png`
- `artifacts/renc-vn-m13/scene-choice.png`
- `artifacts/renc-vn-m13/save-menu.png`
- `artifacts/renc-vn-m13/load-menu.png`

The choice capture shows the original stylized Oppenheimer portrait over the
original desert-bunker background. Mouse press/release also proves Machina focus
and pointer capture ownership.

## 17. Save-after-line proof

Slot 1 restores operation
`dialogue.sunkill.dawn-engine.line.intro` exactly. The restored session reports
zero dialogue redispatches.

## 18. Pending-choice restore proof

Slot 2 restores `dialogue.sunkill.dawn-engine.choice.protocol` with selected
index 1 (“wait for Strauss”) and zero consequence emissions. The pending UI is
rebuilt from the restored semantic operation.

## 19. Post-effect restore proof

Slot 3 is captured after `ImmediateShutter` commits. It restores the exact
`open-result` line with `DawnEngineTested = true` and zero new consequence
emissions. The already-committed effect does not replay.

## 20. Settings restart proof

The proof changes all three values, drives SFX to zero, disposes the app, and
creates a new app from the same settings path. The values and Aurelian bus gains
match after restart. A malformed settings file separately falls back to
`0.80 / 0.65 / 0.80`.

## 21. Quit proof

`QuitIntent` sets `RenApp.ExitRequested`. `VnPresenter` observes that fact and
closes its window; UI code never terminates the process. A real interactive
smoke test kept the default product window responsive at
`SUNKILL — MainMenu — READY` and then closed it through the window host.

## 22. Replay/determinism proof

Two fresh sessions received the same semantic trace: advance until the protocol
choice, choose `open-shutters`, then advance until terminal. Both produced:

`7d8272b00185b88455587ad91b84bceed860247cc39f9c8ed5f4bad54f0d4cf1`

The trace is semantic rather than coupled to a hard-coded dialogue-beat count.

## 23. Performance sanity

The final 12-frame native-compositor/readback sample averaged **29.75 ms** and
had a **104.90 ms** worst sample on this machine. This includes synchronous
1280×720 proof readback and is not an interactive frame-budget benchmark. A
local presentation cache removed repeated image decoding and unchanged Machina
rasterization; the pre-cache proof averaged about 132 ms. No engine performance
framework was added.

## 24. Fresh product-edit proof

A fresh-context agent was asked to change the title subtitle and add one setting.
It located the narrow edit set without being told implementation files:

- `samples/Integrations/Aurelian.Ariadne.VnDemo/RenApp.cs`
- `samples/Integrations/Aurelian.Ariadne.VnDemo/RenSettings.cs`
- `tests/Integrations/Sunkill.Tests/SunkillM13Tests.cs`
- optionally `samples/Integrations/Aurelian.Ariadne.VnDemo/Program.cs` for proof evidence

It found that `VnMachinaLayer` already projects menu-entry labels and therefore
did not require generic renderer, persistence, InputMan, Ariadne, or Dominatus
changes. This was a read-only locality probe; no arbitrary probe feature was
retained in the product.

## 25. Fresh content-edit proof

A separate fresh-context agent was asked to add two beats before the choice. It
identified `SunkillDialogue.cs` as the only required production edit, with an
optional content-test update. It also caught an initial proof-runner coupling to
five `Advance` calls. That coupling was removed: qualification now advances
until the semantic choice operation with a guard. No renderer, persistence,
InputMan, Deliverance, Ariadne, or Dominatus internals are needed for this edit.

## 26. Owner-lane fixes

No engine owner-lane defect was required. All engine projects were reused as-is.
The only hot-path correction is app-local: cache normalized background/portrait
resources and the unchanged Machina overlay. It does not change semantic
ownership or introduce another presenter.

## 27. Deferred systems

History/backlog, auto, skip, rollback, transitions, portrait expressions,
layered characters, voice, gallery, achievements, chapter select, localization,
a VN scripting DSL, Copeland authoring, generalized content pipelines, animated
titles, video, cloud saves, and modding remain explicitly deferred.

Fullscreen/windowed and gamepad input are also deferred because the present host
does not expose those as an already-clean app seam. Text reveal is the one
observed product pressure called out for the next milestone.

## 28. Exact M14 recommendation

**M14: add one deterministic typewriter/reveal controller and a persisted text
speed setting.** Keep completed text semantic, reveal position transient and
reconstructible, let confirm reveal the current line before it advances, and
wire only this new setting into the existing app snapshot. Do not combine M14
with history, skip, auto, transitions, or expression work.

## 29. Diff stat

The tracked baseline diff is currently 11 files, **914 insertions and 679
deletions**. M13 additionally adds the launcher, profile/report, three focused
app/content files, a two-file test project, two PNG assets, and the generated
qualification directory. The removal is the superseded M7b
`VnDialogueDefinition.cs`; its reusable behavior moved into the explicit
SUNKILL/Ariadne definition and app session rather than leaving two content paths.

The generated artifact directory contains the six required PNGs, four required
JSON proofs/manifest files, two optional JSON proofs, isolated settings, and three
Deliverance slots with their single retained backups.

## Validation

- Focused product build: succeeded with 0 warnings and 0 errors.
- Focused SUNKILL tests: 10 passed.
- Full `Aurelian.slnx` test run: 762 passed, 0 failed.
- Default executable: responsive native window smoke-tested.
- Proof runner: Outcome A and all required artifacts regenerated successfully.

## Launch hardening follow-up

The first interactive save exposed a startup defect that the original
process-exists smoke did not catch. Slot metadata was read with a synchronous
wait over an async continuation on Avalonia's UI synchronization context. With
an existing `.dlv` file, startup could deadlock before the window acquired a
handle. After that was isolated, runtime DXC compilation could also remain in a
subprocess wait when invoked directly from the Avalonia UI apartment.

The repair is bounded to those owner seams:

- persistence awaits explicitly avoid capturing the UI context;
- shader compilation runs away from the UI apartment;
- DXC drains stdout/stderr concurrently, creates no console window, has a
  15-second deadline, and kills its complete process tree on timeout;
- the presenter creates a visible `SUNKILL — STARTING` shell before product
  initialization and shows a readable startup error if initialization fails;
- `Play-Sunkill.cmd` builds with one non-reusable node and launches the built
  apphost directly instead of nesting `dotnet run`;
- `tools/Test-SunkillLaunch.ps1` requires an opened window and rendered native
  frame, applies a 20-second deadline, captures both streams, kills a timed-out
  child tree, and rejects any newly leaked reusable MSBuild process.

The hardened launch test passed three consecutive runs and the launcher itself
passed the same `--launch-smoke` route. A focused regression additionally proves
that populated save-slot metadata does not post a continuation back to a UI-like
synchronization context.
