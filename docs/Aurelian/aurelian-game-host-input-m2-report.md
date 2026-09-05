# AURELIAN-GAME-HOST-INPUT-M2 report

## Outcome

**Outcome A — InputMan is the Aurelian input substrate.** The qualified path is:

```text
Silk.NET window/device events
  -> InputMan.Aurelian physical accumulator
  -> InputMan.Core maps/bindings/processors/consumption/rebinding
  -> immutable InputFrame
  -> TinyFarm.InputMan controller
  -> typed GameIntent
  -> TinyFarm resolver
```

Machina/Aurelian composition remains focus and capture authority. `InputContextPolicy` translates its focus/capture/opaque state into active InputMan maps. Opaque UI activates only UI; transparent focused UI layers UI over Gameplay and rely on binding consumption. The adapter does not mutate game state, and gameplay replay still begins at semantic intents.

## InputMan audit

| Concern | Current law before M2 | Keep / change / delete | Reason |
| --- | --- | --- | --- |
| Core package | Engine-free evaluator on `net8.0` | Keep and modernize | Correct ownership and independently publishable. |
| Typed IDs | `ActionId`, `AxisId`, `Axis2Id`, `ActionMapId` record structs | Keep | Strong programmatic boundary; strings remain persistence form. |
| Physical identity | `DeviceKind + byte index + int code`; adapters supplied engine enum numbers | Change | Retain compact key but add Core-owned keyboard/mouse/standard-gamepad enums and stable paths. |
| Physical snapshots | Caller-owned dictionaries described as “immutable-ish” | Change | Snapshot now copies into read-only dictionaries. |
| Edges | Previous/current physical polling | Keep and repair | Correct deterministic basis; aggregate logical actions now OR multiple bindings before deriving edges. |
| Maps | Active-map refcounts and priority sort | Keep and stabilize | Add ordinal tie break for deterministic equal priorities. |
| Consumption | Modes existed, but a consumed control only blocked a lower binding that also requested consumption; `ActionOnly` was not enforced | Change | Central consumed-control/action sets now block every lower binding correctly. |
| Axes | Contributions sum; ordinary axes clamp; delta-driven logical axes remain unclamped | Keep | Useful and deterministic combining law. |
| Axis2 | Named X/Y projection | Keep | Small, explicit logical composition. |
| Chords | Effective primary + all modifiers with previous/current edge derivation | Keep | Modifier release naturally releases the effective chord. |
| Processors | Deadzone, scale, invert | Keep and add clamp | Clamp is the one additional generally useful scalar processor; no speculative curve DSL. |
| Rebinding | Session, seeding, candidates, timeout, chords, manager persistence | Keep and simplify policy | Candidate frames now suppress normal maps; conflict behavior is explicit. |
| Conflict handling | Two rejection booleans | Change | Add `Allow`, `Reject`, `ReplaceExisting`; replace swaps the displaced binding to avoid silent deletion. |
| Candidate discovery | Primarily Stride helper lists | Change | Core exposes portable keyboard/mouse/gamepad candidate lists; adapters can extend them. |
| Display metadata | `Binding.Name` and numeric `ControlKey.ToString()` | Change | Stable `ControlPath` and `BindingPrompts` return readable labels. |
| Profile authority | Mutable typed DTO graph | Keep | It remains runtime semantic authority; validation occurs after authoring/load. |
| JSON | Core serializer and adapter-specific file stores; documented as default | Change | Retained as optional legacy serializer/import; no longer preferred. |
| TOML | Mentioned only as possible plugin | Add separate package | Human-readable canonical v1 persistence without making runtime dictionary-driven. |
| Stride system | Stride lifecycle/service registration, candidate enums, storage | Keep as optional legacy adapter | Still useful, but not Core law or primary positioning. |
| MonoGame system | Polling adapter and JSON storage | Keep as optional legacy adapter | Compatibility only; native Aurelian uses the new adapter. |
| F# overload duplication | Extra overloads in `Bind` | Keep for now | Harmless compatibility; not copied into the modern surface. |
| Legacy `InputOptions.DefaultDeadzone` | Global field largely superseded by explicit processors | Keep serialized | Avoid semantic loss for old profiles; modern samples use per-binding deadzone. |
| Core touch/gesture kinds | Enum values without qualified implementation | Keep reserved, do not expand | Removing breaks serialized identity; no new touch/gesture abstraction was built. |

## Retained, changed, and removed concepts

Retained: action maps, priority, control/action consumption, actions, scalar axes, delta axes, Axis2, chords, ordered processors, typed IDs, profile validation, pluggable storage, rebinding sessions/managers, and optional Stride/MonoGame adapters.

Changed: portable control identity is now authored in Core; snapshots and logical frames are immutable; multiple physical bindings aggregate before logical edge derivation; consumption works across lower maps; map tie ordering is deterministic; rebinding has explicit conflict policy and consumes its capture frame; TOML is preferred; documentation is engine-neutral; package version is `0.2.0`.

Removed from the preferred path: Stride enum references, startup-script ceremony, direct DTO dictionary construction, JSON-default positioning, and direct game polling of physical keys. No compatibility adapter was deleted.

## Package structure and dependency law

- `InputMan.Core` (`InputMan` repository): no Aurelian, Stride, MonoGame, or TOML dependency.
- `InputMan.Toml` (`InputMan` repository): references Core and owns canonical persistence/storage.
- `InputMan.StrideConn` and `InputMan.MonoGameConn`: optional legacy leaves.
- `Aurelian.GameHost` (`Copeland` repository): window/frame/resize/focus/bootstrap contracts and deterministic lifecycle; no InputMan dependency.
- `Aurelian.GameHost.Silk`: thin `IWindow` event/pump adapter.
- `InputMan.Aurelian`: references Core plus Aurelian host/composition and Silk input; owns native device subscriptions and physical accumulation.
- `Aurelian.NativeComposition`: `NativeGameHostCompositor` adapts the already-qualified native world+Machina compositor to host resize/present/disposal.
- `TinyFarm.InputMan`: app-owned logical ID declarations and `InputFrame -> TinyFarmInputCommand/GameIntent` lowering.

Copeland consumes InputMan by a sibling project reference during development. No Core source was copied into Aurelian.

## Authoring, IDs, controls, and frames

`GameControls.cs` demonstrates the intended 90% surface: `Input.Profile`, `Input.Map`, collection-spread `Input.Wasd`/`Input.GamepadLeftStick`, and direct `Bind.Action`/`Bind.ActionChord`. User code declares static readonly typed IDs. Game meaning appears only in those opaque IDs and in the app controller.

Portable controls use `KeyboardKey`, `MouseButton`, `MouseAxis`, `GamepadButton`, and `GamepadAxis`, plus a bounded byte device index. Standard gamepad naming is position-neutral (`South`, `East`, left/right sticks); Silk button indices are translated at the adapter boundary. Keyboard, mouse buttons, position/delta/wheel, standard gamepad buttons/sticks/triggers, connection, and focus are covered by the native adapter.

Each tick produces an immutable `InputFrame` with sequence, delta time, actions, scalar axes, Axis2 values, and optional last-active-device. Actions are `Up`, `Pressed`, `Held`, or `Released`. Last-active-device updates from a stable physical-control ordering. It is prompt metadata only, never mapping authority.

For scalar axes, active contributions sum in map/binding evaluation order. Ordinary logical axes clamp to `[-1,1]`; an axis with a delta source is unbounded. Axis2 is the declared `(X,Y)` pair. Processors run in authored order: deadzone, scale, invert, and clamp are supported in TOML/JSON.

## Maps, consumption, UI, focus, and devices

Higher integer priority runs first; equal priority uses ordinal map ID order. A map can disable consumption globally. A triggered binding can consume its physical control, logical action, or both; the resulting set suppresses all lower maps.

Machina capture/focus is not duplicated. `InputContextPolicy.Apply(LayerInputRoutingResult, uiLayer, uiIsOpaque, rebinding)` selects `Rebind`, opaque `UI`, transparent focused `UI + Gameplay`, or `Gameplay`. The UI map consumes shared confirm/cancel controls in the layered case. The real native slice proves `E` reaches UI and not `Interact`, then reaches `Interact` again after UI closes.

On focus loss, the adapter clears accumulated controls and Core releases held actions, zeros axes, clears previous physical history, and cancels rebinding. Focus regain starts empty; no stale press is synthesized. Gamepad disconnect removes all state for that device; reconnect begins cleanly. Hardware hot-plug was not physically exercised in this environment, but native Silk connection events are wired and simulated connect/disconnect is tested.

The bounded `InputDeviceAssignment` filters snapshots for `PlayerIndex`, optional keyboard/mouse, and a set of gamepad indices. It is sufficient for `P0 = keyboard + pad0`, `P1 = pad1`; join/lobby/controller ownership remains application policy. `LastActiveDevice` was added because the implementation was small and directly supports prompt switching.

Haptics were not added. Rumble is device output, not physical-to-logical input mapping; it belongs beside a future Aurelian device-output seam when a real consumer exists.

## TOML and JSON law

TOML format v1 has explicit `[[maps]]`, nested `[[maps.bindings]]`, `[[axis2]]`, portable control paths, output discriminators, edge/threshold, consumption, modifiers, and ordered processor strings. Writer order is priority-descending/map-ID, binding-name, and Axis2-ID; numeric formatting is invariant and output uses LF. File replacement is atomic.

`formatVersion = 1` is mandatory. Missing or unsupported versions throw `NotSupportedException`; no migration framework exists. `LayeredTomlProfileStorage` selects one complete profile in this order: user TOML, bundled TOML, code default. It deliberately does not perform ambiguous field-level merges.

JSON remains an optional serializer/import path. Existing JSON can load into typed `InputProfile` and then save as canonical TOML. It is not default persistence. Adapter-specific historical numeric codes remain adapter compatibility data; new profiles should use portable controls.

## Rebinding law

`RebindingManager` owns begin/status/cancel/complete and storage save. `RebindRequest` owns candidate buttons/axes, allowed device kinds, forbidden controls, timeout, chord modifiers, and conflict policy. Initial state is seeded so an already-held key cannot bind. A new press or axis threshold crossing captures. The entire rebind frame is reserved from normal maps even when capture completes that tick.

`Reject` returns a visible failure without mutation. `Allow` permits duplicates. `ReplaceExisting` swaps the previous control into conflicting slots and installs the candidate in the requested slot, preserving an explicit binding rather than silently deleting it. The proof rebinds `Interact.Keyboard` from E to F, persists TOML, reloads, and verifies E is inactive while F triggers.

Candidate controls come from `CandidateControls` in Core or adapter-provided extensions. `BindingPrompts.ForAction` returns the binding name, stable label (`Keyboard.0.E`, `Gamepad.0.South`), device kind, and index. Glyph art is out of scope.

## Host/bootstrap, resize, and shutdown

`AurelianGameHost` owns event pumping, monotonically sequenced frame timing, input snapshot finalization, simulation callback, render callback, compositor present, focus propagation, resize propagation, application/config roots, and deterministic disposal. It has no `Awake/Start/Update/LateUpdate` gameplay model and owns no game truth.

Resize flows from `SilkGameWindowAdapter.FramebufferResize` to host, then native compositor target recreation and the application `OnResize`; the application uses that callback for camera viewport and Machina layout. The existing native compositor resize proof still checks the 2560x1440 target and layer layouts.

Shutdown order is application (therefore its world/Machina resources), input adapter, native compositor, then window/device. Every component is `IDisposable`; no finalizer is relied upon. Disposal aggregates failures while continuing to release remaining owners.

The host exposes `%LocalAppData%/<ApplicationName>` and its `config` child, but owns no save schema.

## Controller and replay boundary

`TinyFarmInputController` maps logical `Move`, `Interact`, `Hotbar1/2`, `Pause`, and inventory commands. Movement emits `SpatialMoveIntent`; interaction and hotbar selection emit their existing typed intents. Pause/inventory remain app/host commands. The adapter never directly mutates `TinyFarmState`.

Replay authority begins at `SubmitGameIntent -> GameIntent -> IntentEnvelope`. Physical callbacks and logical frames remain inspectable diagnostic input, not gameplay replay records. Dominatus controllers remain peer semantic-intent producers and were not changed.

## Proof and validation

The native proof now runs the new host around the real Vulkan world+Machina compositor after its M0 regression checks. It demonstrates keyboard W and gamepad-left-stick parity, UI consumption, gameplay resume, focus-loss stop, clean regain, host disposal, and unchanged world/UI native order. The composed image hash remained `539030cf04b60cad870114569eaf05479c2de2b7c20b644339714005278cf3dd`; Vulkan validation was enabled with zero errors; 100 warm frames remained stable.

Validation totals:

- InputMan solution: 90/90 passed (Core 64, MonoGame 19, Stride 7).
- Aurelian solution: 669/669 passed, including 4 new host/input integration tests.
- TinyFarm solution: 273/273 passed.
- Native world+Machina+host slice: Outcome A; 3 direct passes, 9 draws, 100 stable warm frames, validation errors 0.
- `dotnet build Aurelian.slnx` and the final standalone native proof build: 0 warnings, 0 errors.

The new focused tests cover action edges and multi-binding aggregation, scalar/Axis2/digital/analog axes, deadzone/scale/invert/clamp, chord modifier release, priority and consumption, map activation through composition state, focus loss, gamepad disconnect, player assignment, last-active-device, rebinding/candidate-frame isolation/conflict replacement, prompt metadata, TOML canonical roundtrip/version rejection, JSON import, keyboard/gamepad intent parity, resize forwarding, host sequencing, and disposal.

## Artifacts and diffs

- `artifacts/aurelian-game-host-input-m2/manifest.json` — requested machine-readable milestone flags.
- `artifacts/aurelian-game-host-input-m2/input-profile.toml` — canonical authored profile.
- `artifacts/aurelian-native-layer-compositor-m0/world-machina-1280x720.png` — unchanged native composed visual proof.
- InputMan diff: Core modernization, new TOML package/tests/docs, compatibility fixes in legacy adapters.
- Copeland diff: GameHost/Silk/InputMan/native-composition packages, TinyFarm controller bridge/sample controls, integration tests, native proof extension, solution wiring, and this report.

## Exact next milestone

`AURELIAN-SPATIAL-2D-M3`.
