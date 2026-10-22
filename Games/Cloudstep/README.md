# Cloudstep

A short 3D platforming course built on the [game shell](../../docs/Aurelian/game-shell.md) and the [platforming primitives](../../docs/Aurelian/platforming.md). Run `cloudstep.cmd` from the repository root.

The course: stairs, a gap, a ramp to a bonus ledge, a shuttle platform, a lift, a crumbling bridge, a turntable and a thorn strip before the goal beacon. It has 21 gems and three checkpoints. Falling, thorns and being crushed cost a life and respawn you at the last checkpoint.

Controls: WASD or arrows to move, Space to jump (hold for height), mouse or Q/E to turn the camera, Escape to pause. Everything is rebindable in Settings → Controls.

The opt-in [image guided look pilot](../../docs/Aurelian/image-guided-look-pilot.md) compares this game's rendering policies against frozen generated appearance targets. Run `cloudstep.cmd --look-policy PATH_TO_SELECTED_SETTINGS.json` to try an exported policy; ordinary launches retain the authored look.

Movement feel (`PlayerTuning`): acceleration with separate air control, 0.1 s coyote time, 0.12 s jump buffer, variable jump height, platform momentum on jump, and 0.4 m step-up.

| File | Role |
| --- | --- |
| `CloudstepCourse.cs` | Authored data: blocks, platforms, gems, checkpoints, hazards, goal |
| `CloudstepWorld.cs` | Rules: platform poses, crumble timers, deaths, pickups, checkpoints, snapshots |
| `CloudstepPlayer.cs` | Platformer controller on `CharacterMotor3D` and `KinematicSpatialWorld3D` |
| `CloudstepGame.cs` | The `IGame`: input layout, camera boom, HUD, music, sound effects, saves |
| `CloudstepVisuals.cs` | Procedural placeholder art |
| `CloudstepAutopilot.cs` | A route runner that plays the course with ordinary inputs |

## Audio

The sound effects are cut from *Insert Quarter* (see `Assets/Audio/README.md`). *Insert Quarter* plays on the title screen and *Sunlight on the Cartridge* on the course. Both are MP3 and are decoded on worker threads by `Aurelian.Audio.Mp3Decoder`. Replace any WAV with your own of the same name.

## Proof

```powershell
dotnet test Games/Cloudstep/Cloudstep.Tests
.\cloudstep.cmd --playtest-script Games/Cloudstep/Playtests/tour.json --output artifacts/local/cloudstep-tour --save-root artifacts/local/cloudstep-tour/saves
.\cloudstep.cmd --headless --playtest-script Games/Cloudstep/Playtests/tour-headless.json --output artifacts/local/cloudstep-headless --save-root artifacts/local/cloudstep-headless/saves
```

The tests drive the autopilot from start to beacon with zero falls. They also cover falling and respawn, thorns, crushing, short versus held jumps, coyote time and exact mid-ride snapshot restore. `tour.json` runs the same route through the real application and captures each section; `tour-headless.json` is the same script without the captures. The playtest command `autopilot on|off` starts the runner, and `checkpoint N` teleports to a checkpoint.
