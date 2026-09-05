# AURELIAN-TINYFARM-DIALOGUE-CONSUMER-M7B2

## 1. Outcome

**Outcome A.** TinyFarm is the second real consumer of the immutable Ariadne
dialogue presentation seam. The VN demo and TinyFarm now consume
`Ariadne.OptFlow.Presentation.DialoguePresentationSnapshot` directly. The type
contains no Machina, portrait, background, auto/skip, save-button, camera, or skin
types. Ariadne/OptFlow remains dialogue authority; `TinyFarmResolver` remains world
authority.

## 2. VN projection audit

| Field | Used by VN | Needed by TinyFarm | Shared? | Action |
| --- | ---: | ---: | ---: | --- |
| dialogue ID | yes | yes | yes | retained as `DialogueId` |
| step/line identity | yes | yes | yes | renamed neutral `OperationId` |
| operation kind | yes | yes | yes | retained as line/choice |
| speaker | yes | yes | yes | `SpeakerId`; null covers narration |
| text/content | yes | yes | yes | retained as `Text` |
| choice IDs/text/order | yes | yes | yes | stable IDs plus declaration index |
| selected choice index | yes | yes | yes | retained; persistence is app policy |
| can advance / awaiting choice | yes | yes | yes | explicit derived facts |
| completion/cancellation | yes | yes | yes | semantic lifecycle facts |
| pending actuation ID | yes | yes | yes | retained for save/debug inspection |
| portrait key | yes | optional | no | VN authored metadata only |
| expression key | yes | no | no | VN authored metadata only |
| background key | yes | no; live world | no | VN authored metadata only |
| auto/skip state | yes | no | no | VN session policy only |
| save/load state/buttons | yes | host integration | no | application integration only |
| terminal text | demo convenience | no | no | removed; completion is a fact |

## 3. TinyFarm conversation definition

`tinyfarm.mara.wild-mint` starts with Mara's greeting, calls a reusable pushed
weather exchange, branches on the explicit snapshot fact “player owns
`TinyFarmIds.WildMint`”, presents one of two ordered choices, and ends cleanly.
The mint branch offers `give-mint` and `keep-mint`; the no-mint branch offers
`ask-town` and `goodbye`. Every visible line and choice has a stable authored
operation ID. No quest, relationship, portrait, camera, voice, or story-state
framework was added.

## 4. Interaction-start law

```text
InteractIntent
  -> TinyFarmSpatialQueries.SelectInteractionTarget
  -> TinyFarmResolver.ResolveTalk(Mara)
  -> accepted Conversation event targeting Mara
  -> TinyFarmDialogueCoordinator begins tinyfarm.mara.wild-mint
```

No live component exposes `Dialogue.Start`. A rejected interaction cannot activate
the dialogue.

## 5. Shared presentation fields

The final snapshot contains `DialogueId`, `OperationId`, `OperationKind`,
`SpeakerId`, `Text`, ordered `Choices`, `SelectedChoiceIndex`, `CanAdvance`,
`IsAwaitingChoice`, `IsCompleted`, `IsCancelled`, and `PendingOperationId`.
The projector reconstructs these facts from authored operation definitions, the
active surface operation, and Ariadne's blackboard pending keys. It owns no hidden UI
cursor.

## 6. Consumer-only fields

VN-only: background, portrait and expression lookup, auto/skip toggles and timers,
quick-save controls, full-screen layout, and screen transitions.

TinyFarm-only: the Mara speaking actor, full semantic pause policy, portrait placement,
live-world composition, green/gold lower-third skin, and conversion of a consequence
to a game intent.

## 7. Final projection ownership and dependency boundary

`DialoguePresentationSnapshot`, `DialoguePresentationOperation`, the ordered choice
record, and `DialoguePresentationProjector` live in
`Ariadne.OptFlow.Presentation`. The assembly already owns stable dialogue operation
inspection and depends only on Dominatus semantic runtime types. It does not reference
Machina or Aurelian. Both applications adapt the same snapshot into their own skins.
The owner change is upstream on Dominatus `master` at `8672881`.

## 8. In-world presentation and input

TinyFarm leaves the normal tile world visible, draws the generated transparent Mara
pixel portrait, then draws a compact green/gold Machina lower-third. Choices appear
in declaration order and the selected row is highlighted. Pointer activation uses
the existing Machina hit-test/capture path; no new pointer infrastructure exists.

InputMan defines `DialogueAdvance`, `DialogueChoiceUp`, `DialogueChoiceDown`,
`DialogueConfirm`, and `DialogueCancel` in a priority-200 dialogue map. While active,
the MonoGame leaf activates only that map, suppresses its legacy UI key routing, sets
player movement to zero, and does not execute gameplay tool/attack actions. The
Machina layer is opaque and requests focus/capture. Once presentation is absent the
layer returns to hit-test policy and gameplay input resumes.

## 9. Simulation and speaking actor policy

TinyFarm chooses full semantic pause while a conversation is active: it skips
`AdvanceHostTime`, clears held player movement, and leaves world rendering active.
This is application code, not Ariadne policy. Because NPC locomotion does not tick,
Mara remains stable for the conversation without a generic speaking-actor system.

## 10. Conditional and typed-consequence proof

At dialogue start the application projects player ownership of Wild Mint into the
dialogue blackboard. The graph reads that explicit fact; the UI does not evaluate the
condition. Choosing `give-mint` emits `GiveMaraWildMintConsequence`, whose application
handler submits the existing `GiveIntent(WildMint, Mara)` through
`TinyFarmSimulationHost` and `TinyFarmResolver`. The accepted proof transfers the
authoritative item owner to Mara.

The handler retains the exact `IntentResult` and sets an explicit consequence-result
fact. If the player no longer owns the item, the resolver returns `ItemNotOwned` and
the graph presents `mara.mint-rejected`; it never presents the success line or mutates
ownership optimistically.

## 11. Save and presentation-state law

The Deliverance TinyFarm module now optionally carries a
`TinyFarmDialogueCheckpoint`: Dominatus chunks, dialogue ID, selected choice index,
active state, and cancellation state. Save at a pending line restores the same world
hash and operation with zero line redispatch. Save at a pending choice restores the
same IDs/order and selected index; continuation emits the effect exactly once.

Durable: world state, active semantic dialogue position/pending actuation, dialogue
identity, and the intentionally persisted selection. Derived: visible choices,
speaker, content, advance/choice flags, and presentation. Non-durable: typewriter
progress, Machina focus/capture objects, panel layout, texture handles, and portrait
placement.

## 12. Replay parity

The tape records `TinyFarmDialogueAction` values, not keys or coordinates. Replaying
advance/selection/confirm after the same accepted `InteractIntent` produces the same
operation trace, accepted consequence result, item owner, and final TinyFarm semantic
hash.

## 13. World/compositor integration

```text
TinyFarm native MonoGame world pass
  -> optional app-local Mara portrait in the world presentation
  -> TinyFarm Machina dialogue overlay
  -> existing compositor frame
```

No offscreen VN target, background replacement, scene graph, or conversation camera
was introduced.

## 14. Second-consumer comparison

| Concern | VN | TinyFarm | Shared |
| --- | --- | --- | --- |
| current line | yes | yes | yes |
| speaker | yes | yes | yes |
| ordered choices | yes | yes | yes |
| portrait | required by skin | optional generated Mara art | no |
| background | authored bitmap | live world renderer | no |
| auto/skip | yes | no | policy only |
| focus/capture | yes | yes | compositor/InputMan mechanism |
| save/load | quick slot | TinyFarm Deliverance module | host integration |
| skin/layout | full-screen VN | lower-third farm overlay | no |

## 15. Extraction decision

Extraction passed the threshold. Both consumers use the same neutral type directly;
neither carries null portrait/background fields for the other; no skin metadata leaks
into Ariadne. No `Ariadne.Machina` package and no skin extraction were created.

## 16. Performance, allocation, and inspection

The TinyFarm coordinator caches the immutable snapshot and invalidates it only when
the semantic operation or selection changes. Ten thousand stable reads allocate zero
bytes in the focused test. Re-sending the same dialogue snapshot for 120 Machina frames
performs one topology/layout/presentation/hit-test build and zero dynamic updates.

Inspection exposes active dialogue ID, operation/line/choice ID, visible choice IDs,
pending operation ID, active speaker, speaking actor, cancellation and completion.
No debug UI was added.

## 17. Tests and validation

Added focused tests cover interaction start and dialogue ID, line/speaker projection,
choice order, conditional branches, pushed subdialogue, accepted/rejected typed
consequences, InputMan capture and gameplay suppression, opaque Machina routing,
line/choice save restoration, no redispatch, selection persistence, replay parity,
shared-type parity with the VN demo, stable-frame invalidation, and allocation.

All required validation completed successfully:

| Lane | Result |
| --- | ---: |
| `dotnet test Aurelian.slnx -m:1` | 728 passed |
| `dotnet test TinyFarm.slnx -m:1` | 323 passed |
| `dotnet test JointTaskForce.slnx -m:1` | 3,476 passed |
| Ariadne owner tests on Dominatus master | 31 passed |
| Dominatus Core owner tests | 255 passed on net8 and 255 on net10 |
| `dotnet test Dominatus.Release.slnx -m:1` | 1,401 passed, 8 credential-dependent live tests skipped |
| `dotnet test Deliverance.slnx -m:1` | 30 passed, 1 intentional golden generator skipped |
| VN `--proof` | Outcome A; all M7b proof categories qualified |
| TinyFarm MonoGame `--m7b2-proof-dir` | completed; four screenshots and five JSON artifacts regenerated |
| `git diff --check` in Copeland and standalone Dominatus | passed |

The Ariadne and Dominatus owner lanes run directly from the standalone sibling
Dominatus repository on `master`, preserving its own multi-target and package
policy. Visual proof is generated by the real `TinyFarm.MonoGame` executable and
shows world + portrait + lower-third composition.

## 18. Artifacts and generated art

`artifacts/aurelian-tinyfarm-dialogue-consumer-m7b2` contains `proof.json`,
`projection-audit.json`, `save-replay.json`, `extraction.json`, `manifest.json`, and:

- `01-line.png`: Mara's line over the live town;
- `02-choice.png`: ordered mint choices;
- `03-save-restored.png`: exact pending choice reconstructed from semantic state;
- `04-conditional.png`: accepted mint consequence result.

The optional portrait is `src/TinyFarm/TinyFarm.MonoGame/Assets/mara-dialogue.png`.
It was produced with the built-in image generator as an original transparent,
chest-up cozy pixel-art farmer; no text, logo, background, anime/VN styling, or voice
asset was requested.

## 19. Diff statistics

The final cleanup removes the `reference/dominatus` gitlink and `.gitmodules`, and
redirects the five active source integration projects to the standalone sibling
Dominatus repository. Dominatus master owns the new projection/doc plus the general
checkpoint actuation-ID reservation and its regression test.

## 20. Exact next milestone

**AURELIAN-GAME-EFFECTS-EMITTERS-M8.** The second-consumer projection question is
closed without a RenC# product layer. The next bounded engine pressure is semantic
game feedback/effect emitters; any RenC# productization should remain a separate,
evidence-led milestone.
