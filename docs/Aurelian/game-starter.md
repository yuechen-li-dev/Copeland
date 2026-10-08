# Composable C# game starter

Create an executable project with a reference to `src/Integrations/Aurelian.Games/Aurelian.Games.csproj`. Inside `Games/MyGame/MyGame`, this is the entire project:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../src/Integrations/Aurelian.Games/Aurelian.Games.csproj" />
  </ItemGroup>
</Project>
```

The repository supplies the .NET 10 target through `Directory.Build.props`. A project outside the repository should specify its target framework explicitly.

```csharp
using Aurelian.Games;

GameStarter.Run("my-game",
    [.. GamePresets.FirstPersonShooter,
     GameConcept.ThirdPersonCamera, GameConcept.ThirdPersonControl],
    args);
```

This starts a geometric training range with a title menu, pause menu, settings, a character agent, object targets, collision, mouse look, jump, hitscan gun, magazine/reload state and switchable first/third-person views. `V` switches views without replacing the actor or gun. `WASD` moves, mouse looks, left click fires, `R` reloads, Space jumps, Escape pauses. Menus support InputMan keyboard navigation and actual rendered-button hit testing.

Run the checked-in example with `starter.cmd`. Its project has one runtime reference and a short entry point. `TrainingRange.cs` shows [document scene composition](scene-composition.md): one checkpoint fragment instantiated twice and a target customized with `with`. Engine shaders, fonts and their licenses ship transitively; the published starter resolves its own built-in assets without finding the checkout.

## Concepts compose; presets only select concepts

`GamePresets.FirstPersonShooter` contains `ThreeDimensional`, `FirstPersonCamera`, `FirstPersonControl`, and `ReloadableGuns`. `ThirdPersonShooter` contains the corresponding third-person camera/control fragments. Both use the same `GameDefinition` validation and execution path as a hand-written list.

Fragment order and duplicate declarations do not change a definition's identity. A control fragment requires its matching camera; a camera can be used without movement controls. Invalid or unsupported declarations fail before creating a native window. Adding both cameras automatically enables view switching; controls apply to the active view.

The currently executable fragment library covers this 3D FPS/TPS foundation. Driving, top-down controls, VN presentation and farming/strategy starter fragments are additional library work. The existing Sunkill, TinyFarm and Mossward games retain their specialized applications, rendering adapters and domain save schemas. Their folder consolidation does not turn their entire rule sets into engine modules.

## Customize through ordinary typed code

```csharp
using Aurelian.Combat;
using Aurelian.Games;
using InputMan.Core;

GameStarter.Run("my-range", GamePresets.ThirdPersonShooter, args,
    new StarterOptions(
        Title: "MY RANGE",
        MovementSpeed: 6,
        Gun: new GunConfiguration(Capacity: 6, ReloadSeconds: 0.8f),
        Objects:
        [
            new("practice", new(0, 1, -4), new(1, 1, 1), Health: 5),
        ]),
    configure: game =>
    {
        game.Rebind(game.Controls.Keys with { Forward = KeyboardKey.I });
        game.ShotFired += current => Console.WriteLine(current.Observe().Shots);
    });
```

`GameStarter.Create` creates the same application without a window, for tests or application-owned hosting. `StarterGame` exposes typed commands, snapshots, authored agents, camera matrices, scene geometry, settings and events. Snapshot validation checks geometry, fragment compatibility, weapon state, keybindings and cadence phase before replacing live state. Target identities are deterministic regardless of declaration order.

The training range is a small starter world. Custom game domain state needs its own explicitly captured snapshot and validation. `GameSaveSlots<TSnapshot>` accepts a game's source-generated `JsonTypeInfo<TSnapshot>` and validator; it does not discover fields or silently include state added by event handlers.

`GameStarter.Run` and `Create` also accept `sceneDocument: SceneGroup`. The starter accepts one root `player` with `StarterPlayerDefinition`, targets with `StarterTargetDefinition`, decorative box/triangle geometry and explicit solid boxes. Target health is saved in canonical instance-ID order. Scene transforms, geometry and explicit definition identities participate in Deliverance's compatibility identity. Switching to an authored document changes that identity; incompatible older slots fail before changing live state.

The starter's existing collision and hitscan rules support translated and positively scaled axis-aligned boxes. Rotated decorative geometry renders normally; rotated solid boxes or target colliders fail with a diagnostic. Other game state types, dynamic spawning and richer physics need application-owned rules and snapshots; the starter does not silently save arbitrary agents added through its inspection-facing `Scene` property.

## Shared owners and persistence

| Capability | Owner / reuse |
| --- | --- |
| Native lifecycle, focus and frame ordering | Existing `AurelianGameHost` |
| Fixed 60 Hz simulation | Existing `CadenceScheduler`, including validated phase restore |
| Physical input, contexts, action edges, rebinding | InputMan through `GameControls` and `AurelianInputAdapter` |
| Native subscriptions and captured mouse | `CapturedGameInput`, extracted from Beacon |
| Cameras | `Aurelian.Runtime.Camera3D` |
| Magazine/reload state | `Aurelian.Combat.ReloadableGun`, also consumed by Beacon |
| Primitive geometry | `PrimitiveGeometry3D`, also consumed by Beacon |
| Vulkan device, target, renderer and presenter lifetime | `NativeGameGraphics`, extracted from Beacon |
| Menus and retained native text | Existing `Aurelian.GameMenus` and Machina |
| Built-in shader/font resources | `GameAssets`; default fonts now live in `Aurelian.Assets/Defaults/Fonts` |
| Audio | Existing `AurelianAudioRuntime`, NAudio on Windows, null backend for deterministic playtesting |
| Save slots and preferences | `GameSaveSlots<T>`, backed by Deliverance's real containers, integrity and atomic store |
| Scripts, stdin, observations, replay | Existing `Aurelian.Playtesting` |
| Autonomous policy and AI inspection | Existing `Aurelian.Runtime` Dominatus integration; game-owned controllers opt in explicitly |

Game saves default to `%LOCALAPPDATA%/<game-id>/saves`. Settings use a separate Deliverance preferences slot under `saves/config`. Menu sensitivity/volume changes persist without taking a world save. Slot 1 is exposed in the basic menu; the typed API supports arbitrary named slots, listing, existence checks and deletion. The starter saves the active view, actor position/orientation, weapon timers, target health, input configuration, volume and cadence phase.

Dominatus kernel checkpoints remain policy/kernel tools. They are not the default game save system. Existing games' historical Deliverance modules and migrations remain intact.

## Playtest the actual application

```powershell
.\starter.cmd --headless --playtest-script Games/Starter/Playtests/headless-proof.json `
    --output artifacts/my-game-test --save-root artifacts/my-game-test/saves

.\starter.cmd --playtest-script Games/Starter/Playtests/native-proof.json `
    --output artifacts/my-game-native --save-root artifacts/my-game-native/saves

.\starter.cmd --headless --playtest-stdio --save-root artifacts/my-game-shell/saves
```

Native scripts use Vulkan and can capture pixels; both modes advance the same `StarterGame`. Stdin commands include `click-action start`, `hold w 0.3`, `tap v`, `save slot-name`, `load slot-name`, `mark name`, and `assert-mark name`. Assertions support screen, view, ammo, shots and hits. `checkpoint`/`rewind` use the shared runner's input-prefix replay. External save-file writes are not rolled back by replay; use isolated test save roots and avoid treating replay as a transactional filesystem rewind.

`--launch-smoke` exercises four frames through the interactive `AurelianGameHost` path and exits. `--visible` shows script/smoke windows. `--output` controls evidence output and `--save-root` isolates persistence.

## Qualification and current bounds

The qualification record is [aurelian-game-starter-m0/manifest.json](../../artifacts/aurelian-game-starter-m0/manifest.json). The published starter was run from `C:\Windows\Temp`; native/headless final observations agree. Native proof covers gun hits/reload, both views, movement, mouse deltas, Deliverance save/load, menu settings and replay. Beacon's existing full FPS/win proof and the moved TinyFarm/Sunkill native paths are also checked.

The starter currently has a fixed 960x600 Vulkan window, a geometric scene, simple horizontal box collision and a follow camera. It does not supply general rigid-body/vehicle physics, camera obstruction avoidance, imported models, rigs, shadows or a generic texture streaming system. Built-in text uses the existing bounded ASCII font atlas. Audio uses the existing platform backend and reports fallback to null output if no device is available; scripted proof qualifies audio state, not audible output quality. This is a framework-dependent .NET 10 published build, not a qualified standalone installer.

The existing dependency-boundary checker still reports seven pre-existing violations. Their references were verified against the original Git HEAD in [boundary-baseline.json](../../artifacts/aurelian-game-starter-m0/boundary-baseline.json); this change adds a production-to-`Games` prohibition without suppressing those findings.
