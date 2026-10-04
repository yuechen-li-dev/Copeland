# TinyFarm CLI playtesting

TinyFarm's playtest kit runs scripts and semantic commands against the shipping-chest campaign. It injects owned keyboard and mouse events through InputMan and the same Machina menu router used by native play. No desktop automation, global input hooks, server, or permanent runtime reflection is involved.

Build once:

```powershell
dotnet build TinyFarm.slnx -c Release -m:1
```

From the repository root:

```powershell
.\tinyfarm.cmd open inventory
.\tinyfarm.cmd run examples/tinyfarm/playtest-input.json --output artifacts/my-playtest
.\tinyfarm.cmd run examples/tinyfarm/playtest-native.json --native --output artifacts/my-native-playtest
.\tinyfarm.cmd fuzz --seed 25 --steps 100 --output artifacts/my-fuzz
.\tinyfarm.cmd shell --native --output artifacts/my-session
```

For agents, invoke the built `src/TinyFarm/TinyFarm.Cli/bin/Release/net10.0/TinyFarm.Cli.dll` with `dotnet` to avoid rebuilding for every command. The wrapper builds when needed. Native mode requires the Release TinyFarm.Native build and a working Vulkan device. Its window is hidden by default; `--visible` shows the owned instance.

## Persistent sessions

One-shot commands start a fresh game instance. Use `shell` or a script when commands must share state. Shell reads one command per stdin line, emits one JSON observation per successful command, and exits on EOF, `exit`, an in-game quit, or an error. Native shell also emits a final observation when closing. An agent can pipe a transcript or keep stdin open with its process tools:

```powershell
@(
  'new game'
  'hold w 3'
  'hold w+s 2'
  'open inventory'
  'click-action search'
  'text w'
  'mark searching'
  'hold w 3'
  'assert unchanged searching'
  'inspect'
  'exit'
) | .\tinyfarm.cmd shell --output artifacts/transcript
```

`hold` uses simultaneous physical keys. `press` persists until `release`, focus loss, failure, or session end. `tap` produces a down/up edge without advancing the simulation clock. Text entry is a separate character event, just as in the native adapter: use `text w` for typing and `hold w 3` to test whether the movement action leaks through. Supported physical key names come from InputMan's portable KeyboardKey enum, case insensitive; aliases include `esc`, `up/down/left/right`, and `1/2/3`. Unsupported physical keys are rejected explicitly; this pass does not extend InputMan's portable key catalog.

Useful commands:

| Command | Behavior |
| --- | --- |
| `hold w+s 2`, `tap enter`, `press w`, `release w` | Emulated keyboard input |
| `click 0.5 0.5` | Primary click in normalized client coordinates |
| `click-action search` | Click the current Machina action's measured center |
| `text turnip` | Native-style text event into the focused menu field |
| `wait-seconds 3` | Advance the owned game at fixed 60 Hz |
| `resize 2560 1440`, `focus off`, `focus on` | Presentation and focus tests |
| `capture world.png` | Native PNG under the output directory |
| `new game` | Begin the fresh instance through the existing Start behavior |
| `open inventory`, `open stats`, `open pause`, `close` | Existing application menus |
| `ui category:Food`, `ui sort:name` | Explicit semantic menu action, separate from mouse testing |
| `open container shipping-chest` | Existing range-checked object interaction |
| `deposit product:turnip-seed 2`, `withdraw item:axe` | Existing range/key/equipment-checked transfer resolver |
| `save slot2.sav`, `load slot2.sav` | Existing Deliverance slot persistence |
| `inspect` | Read-only semantic/menu observation |
| `mark baseline`, `assert unchanged baseline` | Compare world AND field hashes |
| `assert screen Inventory`, `assert search w`, `assert money 12` | Fail explicitly on a mismatch |
| `assert product shipping-chest turnip-seed 2` | Check the authoritative inventory count |

The existing semantic command parser remains available: `move up 256`, `go to <anchor>`, `interact`, `harvest`, `water`, `wait <game-minutes>`, etc. `sleep`, `sword`, and `eat [product-id]` also use existing typed intents. Semantic actions do not bypass collision, proximity, ingredient, or equipment checks. Rejection is a tool error with a nonzero exit code; ordinary rejected actions produced by raw emulated input remain normal gameplay observations.

Save names identify Deliverance slots: `slot2.sav` is stored as `slot2.sav.dlv`, not a separate save format. Default saves are isolated under `<output>/saves`. Reuse an output directory to load a slot in a later process, or specify `--save-dir DIR` to use a chosen store, including native game saves. Names cannot contain directories. `new game` begins the current fresh instance; start another process for a full reset.

## Script format

```json
{
  "schemaVersion": 1,
  "steps": [
    { "kind": "command", "command": "new game" },
    { "kind": "hold", "keys": ["w"], "seconds": 3 },
    { "kind": "hold", "keys": ["w", "s"], "seconds": 2 },
    { "kind": "click", "x": 0.5, "y": 0.5 },
    { "kind": "command", "command": "open inventory" }
  ]
}
```

Step kinds: `hold`, `tap`, `press`, `release`, `wait`, `pointer`, `click`, `click-action`, `text`, `scroll`, `command`, `resize`, `focus`, `capture`. `scroll` takes normalized `x/y` and a numeric wheel delta in `text`. `click-action` takes the action ID in `text`; menu targets and normalized centers are included in observations. Resize takes `width/height`; focus takes `focused`; capture takes `path`.

Scripts are validated before execution: schema 1, at most 10,000 steps, durations from 0 to 60 seconds per step, at most one hour total, finite normalized coordinates, and bounded viewport sizes. Unknown JSON properties and unknown step/key names fail. Timing uses integer ticks at 60 Hz, rounded up to a frame for fractional durations; it never waits three wall-clock seconds just to simulate three game seconds. GPU rendering can make native execution slower. Zero-time input edges and captures do not advance gameplay. Persistence is explicitly settled before observation with a ten-second timeout. Quit stops the remaining steps.

## Evidence and boundaries

Each run writes `script.json`, `trace.jsonl` (step index, input, resulting observation or error), and `final.json`. Fuzzing stores its xorshift seed and the complete generated script, so replay needs no random generation. Observations include world/field hashes, position in 1024 units per tile, time, HP/SP, money, inventories, agent placements, chest state, screen, action maps, search state, and actual Machina action centers. Captures remain inside the output directory.

Headless mode qualifies simulation, InputMan, and Machina hit testing; it does not render pixels. Native mode uses the real Aurelian host, Vulkan renderer, and framebuffer capture. Playtest focus is explicit and injectable so a hidden owned window can run reproducibly. This does not prove desktop OS focus acquisition. Visible runs may receive human input; use hidden runs for reproducible automation. The kit launches its own instance; it does not attach to an already-running game or emulate operating-system key repeat. Semantic commands intentionally provide direct application operations; use raw input/click steps to qualify controls and hit testing.

See the checked-in input, native, and container examples under `examples/tinyfarm/`.
