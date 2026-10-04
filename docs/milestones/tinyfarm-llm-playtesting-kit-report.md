# TinyFarm LLM playtesting kit

Outcome: **Success**. TinyFarm.Cli can execute timed emulated input, seeded fuzzing, semantic operations, assertions, and persistent stdin sessions. Headless and native Vulkan runs use the same InputMan/application cadence and Machina menu hit testing. Native scripts produce real PNG captures without desktop automation.

The earlier TinyFarm.Runner exposes historical milestone scenarios and an older semantic parser. This pass adds a focused CLI for the current A3 shipping-chest campaign, while retaining that parser and its resolver authority. It does not add another input system, gameplay model, save format, remote service, or UI framework.

## Implementation

- `TinyFarm.UI` now owns the existing menu presentation code through single compile ownership. Native rendering and headless playtesting share `TinyFarmInputPump` and `TinyFarmMenuInputRouter`. The linked source files retain their historical paths/namespaces to keep this extraction local.
- `TinyFarm.Playtesting` owns source-generated script/observation JSON, validation, fixed 60 Hz timed input, seeded xorshift fuzz generation, semantic command dispatch, state assertions, artifact writing, and input cleanup.
- `TinyFarm.Cli` owns one-shot commands, scripts, fuzzing, and stdin sessions. Native mode launches the existing TinyFarm.Native process with its playtest driver and forwards its exit code. The parent does not synthesize OS-wide input or run a background service.
- `TinyFarmGame.Save/Load` accept an optional named slot through the existing Deliverance path. Default gameplay slots and their UI availability remain authoritative. A shared `OpenPause` helper serves both gameplay input and semantic tooling.
- Native playtest focus is explicitly injected, independent of the desktop foreground window; ordinary native launches retain their OS focus behavior. Native startup errors go into the chosen playtest output directory.

Input scripts can hold simultaneous keys, press/release, tap, move/click a normalized pointer, enter text, scroll, resize, change focus, invoke semantic commands, and capture frames. `click-action` resolves the actual Machina layout target and performs a down/up click; `ui` deliberately invokes a semantic menu action. Menu snapshots expose those targets for agents to discover, rather than requiring guesses about hardcoded button positions.

All new JSON graphs use source generation. Save/load and container actions use their established owners, including proximity, collision, key-item, equipment, and inventory checks. HUD, resize, and menu inspection do not enter gameplay hash authority.

## Verification

`dotnet build TinyFarm.slnx -c Release -m:1` passed with zero warnings/errors. The full solution test run passed **449 TinyFarm tests + 27 Spatial2D tests = 476**. Thirteen new focused playtesting cases also passed with `JsonSerializerIsReflectionEnabledByDefault=false`; CLI and native Release builds and real script executions were qualified with that setting. This does not claim a migration of unrelated historical reflection-based proof serializers.

Real CLI evidence under `artifacts/tinyfarm-playtesting/`:

| Evidence | Result |
| --- | --- |
| `input/`, `input-native/` | Same input script; identical final world hash, field hash, scene, position, and screen |
| `native/` | Actual title, 1080p world, focused inventory filter, and 2560×1440 world PNGs; visually inspected filter and 1440p world |
| `fuzz/`, `fuzz-native/` | Seed 25, 100 generated steps plus initial start; all 101 records execute, final world/field/position agree |
| `shell/`, `shell-native/` | Real stdin transcripts, including search text plus held W with unchanged semantic state |
| `container/`, `container-native/` | Walk to chest using raw input, open semantically, deposit two seeds, withdraw one, assert authoritative counts, save/load and restore open window |
| `fresh-load/` | Separate CLI process loads the named slot from a chosen save store |
| `expected-failure/`, `error-proof.json` | Failing assertion exits 1, writes the failing step and final state |
| `tests/` | Full-suite and reflection-disabled focused TRX results |

The input script exercises opposing W/S, a three-second W hold with a focused textbox, UI capture/action maps, custom save restoration, resize, and focus loss/regain. Assertions compare both world and field hashes. The container script ends with five seeds on the player and one in the open shipping chest after restoring the save.

## Boundaries

Headless mode qualifies game/input/menu behavior and produces observations; it cannot produce raster captures. Native captures require a working Vulkan device. Scripts launch an owned instance, rather than attaching to an already-running game. Use hidden native runs for reproducibility; visible windows may receive human input. InputMan's existing portable key catalog is retained; unsupported keys fail explicitly. Character input is separate from held keys, and OS key-repeat is not synthesized.

A repeat of the older visible `--container-proof` stopped at its existing foreground-focus assertion before its gameplay checks. Its exact diagnostic is saved as `legacy-visible-proof-focus-stop.txt`. This is not reported as a passed OS-focus proof. The new native container/input scripts pass using the explicitly emulated focus contract, and normal shipping gameplay regression tests pass.

The working tree also contains the prior A3 shipping-chest work. This pass preserves it; no staging or commit was performed. The changes here are the three project/tooling seams, shared input/menu routing, named-slot/pause helpers, native driver, wrapper, examples, focused tests, docs, and captured evidence.

Usage and script/command contracts: [TinyFarm playtesting](../tooling/tinyfarm-playtesting.md).
