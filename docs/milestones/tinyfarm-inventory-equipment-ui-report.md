# TinyFarm inventory, equipment and pause/save menus

Outcome: **Success**. The opening slice now has an interactive table inventory, two equipment slots and a pause/save menu in its real Aurelian Vulkan presentation. Native captures and automated input demonstrate the motivating flow: inspect pockets, unequip the sword, observe that attacking is blocked, save that loadout, change it and load the saved loadout back.

## Try it

Launch `Play-TinyFarm.cmd` from the repository. Enter starts the game. **I** opens Inventory & Equipment; **Escape** opens the pause menu. Menus pause world simulation. Closing Inventory returns to gameplay or the pause menu that opened it.

- Click a category, type in the search field, or click a column heading to sort. Clicking the same heading reverses its order.
- Click a row to inspect it. Equip/Unequip changes the selected sword or axe slot. Broth exposes Eat when healing is possible; planting and cooking retain their existing world interactions.
- Up/Down selects rows, Left/Right changes category, Enter applies the selected action. The wheel and Previous/Next navigate a bounded window of ten rows.
- Click search or press Tab to focus it. Backspace removes a character; Delete clears it. Enter or Escape leaves search focus before acting on/closing the menu. Search accepts up to 48 printable characters and matches name or category without case sensitivity.
- Pause offers Resume, Inventory & Equipment, Save checkpoint, Load checkpoint and Quit. Missing checkpoints disable Load; Load and the Quit button require confirmation. Save uses the existing local Deliverance checkpoint.
- Existing F/N save/load shortcuts remain. Existing Q is a quick quit from non-playing screens, blocked while persistence is pending. Search focus suppresses those letter shortcuts.
- F9 still controls the normal HUD; interactive modal menus remain visible when that HUD is hidden.

Normal saves remain under `%LOCALAPPDATA%\TinyFarm\saves`. The proof uses a separate, ignored directory under `artifacts/tinyfarm-ui-subsystems/saves` and does not overwrite the player's slot.

## Ownership and reuse

Inventory rows project the existing unique-item inventory and product stacks. They do not create another inventory store. Columns are Name, Category, Quantity, per-item Value and Equipped. Identity gear and product stacks retain separate row keys. Values come from existing sale rules; no invented weight, damage progression or armor statistics were added.

Equipment truth lives in TinyFarm.Core. `SetEquipmentIntent` validates player ownership, item/slot compatibility and active swing/dodge restrictions. The sword is the Weapon slot; the axe is the Tool slot. Unequipping actually prevents sword use or chopping. Existing tool selection remains separate. Owned legacy tools retain their implicit equipped behavior until the first loadout change.

Actual changes create version-12 explicit equipment state, with generated save/replay serialization and canonical hashing. Version-10 supper saves and version-11 opening-slice saves remain supported. Historical partial-world versions cannot be promoted by an equipment action. Removing a unique item from player ownership clears an explicit slot. Filters, sorting, focus, selection, scroll and confirmations are transient presentation state and never enter semantic hashes or checkpoints.

Menus are authored in Machina.UI and presented by TinyFarm's existing Aurelian Vulkan renderer. Sunkill's `StandardUI.Button` supplies the controls and action/hit-test conventions. The reusable `Machina.Standard.Authoring.UiDataTable` owns a clipped, selectable visible table window; the host owns querying, selection and paging. A shared `UiTableLayout` carries the notebook's unchanged width sampling and row geometry policy. Standard buttons gained optional horizontal padding, defaulting to zero for existing callers.

**Oblivion renderer audit:** the standalone notebook in this checkout still initializes Avalonia. Its Machina shell uses `AurelianCpuRasterRenderer`; the table body is constructed by `AvaloniaOblivionContentHost`. This pass reuses its layout policy and supplies a native Machina table, but does not claim or perform a standalone Oblivion Vulkan migration.

The native Silk input bridge now forwards printable text and Backspace/Delete/Tab events through the existing composition contracts. Primary clicks use Machina's prepared hit index, require press/release on the same action and reject disabled actions. Client pointer coordinates pass through framebuffer and logical UI transforms, preserving hit alignment during resize. Search focus prevents letters from activating gameplay, save/load or quit shortcuts. No new dependency or runtime reflection dispatch was introduced.

## Engine problems fixed at their owners

The reflection-disabled native run exposed shader JSON export relying on reflection. `VdMirJson` now uses generated compute/graphics metadata. Two compatibility tests compare its output byte for byte with the old serializer's explicit reflection oracle in tests; shader realization hashes retain their established input bytes.

Resizing exposed Vulkan validation error 01282: destroying a semaphore still used by a queued presentation. Swapchain disposal now waits for device idle before releasing presentation semaphores/images. The final native run completed both resizes and teardown without validation output. No application-level resize bypass was added.

## Evidence

`artifacts/tinyfarm-ui-subsystems/native-proof.json` records the real GPU run on an NVIDIA GeForce RTX 3070. The proof injects keyboard events through InputMan and portable pointer/text events through the native host queue, then renders and reads back actual Vulkan frames. It checks:

- Inventory freezes both semantic world and environmental field hashes for 60 frames.
- HUD visibility does not close the modal or change those hashes.
- Search, editing, header sorting, equipment selection and keyboard selection work without shortcut leakage.
- Unequipping blocks sword use; the existing Deliverance save/load flow restores the unequipped loadout.
- Pointer actions remain aligned at 1920x1080, 2560x1440 and 1600x1000; resizing changes neither world nor field hash.
- An idle menu keeps its cached Machina presentation instead of rebuilding it every frame.

Eight captures cover inventory, equipment, filtered inventory, pause, load confirmation, 1440p, a non-16:9 window and HUD-hidden inventory. The sparse opening bag contains the actual axe, sword and six seeds; screenshots do not fabricate an inventory to make the table look full.

| Paused inventory frame | p50 | p95 | p99 | Draws/frame | Idle UI rebuilds | UI texture uploads |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1920x1080 | 7.0172 ms | 8.0161 ms | 8.4804 ms | 511 | 0 | 0 |
| 2560x1440 | 6.9452 ms | 8.0720 ms | 8.5553 ms | 485 | 0 | 0 |

Each measurement contains 120 host frames including submission/presentation and excludes PNG readbacks. These are paused opening-bag measurements on this GPU, not a large-inventory or active-combat benchmark. Draws include the world and text; different views account for different totals. The entire proof records 28 presentation rebuilds for actual UI/state changes and zero dynamic UI texture uploads.

Reproduce the reflection-disabled native evidence from the repository:

```powershell
dotnet build src/TinyFarm/TinyFarm.Native/TinyFarm.Native.csproj -c Release -m:1 -nodeReuse:false -p:JsonSerializerIsReflectionEnabledByDefault=false
& .\src\TinyFarm\TinyFarm.Native\bin\Release\net10.0-windows\TinyFarm.Native.exe --menus-proof
```

The final command exits zero and prints `TINYFARM_MENUS_NATIVE_QUALIFIED`. The generated proof also asserts reflection serialization is disabled. The capture dimensions and performance values are from the final run; the proof's final viewport is 1600x1000 after the resize checks.

## Validation

.NET SDK 10.0.401; Release builds with one MSBuild worker. TinyFarm solution and native builds passed without warnings or errors.

| Suite | Passed |
| --- | ---: |
| TinyFarm.Core.Tests | 373 |
| Aurelian.Spatial2D.Tests | 27 |
| Machina.Pipeline.Tests | 14 |
| Machina.Standard.Tests | 98 |
| Sunkill.Tests | 27 |
| Oblivion.UI.Tests | 19 |
| Oblivion.App.Tests | 129 |
| Copeland.TS.Tests | 1305 |
| Aurelian.Graphics.Tests | 268 |
| Aurelian.Shaders.Tests | 139 |
| **Distinct regression tests** | **2399** |

The 11 new menu/equipment tests also passed with `JsonSerializerIsReflectionEnabledByDefault=false`; they are included in the 373, not added again to the total. They cover ownership/slot rejection, action effects, old/new checkpoint compatibility, generated equipment replay, corrupted loadout rejection, view-state isolation, food consumption and empty selection, search capture, prepared Machina pointer actions and pause navigation. Full historical suites run with their existing defaults: some old milestone evidence writers and compatibility oracles use reflection. This pass qualifies the new runtime path with reflection disabled, not every historical proof writer in the repository.

Exact suite results and artifact hashes are retained in `validation.json` and `manifest.json` alongside the captures. Native stderr was empty and stdout contained no Vulkan validation errors on the final run. The automated proof qualifies event routing and rendered output; it does not substitute for the user's physical keyboard/mouse usability review or a controller text-entry pass.

## Scope and next refinement

This is the basic PC inventory/equipment and pause/save subsystem requested. Crafting remains the existing stove interaction; no crafting screen, armor progression, new item content, menu theme framework, rich text editor, IME framework or campaign expansion was built. The shared table accepts a visible window rather than creating a general spreadsheet editor. Equipment is intentionally limited to the two usable opening-slice tools.

The next useful refinement is to play the opening loop using these menus and assess action/focus clarity with the real small bag, before adding crafting or a larger equipment catalogue. This pass leaves the first-slice world and art unchanged.

Code diff against `f97ceb0229c89488eb6ee6a44d30dc48d6a19ab3`: 25 modified files and 10 new source/test files, 1,641 added lines and 43 removed lines. Documentation and evidence are excluded from that count; the manifest records the separate tracked/new-file totals.
