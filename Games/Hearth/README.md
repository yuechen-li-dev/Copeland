# Hearth

A night campsite for judging Aurelian's fire and smoke. It has a campfire with embers and the odd spark, two wall torches and a brazier, each with a flickering fire light. Run `hearth.cmd` from the repository root.

Controls: mouse or A/D to orbit, W/S to zoom, Space for the next fire, Escape to pause.

Everything visual is a pure function of the fixed-step clock: particle commands, light flicker and the camera shots. Playtest captures therefore replay exactly.

| File | Role |
| --- | --- |
| `HearthScene.cs` | The campsite, built from boxes, and the four fire sites |
| `HearthEffects.cs` | Fire sites and time become particle emitters (fire, smoke lit from below, embers, sparks) and flickering point lights |
| `HearthGame.cs` | The `IGame`: orbit camera, named shots, a daylight variant, playtest commands |

The effects themselves are engine features; see [GPU VFX](../../docs/Aurelian/gpu-vfx-foundation.md) for the techniques and references.

## Proof

```powershell
dotnet test Games/Hearth/Hearth.Tests
.\hearth.cmd --playtest-script Games/Hearth/Playtests/gallery.json --output artifacts/local/hearth-gallery --save-root artifacts/local/hearth-gallery/saves
.\hearth.cmd --playtest-script Games/Hearth/Playtests/daylight.json --output artifacts/local/hearth-daylight --save-root artifacts/local/hearth-daylight/saves
.\hearth.cmd --headless --playtest-script Games/Hearth/Playtests/gallery-headless.json --output artifacts/local/hearth-headless --save-root artifacts/local/hearth-headless/saves
```

The tests check that every frame submits valid, unique emitters that fit the default pool, that effects and flicker are pure functions of time, and that the playtest commands work. The gallery captures each fire at night. The daylight script swaps in a high sun to show how smoke takes sunlight. `gallery-headless.json` runs the same steps without captures. Playtest commands: `shot wide|campfire|torch|brazier|smoke`, `orbit DEGREES` and `daylight on|off`.
