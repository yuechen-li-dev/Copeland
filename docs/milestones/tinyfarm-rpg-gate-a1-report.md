# TinyFarm RPG Gate A1 — agent properties

Outcome: **A / Success for Gate A1**. The normal native game has a readable agent properties screen, and UI/text input ownership is qualified through InputMan and the real Vulkan host. This gate establishes authored data and inspection; it does not claim skill training, derived combat stats, or a completed trait/condition effect system.

The [approved initial RPG design](../design/tinyfarm-rpg-progression-design-spec.md) remains a starting design to refine through play.

## Review build

Run `Play-TinyFarm.cmd`, start the game, then press **C**. Alternatively, choose **Agent properties** from the pause menu. No proof executable or special flag is needed for the screen.

- Opens on the six base stats, with a progression-eligibility row.
- Categories: Identity, Resources, Stats, Skills, Traits, Conditions, Equipment, Inventory, Presentation, and All.
- Left/right arrows or the Agent buttons inspect another agent. This changes inspection selection, not player control.
- Click the filter or press Tab to type. Search covers property names, groups, values, and provenance/status.
- Wheel, draggable scrollbar, track paging, up/down arrows, and Previous/Next browse the bounded table window.
- Enter/Escape leaves typing first; another Escape closes the screen. Closing returns to its play/pause parent.
- F9 can hide normal HUD without hiding the properties screen. F10/F11 presentation controls retain their existing contracts.

For additional character/object examples, run `Play-TinyFarm.cmd --agent-authoring`. Ivy has distinct base stats, authored Farming/Sword ranks, a declared trait, a declared condition, and equipment. The supply cache exposes object properties and inventory without character abilities.

## Data foundation and authority

`TinyFarmRpgProfile` is optional agent-owned semantic data on the existing actor record: six typed base abilities, progression eligibility, current/maximum Spirit Points, typed skill ranks and remainder XP, and trait IDs. Templates author it; instances and deep copies own independent collections. New opening characters receive initial profiles. Gardener and Mara are progression-eligible regardless of their human/schedule controller; the other opening profiles are fixed initially.

The screen reads live HP from the existing opening combat owner, equipment from the existing equipment owner, energy from the existing energy owner, and inventory from the shared agent inventory projection. It does not manufacture a second HP, equipment, or inventory authority. Missing values are explicitly reported as not authored/not applicable.

The skill catalog exposes all 26 initial design skills. Omitted skill entries mean rank 0 with no XP. A1 shows authored ranks and the proposed next-rank curve, but actions do not yet award practice. Trait declarations and existing condition strings are labeled as declarations with effects pending. They cannot silently modify gameplay. Base stats are shown as base stats, rather than pretending skill contributions are already applied to combat.

Save version 14 and runtime identity `tiny-farm-rpg-profile@14` preserve the profile through the existing chunked save path and generated JSON graph. RPG inputs participate in the semantic hash with canonical skill/trait ordering. Presentation selection, category, filter, and scroll offset do not. Version-13 saves remain readable without silently inventing profiles; they display that the profile was not authored. Older supported save versions retain their existing adapter paths.

## InputMan ownership

| Application context | Active maps |
|---|---|
| Gameplay | System, Shortcuts, Gameplay |
| Dialogue | System, Dialogue |
| Title, pause, inventory, properties | System, Shortcuts, UI |
| Inventory/properties text filter | System, TextEntry |

System contains the presentation function keys. Letter-based checkpoint/quit shortcuts are separated into Shortcuts. TextEntry contains search-finish bindings; normal UI confirmation, category navigation, close keys, gameplay axes/actions, and checkpoint/quit shortcuts are absent. Disabling lower maps provides exclusive modal ownership, rather than relying on a search box to consume a handpicked list of gameplay keys.

The native Unicode text/edit/pointer channel still supplies text and field editing. InputMan owns logical action eligibility. Application guards cover the frame in which pointer focus changes after the physical snapshot was sampled. The native adapter updates the active maps at the host boundary. Paused menus also stop simulation; the tests independently prove that blocked actions/axes are absent from InputMan's output, not merely ignored by a paused simulation.

A regression test exposed a second issue in the requested InputMan owner: an action whose map reactivated while its key was held generated a synthetic press. The fix in `../InputMan/src/InputMan.Core/InputManEngine.cs` requires both a logical up-to-down transition and an effective physical/chord press edge. Continuous held controls remain held; returning from a text box does not invent an attack or confirm press. Existing multiple-binding aggregation and chord semantics remain covered by the InputMan suite.

The InputMan changes are in its separate checkout, alongside pre-existing function-key edits that this pass preserved. They must accompany the Copeland changes when committing/sharing the work. No InputMan source copy or game-specific key suppression workaround was added.

## Qualification

Environment: Windows, .NET SDK 10.0.401; native Vulkan device NVIDIA GeForce RTX 3070.

| Check | Result |
|---|---|
| `dotnet build TinyFarm.slnx -c Release -m:1` | Passed; final build 0 warnings, 0 errors |
| `dotnet test TinyFarm.slnx -c Release -m:1` | 393 TinyFarm + 27 Spatial2D = 420 passed |
| `dotnet test ../InputMan/InputMan.slnx -c Release -m:1` | 78 Core + 19 MonoGame connection + 7 Stride connection = 104 passed |
| A1 + inventory + agent-authoring tests with `JsonSerializerIsReflectionEnabledByDefault=false` | 31 passed; a focused subset, not additional to the totals |
| Native `--stats-proof`, JSON reflection disabled | Passed on actual Vulkan device |
| Native dimensions | 1920×1080, 2560×1440, 1600×1000 |
| Whitespace checks | Copeland and InputMan diffs checked |

The initial dependency rebuild emitted existing CS0649 warnings in the vendored OpenFont sources. No new RPG/InputMan compiler warnings were introduced. No dependencies were installed and no new reflection registry was introduced.

Unit tests exercise action-map output for movement/attack/UI/shortcut keys while filtering; held-key map transitions; menu parents; presentation controls during text entry; generated save/load; canonical hashes; deep-copy ownership; invalid ranks/XP; explicit missing profiles in old saves; live HP; NPC skill/trait/condition inspection; object inventory; filtering; and the actual Machina scrollbar reducer.

The native proof drives the real window/input/host/render path. It opens C, types into the filter while pressing W and other conflicting keys, compares world and field hashes, scrolls with wheel and drag, inspects Ivy and the cache, toggles HUD, resizes, exercises the pause-menu parent, and saves/loads the profile through the menu. It asserts that the player position, semantic world, and field remain unchanged while inspecting and typing. Its JSON is the machine-readable qualification record.

Reproduce:

```powershell
dotnet run --project Games/TinyFarm/TinyFarm.Native/TinyFarm.Native.csproj -c Release -p:JsonSerializerIsReflectionEnabledByDefault=false -- --stats-proof
```

Evidence: [native proof](../../artifacts/tinyfarm-rpg-gate-a1/native-proof.json), [manifest](../../artifacts/tinyfarm-rpg-gate-a1/manifest.json), and eleven native screenshots in that directory. The 1080p stats, 1440p skills, and non-16:9 captures were inspected for readable text, table bounds, and scalable layout.

## Deliberate boundary and next pressure

The capability requested for A1 is ready for review. No XP awarding, trait damage bonuses, timed conditions, stat-derived damage/speed, new spell, or possession mechanic was added. SP is authored/inspectable but has no new gameplay consumer. Condition durations and removal rules remain the approved design, not executed effects. The current player-health/equipment legacy owners remain visible until their common-agent migration is implemented.

The next pressure is **one meaningful action producing durable skill practice and an explained stat contribution through the shared agent reducer**, using farming and heated cooking as the first cross-training case. That follows this gate; it is not bundled into a stat-screen milestone.
