# AURELIAN-FULL-GAME-SLICE-M9

**Outcome A: a small, completable native game.**

## Game pitch

**TinyFarm: A Little Mint of Kindness** is about making a place feel like home through a few ordinary acts of care. Mara is organizing supper. Plant a turnip for tomorrow, gather river mushrooms and cook them in Hearth House, discourage the slime in Old Burrow, and bring Mara the mint beside the farm plots. The reward is a warm thank-you and a place at the table. There is no countdown, player damage, or grinding. Estimate 5–10 minutes for a first visit; this is not a measured unfamiliar-player study.

## Practical audit

There was no combined native TinyFarm game to launch. The interactive client was MonoGame, while native world, compositor, host/input, and effects examples were separate proofs. I ran the existing client at 1280x720 for its three-frame composition smoke and inspected its real controllers/content. It reported one UI topology/layout build. Native subsystem examples were rerun during qualification.

| Classification | Gap | M9 response |
| --- | --- | --- |
| Must fix for game | No integrated native interactive client | Added `TinyFarm.Native`, using Aurelian host/compositor |
| Must fix for game | Milestone start state, no objective or ending | Authored farm start, title, persistent journal, supper completion |
| Must fix for game | Controls require prior knowledge | Controls on screen, facing-sensitive prompts, doorway signs |
| Must fix for game | Technical saves lack native player UX | One Deliverance slot; F/N and success/error messages |
| Must fix for game | House exit returns to the distant farm gate | Corrected authored target to `farm.start` |
| Must fix for game | Mara and Elias overlap at the river | Separate authored river bench for Elias |
| Engine seam | Animated materials exhaust 4,096 Vulkan bindings | Correct-owner cache reclamation and native regression |
| Polish worth doing | Tiny text, anonymous portals, rectangle scenery | Larger Machina text, signs, roof, fence, well, stalls, beds, portrait |
| Defer | Public packaging, physical gamepad feed, native presentation optimization | M10 release work |

Available mechanics already included movement/collision, pickup, stack inventory, hotbar, farming, foraging, cooking, chopping, sword/slime combat, portals, NPC scheduling, dialogue, saves, and replay. Immediate pickup/tool consequences were promising; the missing ingredient was a reason to connect them. Existing TSON scenes, resolver verbs, Ariadne coordinator, Deliverance bridge, projectors, and Mara portrait were reused.

## What the player does

The title states the premise. Enter starts, N continues a save, and Q quits. The player begins outside the farmhouse at 11:30 with seeds, an axe, and a sword. The note and journal explain the jobs immediately; Mara provides the opening conversation. Jobs can be completed in a convenient order.

The qualified flow is farm → mint pickup → selected-slot planting → opening conversation in town → river mushrooms → noon schedule change → Old Burrow and selected-sword hit → Hearth House cooking → return to Mara at the river → conditional give/keep choice → typed completion → ending → save/continue.

The resolver checks the supper request, mint ownership, cooked mushroom dish, persistent planted-seed fact, defeated slime, and Mara's presence/range. `GiveMaraWildMintConsequence` lowers to `CompleteSupperIntent` for this scenario. Mint transfer and completion are atomic; unfinished and repeated turn-ins reject. Later harvesting cannot erase planting credit. The closing conversation leads to **SUPPER IS READY**, with save, continue, and quit available.

There is no losing state. The one-hit slime is a tiny tool encounter, without aggression or damage. Optional chopping yields firewood; watering, later harvesting, the shop, and other NPCs remain available. No grinding or real-time crop growth is required to finish.

## Controls and onboarding

| Input | Action |
| --- | --- |
| WASD | Move and face objects |
| E | Nearest facing interaction: talk, pickup, forage, cook, tend, doorway |
| 1 / 2 / 3 / 4 | Seeds / turnip / axe / sword |
| Space | Use selected binding |
| I | Pockets; I, Escape, or Enter closes |
| Escape | Pause/resume or leave dialogue |
| Space / Enter; Up / Down | Dialogue advance; choose reply |
| F / N | Save / load |
| Q | Quit from a menu or ending |

All controls enter InputMan logical maps. F/N were chosen because the portable key enum lacks function keys. Dialogue/menu maps exclude Gameplay. Focus loss clears held input. Loading clears stale physical input and transient SFX. The visible-window smoke exercised native key messages through WinForms callbacks, InputMan, and the host: title, Enter, movement, pause, and captured attack passed.

Logical gamepad mappings cover stick movement, South interaction, West tool, D-pad slots, North pockets, and Start pause. The Windows surface does not yet collect physical gamepad events; hardware controller support is not claimed.

## World, NPCs, and narrative

The small existing places remain: Farm, connecting trail, Town, Riverside, Hearth House, General Store, and Old Burrow. The current scene fits a fixed 1280x720 view; scale/origin update on scene changes. Portals use authoritative target anchors, and collisions follow authored objects. Doorway labels distinguish destinations. The farmhouse, rails, well, market, river, beds, mushrooms, mint, actors, and slime have distinct visual treatments.

Mara is in town before noon and at the river afterward. The journal names her current scene. Elias has his own river rendezvous. M5 cadence/schedule/navigation mechanics remain in use through the existing host/session. Human input remains a peer controller; ordinary player actions do not pass through an AI abstraction.

Mara has an opening premise, unfinished-supper reminder, ready branch, thanks, and post-completion greeting. Her humor is dry: supper traditionally requires “one fewer slime,” and turnips are a difficult audience. Dialogue uses high-level `Diag.Line`, `Diag.Choose`, and a typed consequence. No raw OptFlow conversation authoring was added.

## Visuals, audio, and game feel

Machina owns the title, HUD, journal, prompts, dialogue, pockets, pause, and ending. Its existing deterministic bitmap presentation is realized by Aurelian's raster adapter and cached as a transparent native layer. Vulkan renders the world geometry and SoftShockwave. The Windows form displays the composed framebuffer and forwards input; it does not draw game text or decide gameplay. There is no MonoGame path in the final client.

The existing Mara portrait is a separate native texture behind the dialogue panel. Pickup/harvest sparkles and puffs, movement dust, ambient motes, and gold sword-hit feedback reuse M8. Combat visibly submits a `SoftShockwave.v.ts` quad through Copeland GPU profile → VD-MIR → backend → validated SPIR-V. Effect state is scoped to the accepted current scene and recreated on load. Rebuilding presentation does not emit gameplay effects or audio.

Resident authored PCM supplies pickup, harvest, sword, footsteps, UI confirmation, and a gentle eight-second melody through Aurelian.Audio/Windows NAudio. Music has higher voice priority than bursts. Feedback queues and runtime capacities are bounded, and diagnostics/completions are drained. Missing audio hardware falls back to silence; an absent optional portrait does not prevent play. Physical speaker output was not independently measured.

Existing movement speed/range and one-hit combat remain. The feel improvements are immediate planting credit, readable prompts/signs, larger text, feedback/status messages, and a clear ending. No new image pipeline, tutorial framework, or settings framework was added.

## Save/load and replay

F captures one Deliverance slot, including inventory, scene/enemy/crop/objective state, NPC placement/energy, world minute, selected slot, sequence, and active Ariadne checkpoint. Failed loads retain the old session and show an error. Normal saves live at `%LOCALAPPDATA%/TinyFarm/saves/supper.dlv`. N restores a validated candidate and clears old input/SFX. Completed saves remain complete.

Qualification saves during dialogue and after cooking mid-objective. A fresh game/session restores the latter, walks the remaining route, and reaches the same ending. The original and restored continuations match serialized/deserialized semantic replay. Every one of **1,508 recorded intents** has its acceptance status and intermediate semantic hash checked.

Final original/restored/replayed hash:

`cfc54ce2cac762d0bbeaf38098fa8344152557a8533428800e694691e717dee1`

Framebuffers and transient audio/effect state are excluded from semantic replay. Detailed hashes are in `save-replay.json`.

## Bugs fixed and architecture decisions

1. **Vulkan material-cache exhaustion:** animation generated indefinitely new material keys. Under capacity pressure, Aurelian now frees bindings absent from the current pass. Current bindings and painter order are preserved; preceding submissions have completed. The simultaneous-binding limit remains 4,096. Allocation metrics count actual allocations rather than dictionary growth.
2. **Incorrect house return:** fixed the authored route rather than moving the player in presentation code.
3. **River NPC overlap:** separate authored Elias anchor makes Mara selectable.
4. **Load input/feedback:** stale held controls and pending SFX are cleared; feedback epochs prevent repeated sequence numbers after load from suppressing new audio.
5. **Planting credit:** later harvesting cannot undo the objective step.

The renderer regression submits **5,120 changing material values across 80 passes**, then requires exact canonical pixels and zero warm descriptor rewrites. It fails on the old cache behavior. The native long ambient run independently exposed and now covers the same failure.

No other repository was edited. No new reusable public API was extracted; the reusable repair is internal to Aurelian. Quest engines, cinematic timelines, scene editors, alternative input/movement systems, and general content frameworks were deliberately rejected. The qualification walker uses existing DotRecast proposals and accepted spatial intents, with authored waypoints through wide lanes; it never writes positions.

## Performance and stability

Final Debug measurements on NVIDIA GeForce RTX 3070:

| Measurement | Result |
| --- | --- |
| 120 native frames with movement, mean / median / p95 | 28.23 / 27.37 / 29.34 ms |
| Allocations per measured frame | 4,137,503 bytes |
| Stable UI redraw rebuilds | 0 |
| Stress peak particles / emitters / voices | 232 / 1 / 1 |
| Configured particle / emitter / voice limits | 256 / 32 / 16 |
| Managed retained memory at 0 / 200 / 400 / 600 seconds | 20.02 / 21.20 / 21.32 / 21.48 MB |
| Longer run | 600 native frames advancing 600 host seconds, periodic save/load |

The longer run covers ten **simulated** minutes, not ten minutes of unattended wall-clock play. It finishes without a crash or capacity violation. Retained memory grows modestly as bounded caches warm; this is not a general leak certification.

The main inefficiency is full 1280x720 RGBA readback for the Windows display, roughly 4 MB/frame. Changed Machina overlays also use CPU realization. This works as a small playable slice but is not an optimized swapchain client. Draw counts for each screenshot and detailed measurements are recorded in the required JSON files.

## Validation

| Lane | Result |
| --- | --- |
| `dotnet test Aurelian.slnx -m:1` | 745 passed, 0 failed/skipped |
| `dotnet test TinyFarm.slnx -m:1` | 334 passed, 0 failed/skipped |
| `dotnet test JointTaskForce.slnx -m:1` | 3,479 passed, 0 failed/skipped |
| New focused M9 integration tests | 5 passed, included in TinyFarm |
| Native material-cache/painter regression | Passed; 5,120 changing materials, zero Vulkan validation errors |
| M1 native world | Passed |
| M0 compositor / M2 host-input native proof | Passed |
| M8 effects native proof | Passed |
| M7a/M7b VN native proof | Passed: dialogue, conditions, consequence, saves, capture |
| M3–M6, M7b2, TinyFarm M12–M21 | Existing relevant tests green in solutions above |
| Native M9 flow, save continuation, replay, long run | Passed |
| Visible Windows input/capture smoke | Passed |
| `git diff --check` | Passed |

Total: **4,558 passing solution test executions**, plus native executable proofs. No remote CI was run. Dominatus, Ariadne, Deliverance, InputMan, and compiler owner source was not changed, so no cross-repository owner-change validation is claimed.

## Deliverables and next milestone

Player guide: `src/TinyFarm/README.md`. Double-click launcher: `Play-TinyFarm.cmd`. Canonical client: `src/TinyFarm/TinyFarm.Native`.

`artifacts/aurelian-full-game-slice-m9/` contains the five required JSON files and seven native screenshots:

1. `01-title-or-start.png`
2. `02-farm-gameplay.png`
3. `03-dialogue.png`
4. `04-farming-or-pickup.png`
5. `05-combat.png`
6. `06-secondary-scene.png`
7. `07-completion.png`

No trailer was produced. The game remains visually simple; combat has no aggression or attack animation. Public-demo work remains: self-contained packaging/content deployment, native presentation without readback, physical controller events, volume controls, and an unfamiliar-player playtest.

**Exact next milestone: AURELIAN-TINYFARM-PUBLIC-DEMO-M10.** Ship a self-contained Windows build, replace framebuffer readback with native presentation, qualify physical controller input, and run a short first-time-player usability pass. Preserve this supper scenario as the release acceptance path. More gameplay systems are not required before addressing those release issues.

Working-tree scope: **Copeland only, 38 files**, including seven PNGs. The text delta is approximately **2,672 insertions and 23 deletions**, including source, tests, player documentation, report, and JSON evidence. Other repositories: zero edits.
