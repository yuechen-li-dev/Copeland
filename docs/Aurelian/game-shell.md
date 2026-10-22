# Game shell

Most of what makes a program feel like a finished game has nothing to do with the game's rules: a window you can resize or make fullscreen, a title screen, a pause menu, settings that stick, rebindable keys, volume sliders, focus handling, saves. Aurelian's game shell provides all of that. A game supplies an `IGame`, and its project contains only game logic.

```csharp
using Aurelian.Games;

GameApp.Run(new MyGame(), args);
```

## What a game writes

```csharp
public sealed class MyGame : IGame
{
    public GameManifest Manifest { get; } = new("my-game", "MY GAME", new GameInputLayout(
    [
        new("move.left", "Move left", GameControl.Key(KeyboardKey.A), Secondary: GameControl.Key(KeyboardKey.ArrowLeft)),
        new("move.right", "Move right", GameControl.Key(KeyboardKey.D), Secondary: GameControl.Key(KeyboardKey.ArrowRight)),
        new("jump", "Jump", GameControl.Key(KeyboardKey.Space)),
    ]))
    {
        Tagline = "A SHORT SUBTITLE",
        ControlsHint = "A / D MOVE   SPACE JUMP",
    };

    public void Start(GameContext context) => context.AddTone("jump", 520, 0.08f); // or AudioResources.LoadPcmWav
    public void NewGame() { /* reset the world */ }

    public void FixedUpdate(GameTick tick)
    {
        float move = tick.Input.Axis("move.left", "move.right");
        if (tick.Input.WasPressed("jump")) { /* ... */ }
    }

    public GameView? View(GameViewRequest request) => new(scene, camera(request.Aspect), eye);
    public void BuildHud(GameHud hud) => hud.StatusBar($"SCORE {score}");
    public void Observe(GameFacts facts) => facts.Set("score", score);
    public void Dispose() { }
}
```

| Member | Required | Purpose |
| --- | --- | --- |
| `Manifest` | yes | Id (save directory), title, input layout, tagline, controls hint, authored `Graphics3DSettings`, default display, tick rate |
| `NewGame()` | yes | Reset the world when the player picks **New game** |
| `FixedUpdate(GameTick)` | yes | Deterministic simulation at `Manifest.TickRate` (60 by default) while playing |
| `Start(GameContext)` | no | Register sounds; keep the context for `PlaySound` |
| `Update(GameUpdate)` | no | Once per presented frame while playing, for immediate camera look |
| `OnFrame(GameFrame)` | no | Once per frame on every screen, menus included (for example, music for the current screen). It must not change simulation state |
| `Command(string)` | no | Game-specific playtest commands (Cloudstep: `autopilot on`, `checkpoint 2`); return false for unknown commands |
| `View(GameViewRequest)` | no | Scene, camera and eye to draw. It is also drawn behind menus, so return a valid backdrop before `NewGame` too |
| `BuildHud(GameHud)` | no | Panels and text in logical units, drawn while playing |
| `Saves` | no | A `GameSaveSchema` for typed world saves; without one the menus hide save/load |
| `Observe(GameFacts)` | no | String facts that playtest scripts can `assert` |
| `InputContexts` | no | Input contexts active this frame (default: `play`); see [Input contexts](#input-contexts) |
| `AttachGraphics` / `PrepareGpuGeometry` / `DetachGraphics` | no | Native-only hooks for lighting assets, environments, GPU-generated geometry and 2D layers |

`GameTick.Input` is shell-latched. A tap that starts and ends between two simulation ticks still reports `WasPressed` on the next tick, exactly once. `IsDown` is the current state. `Update` sees per-frame edges and the mouse-look delta. A game should consume look in one place: `Update` for responsiveness, or `FixedUpdate` for determinism.

## Input contexts

A layout can group actions into contexts, such as free play, a dialogue box or an inventory screen. Every frame the
game returns the active ones from `InputContexts`.

```csharp
new GameInputLayout(
[
    new("dodge", "Dodge", GameControl.Key(KeyboardKey.Space)),
    new("talk", "Talk", GameControl.Key(KeyboardKey.E)),
    new("advance", "Next line", GameControl.Key(KeyboardKey.Space)) { Context = "dialogue" },
    new("close", "Close", GameControl.Key(KeyboardKey.Escape), Rebindable: false) { Context = "dialogue" },
])
{
    Contexts = [new GameInputContext(GameInputContext.Play), new GameInputContext("dialogue", Modal: true)],
};

public IReadOnlyCollection<string> InputContexts => talking ? ["dialogue"] : [GameInputContext.Play];
```

- Each context is its own InputMan map, and contexts with higher `Priority` see a key first.
- A modal context consumes the keys it binds, so a lower context never sees them. Above, Space advances dialogue and does not also dodge.
- A modal context may bind Escape. While it is active, Escape goes to the game, closing its screen, instead of opening the pause menu. Outside modal contexts Escape stays reserved.
- Duplicate keys and rebinding swaps are resolved per context, so the same key can mean different things in different contexts.

## Pointer and typing

A layout with `Pointer = true` reads the free cursor while playing. It cannot also use captured mouse look.

```csharp
new GameInputLayout(
[
    new("use", "Use", GameControl.Key(KeyboardKey.K), GameControl.Mouse(MouseButton.Primary)),
])
{
    Pointer = true,
};

public void FixedUpdate(GameTick tick)
{
    GamePointer cursor = tick.Input.Pointer;
    if (cursor.Present && cursor.WasReleased(GamePointerButton.Primary))
    {
        Click(cursor.Position, cursor.Surface);
    }
    foreach (GameTyping typing in tick.Input.Typing)
    {
        // Printable text, or an editing key (Backspace, Delete, Tab).
    }
}
```

- The pointer is read through InputMan, from its own map below every modal context. It never consumes anything. A mouse button bound to an action still reaches the pointer, unless a modal context binds that button.
- `Position` is in surface pixels, the same space as `GameViewRequest`. `Surface` is that surface's size, so headless games also know what the coordinates mean. The host maps HiDPI window units to pixels (`SetPointerSurface`).
- Clicks, wheel notches and typing that happen between ticks are latched for the next tick, like action presses. Unlike action presses they are not saved.
- Typing is window text, not key bindings, so letters still arrive as actions too. A game that edits text switches to a modal context that binds only the keys that leave the text box.
- Anything typed over a shell menu is dropped. A screen change releases held buttons but keeps the cursor position.
- `IGame.ActionPoint(action, surface)` places the game's own clickable elements, so playtest scripts can `click-action` them by name. While playing, the `pointer`, `click`, `scroll` and `text` steps go to the game.

## Importing pre-shell saves

A game that had its own save format before moving to the shell can set `Legacy` on its `GameSaveSchema<T>`: a
function that reads the old save and returns it as the current snapshot type, or null. When the menu slot (`slot-1`)
is empty at startup, the shell calls it once, validates the result and writes it there with a fresh clock, so Load
Game offers it. `GameSession.ImportedLegacySave` reports that this happened. TinyFarm imports its last pre-shell
slot this way.

## Gamepad

The first connected gamepad works everywhere without game code:
- The shell menus use the d-pad, South to confirm, and East or Start to go back.
- Start opens the pause menu.
- A game action can take a fixed pad button beside its keys:
  `new("jump", "Jump", GameControl.Key(KeyboardKey.Space)) { Gamepad = GameControl.Gamepad(GamepadButton.South) }`.
  Within a context, each pad button may mean only one action.
- `GameInputState.LeftStick` and `RightStick` read both sticks through InputMan. X points right and Y up, a radial
  dead zone of 0.2 is applied, and the range outside it is rescaled to 0..1. TinyFarm adds the left stick to its WASD
  movement.

Pad buttons are not rebindable yet; capture-rebinding offers keys and mouse buttons.

## Save versions and checkpoints

```csharp
public GameSaveSchema Saves { get; } = new GameSaveSchema<FarmV2>(FarmJson.Default.FarmV2, "farm", Capture, Validate, Restore)
{
    Version = 2,
    Migrations = [new GameSaveMigration(1, state => RenameCountToTotal(state))], // JsonNode in, JsonNode out
};

public void FixedUpdate(GameTick tick)
{
    if (playerSlept) context.RequestSave();   // checkpoint into the slot the menus load
}
```

- `Version` is written into every save. Loading an older save runs the migrations one step at a time (on the game's
  state JSON; the shell carries the clock and latched input across unchanged), then validates the result. A save
  from a newer version, or one with no path forward, is rejected before live state is touched.
- `GameContext.RequestSave(slot)` from `FixedUpdate` captures the state right after that tick, so a checkpoint matches
  the simulation clock exactly. Serialization happens then; the file write continues in the background, and
  checkpoints to a slot land in order. Failures appear as `persistence.error`, and `save.checkpoints` counts them.

## 2D layers

`View` draws an optional 3D scene. A 2D game, or a 3D game with its own 2D presentation, adds native layers
(`INativeLayerPresenter`: ordered sprite, tile or UI passes) to `NativeGameGraphics.Layers` in `AttachGraphics` and
removes them in `DetachGraphics`.

- Layers draw in ascending order after the 3D view and before the shell's HUD and menus.
- When `View` returns null, the first layer pass clears to `NativeGameGraphics.Clear`.
- The shell retargets layers whenever it rebuilds the surface (resize, resolution or vsync change).

## What the shell owns

| Concern | Behaviour |
| --- | --- |
| Window | Resizable Silk/Vulkan window: windowed, borderless (monitor-sized) or exclusive fullscreen; vsync on or off |
| Resolution changes | `NativeGameGraphics.Reconfigure` rebuilds the swapchain, frame target, renderer and overlay presenter. The plant, compiled shaders, font atlas, particle pool, prepared models and published lighting are kept |
| UI scale | Menus and HUD lay out on a logical surface of at least 960x600 and are scaled to the framebuffer, so text stays the same relative size at 1080p and 4K |
| Title screen | New game, Load game (when the game has saves and a slot exists), Settings, Quit |
| Pause | Escape (reserved) or focus loss. Resume, Save game, Load game, Settings, Quit to title. Gameplay sound buses pause with the world; music and menu sounds continue |
| Settings | Display (resolution, window mode, vsync, graphics quality), applied with an explicit **Apply**. Audio (master, music, effects), applied immediately. Controls (mouse sensitivity, invert Y, key bindings) |
| Key bindings | Pick an action, press a key. A conflicting action receives the old key. Escape cancels; capture times out after 10 seconds; **Reset to defaults** restores the layout. `Secondary` controls are fixed alternatives; `Rebindable: false` actions (for example arrow-key turning) are not offered for rebinding |
| Graphics quality | High is the game's authored `Graphics3DSettings`. Medium removes ambient occlusion and limits local shadows. Low also removes temporal AA, shadows and bloom while keeping the authored lighting model |
| Persistence | Preferences live in a Deliverance `preferences` slot under `saves/config`, independent of world saves. Bindings saved for an older layout are rejected as a whole and reported; other preferences are kept |
| World saves | `GameShellSave` holds the game's typed snapshot, the cadence phase, and latched input for a tick that has not run yet. Identity, schema and cadence are validated before any live state is replaced |
| Audio | One `AurelianAudioRuntime`. Clips load from 16-bit PCM WAV (`AudioResources.LoadPcmWav`) or MP3 (`LoadMp3`, or `Mp3Decoder.Decode` on a worker thread for long music). Master/Music/Sfx/Ambient/UI bus volumes follow settings. `GameContext.PlaySound` issues unique event ids; the NAudio device is used interactively and a null device for playtests |
| Playtesting | `GamePlaytestTarget` drives any session headless or native (see below) |

Every menu is a `GameMenuPage` built from the same template. Rows can carry a value and be adjustable (left/right, or click to step forward). Pages hold up to nine rows.

## Launch options

```
--headless --playtest-script PATH | --playtest-stdio   deterministic run without a window
--playtest-script PATH                                 the same script against the native Vulkan window
--launch-smoke                                         four interactive host frames, then exit
--visible  --output DIR  --save-root DIR
--resolution 1920x1080  --windowed | --borderless | --fullscreen  --quality low|medium|high  --basic-graphics
--gpu-rays                                             request VK_KHR_ray_query on the plant
```

Display overrides apply to this run only. Changes made in the Display menu are persisted.

## Playtest commands

`GamePlaytestTarget` supports the shared runner's steps (keys, mouse, `click-action`, `focus`, `resize`, `capture`, `checkpoint`/`rewind`) and these commands:

| Command | Effect |
| --- | --- |
| `assert FACT VALUE` | Compare any fact: shell facts (`screen`, `tick`, `time`, `display.resolution`, `display.mode`, `display.quality`, `audio.master`, `controls.sensitivity`, `binding.<action>`, `menu.selected`, `rebind`) or the game's own |
| `activate ID` / `adjust ID +1\|-1` | Press or step a menu row by id |
| `save S` / `load S` | World save slots |
| `mark M` / `assert-mark M` | Compare the full save envelope (or all facts, for games without saves) |

A rewind also restores the preferences the recorded inputs ran under, in memory only. Persisted save files are not rolled back.

`Games/Starter/Playtests/shell-proof.json` exercises the shell natively: resolution change, borderless, fullscreen, quality, key capture and cancel, window resize during play, and pause on focus loss. Its captures are written at the actual surface size.

## Current bounds

- On a real display, exclusive fullscreen at a non-native resolution asks GLFW for the closest video mode. The headless Xvfb proof cannot change modes and reports the monitor's size.
- The pointer map only knows the primary, secondary and middle buttons. Back and forward can still be bound to actions.
- Gamepad buttons and sticks work (see Gamepad above), but capture-rebinding only offers keyboard keys and mouse buttons, and only the first pad is read.
- All eight applications under `Games/` use this shell. Specialized proof modes may still construct offscreen renderers or historical proof applications.

## Existing applications

| Application | Shell adapter | Game-owned presentation / rules |
| --- | --- | --- |
| Beacon Run | `BeaconShellGame` | Existing arena, agent simulation, optional humanoid and GPU ray queries |
| Mossward | `MosswardGame` | Native profiles, fog, HUD, selection and strategy intents at 10 Hz (input at 60 Hz) |
| Sunkill | `SunkillGame` | Ariadne dialogue, original artwork and Machina dialogue UI |
| CharacterLab | `CharacterLabGame` | Humanoid animation policies and GPU skinning |
| Starter | `StarterGame` | Composed FPS/TPS fragments |
| TinyFarm | `TinyFarmApp` | Farming world, game screens and checkpoint saves |
| Cloudstep | `CloudstepGame` | Platforming course, camera and save schema |
| Hearth | `HearthGame` | Fire/smoke showcase and camera |

Normal launches and `--playtest-script` use `GameApp`; `--proof`, Sunkill's `--m14-proof` / `--m15-proof`, and Beacon's lighting authoring/qualification flags retain their specialized qualification paths. Game-specific launch arguments such as `--body`, `--humanoid`, `--locomotion` and `--lighting` still configure the game's adapter.

Sunkill's dialogue buttons use the shell pointer input and the existing aspect-preserving Machina viewport transform. Escape opens the common pause/save menu. Old slot 1 at `%LOCALAPPDATA%/SUNKILL/Saves` is imported only when the new slot is empty; the old files remain intact. The new application id is `sunkill-vn`, so its `%LOCALAPPDATA%/sunkill-vn/saves` directory cannot collide with the legacy directory on case-insensitive Windows filesystems. Its old standalone menus and persistence classes remain for historical proofs. Slots 2 and 3 are not automatically imported.

Mossward's native passes render a logical 1280x800 canvas into the shell target. `VulkanOrderedQuadRenderer.SetLogicalSize` keeps its geometry aligned through resize without replacing the plant or texture caches. Pointer positions use the inverse scale. C cancels selection/placement; Escape is the common pause action. Beacon's completion message stays in the HUD, with Enter to restart the run.

Only games supplying `IGame.Saves` show save/load actions. This migration does not invent world checkpoints for Beacon, Mossward, CharacterLab or Hearth. All games persist their shell preferences separately from world saves.

`Games/Playtests/common-shell.json` runs the same native menu, pause, settings, target recreation, resize and focus checks against all games. Its headless companion omits captures. The migration regression tests exercise real Beacon movement/rebinding, Sunkill legacy import/save restoration and odd-resolution dialogue clicking, and Mossward's cadence/actions/reset.
