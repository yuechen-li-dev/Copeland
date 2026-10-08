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

Run the checked-in example with `starter.cmd`. Its short entry point selects either the geometric range or the baked Aetheris room. `TrainingRange.cs` shows [document scene composition](scene-composition.md): one checkpoint fragment instantiated twice and a target customized with `with`. Engine shaders, fonts and their licenses ship transitively; the published starter resolves its own built-in assets without finding the checkout.

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

`GameStarter.Run` and `Create` also accept `sceneDocument: SceneGroup`. The starter accepts one root `player` with `StarterPlayerDefinition`, targets with `StarterTargetDefinition`, decorative box/triangle geometry and explicit solid boxes or indexed meshes. Target health is saved in canonical instance-ID order. Scene transforms, geometry, collision roles and explicit definition identities participate in Deliverance's compatibility identity. Switching to an authored document changes that identity; incompatible older slots fail before changing live state.

The starter and Beacon use `Aurelian.Spatial3D` for rays, capsule sweeps, grounded movement, slopes, sliding, ceilings and collision filtering. Rotated and nonuniformly scaled authored solids use their actual affine frames. Floors are explicit solids; leaving their boundaries causes falling. `StarterGame.SpatialWorld` and `LastMovement` expose query geometry and contact facts for inspection. Other game state types, dynamic spawning and richer physics need application-owned rules and snapshots; the starter does not silently save arbitrary agents added through its inspection-facing `Scene` property.

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
| Raycasts, overlaps, sweeps and character movement | Pure `Aurelian.Spatial3D`, projected from document collision by `SceneSpatial3D` |
| Optional hardware rays | `Aurelian.Spatial3D.Vulkan` on the existing Vulkan plant; compiler-owned `RayQuery.v.ts` |
| Aetheris room assets | Build-time `Aurelian.AetherisBake`; source-generated `Aurelian.Aetheris` runtime loader |
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

## Aetheris room and Vulkan ray queries

```powershell
.\starter.cmd --aetheris-room --gpu-rays

.\starter.cmd --aetheris-room --gpu-rays --playtest-script Games/Starter/Playtests/room-proof.json `
    --output artifacts/room-native --save-root artifacts/room-native/saves

dotnet run --project tools/Aurelian.AetherisBake -c Release -- bake `
    ../Aetheris/fixtures/Canonical/Scene/room.firmament `
    Games/Starter/Aurelian.Starter/Assets/room.aurelian.json --solid-prefix RoomProof.hall
```

The bake uses Aetheris's existing Firmament scene compiler. Indexed definitions, occurrence paths, source hash, available face identities and explicit solid selections survive into the asset. The occurrence frame converts millimetres/Z up to metres/Y up once. The ordinary game loader requires no CAD repository or CAD runtime. The optional bake project references the adjacent Aetheris checkout, overridable with `-p:AetherisRepository=...`, and is excluded from the ordinary engine solution.

`--gpu-rays` enables retained BLAS/TLAS and inline ray traversal on the same Vulkan device used for rendering. Unsupported hardware reports a diagnostic and uses CPU rays. Headless execution uses CPU queries. `GameRayQueries.Create` compiles the reusable `.v.ts` through Copeland's binder, VD-MIR, Aurelian HLSL/DXC and SPIR-V validation. `RayQueryTraceClosest` is a statement-only compiler intrinsic; acceleration structures cannot escape into ordinary local storage or helper parameters. The native adapter owns upload, synchronized submission, readback and disposal. `RaycastBatch` amortizes this work across rays; a single shooting ray still incurs synchronous readback and is not expected to outperform the CPU. Geometry is retained and static; rebuild a backend after changing colliders.

Capsule sweeps and character movement remain deterministic CPU calculations. Hardware rays do not directly provide a shape sweep. The pure `ISpatialQueryWorld3D` interface lets another collision representation provide the same contact facts without changing the motor or agent state ownership.

```csharp
using System.Numerics;
using Aurelian.NativeComposition;
using Aurelian.Spatial3D;

SpatialWorld3D queries = SceneSpatial3D.Build(plan);
var motor = new CharacterMotor3D(new(MaximumSlopeDegrees: 45));
CharacterState3D state = new(player.State, verticalVelocity);
CharacterMove3D moved = motor.Step(queries, state, horizontalVelocity, jumpPressed, 1f / 60);
player.State = moved.State.Feet;
verticalVelocity = moved.State.VerticalVelocity;

SpatialHit3D? hit = queries.Raycast(new Ray3D(eye, unitAim, 80));
SpatialHit3D? obstruction = queries.Sweep(Capsule3D.Sphere(cameraAnchor, 0.15f), cameraOffset);
```

The application owns `plan`, typed player state, velocities, input and cadence in this example. `QueryFilter3D` requires both included-layer and query-layer/solid-mask intersections. Contact normals come from collision geometry, independently of interpolated visual normals. Query worlds are immutable: rebuild from authored facts when static geometry changes. No reflection, component discovery or hidden actor updates participate in these calls.

BRep is valuable as authored truth and provenance. The room's rectangular boundary panels are marked `planar-exact`: their triangles preserve the original surfaces and openings, with no curved-surface approximation. Other definitions are marked `triangle-approximation`; baking them as collision requires explicit `--allow-mesh-approximation`. This is a declared surface approximation, not general exact BRep collision. The GPU path also supports exact analytic sphere intersections through procedural AABBs, following Oct's SDSL-V implementation. General trimmed BRep ray intersection, capsule contact and continuous sweeps require additional Aetheris query capability; its existing primitive rays and bounded closest-point helpers do not yet provide that complete contract.

The current spatial evidence is [aurelian-spatial3d/manifest.json](../../artifacts/aurelian-spatial3d/manifest.json). The native proof compares 4,096 room rays with CPU provenance and distances, checks full-width symmetric layer masks and stable ties, and traverses the authored doorway while blocking its solid wall. Local timing includes upload/readback for ten batches and is evidence for this fixture and device only.

The GPU reports hardware-admitted triangle provenance. At a shared triangle edge, hardware can select a different adjacent triangle from the CPU's inclusive intersection convention. The dedicated boundary proof checks collider, distance and geometric normal agreement; it does not promise identical primitive/face selection on an ambiguous boundary. Interior coincident candidates are ordered canonically. Game logic should use semantic collider ownership, rather than depend on an incidental triangle index.

## Qualification and current bounds

The qualification record is [aurelian-game-starter-m0/manifest.json](../../artifacts/aurelian-game-starter-m0/manifest.json). The published starter was run from `C:\Windows\Temp`; native/headless final observations agree. Native proof covers gun hits/reload, both views, movement, mouse deltas, Deliverance save/load, menu settings and replay. Beacon's existing full FPS/win proof and the moved TinyFarm/Sunkill native paths are also checked.

The starter currently has a fixed 960x600 Vulkan window, authored 3D collision and a follow camera. It does not supply general rigid-body/vehicle physics, step climbing, camera obstruction avoidance, Blender/FBX import, rigs, shadows or a generic texture streaming system. Initially embedded capsules fail closed with an explicit overlap rather than teleporting out of geometry. Iteration-limited sweeps stop conservatively and expose the status. Built-in text uses the existing bounded ASCII font atlas. Audio uses the existing platform backend and reports fallback to null output if no device is available; scripted proof qualifies audio state, not audible output quality. This is a framework-dependent .NET 10 published build, not a qualified standalone installer.

The existing dependency-boundary checker still reports seven pre-existing violations. Their references were verified against the original Git HEAD in [boundary-baseline.json](../../artifacts/aurelian-game-starter-m0/boundary-baseline.json); this change adds a production-to-`Games` prohibition without suppressing those findings.
