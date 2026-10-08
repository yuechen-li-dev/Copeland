# TinyFarm title menu and launch input fix

Outcome: **Success**. Normal TinyFarm launches now show generated title art with clickable New Game, Load Game and Quit buttons. Enter confirms the selected row, Up/Down selects, N loads the local checkpoint and Q quits. Load is disabled when there is no checkpoint. New Game begins the fresh boot state without overwriting the saved checkpoint.

## Reused UI and input ownership

The title screen follows Sunkill's `VnMachinaLayer.BuildMenu` / `MenuButton` pattern: existing Machina `StandardUI.Button`, selected-row styling, anchored layout and named actions. TinyFarm retains its own game dispatcher and Deliverance save owner. The title is included in native menu pointer routing. InputMan's UI action map captures input while gameplay maps are absent; title navigation cannot move or attack with the player.

The stuck intro had a native focus bug: normal windows initialized `focused` to false, and initial OS focus could precede focus event subscriptions. Hidden proofs forced focus and missed this case. Launch now seeds focus from the actual foreground HWND through a source-generated `LibraryImport`; subsequent window focus events continue to own changes. No runtime reflection was added.

## Art and viewport behavior

The generated painting is `Games/TinyFarm/TinyFarm.Native/Assets/GateA/title-sleeping-spring.png` (1672 × 941). SHA-256 and prompt provenance are in `artifacts/tinyfarm-title-menu/art-provenance.json`. It is uploaded through the existing painterly resource scope and uses linear filtering. UI text and buttons are separate from the painting.

The title painting covers the physical framebuffer using centered, aspect-preserving UV cropping. The menu uses the existing scalable 1280 × 720 logical UI layout. Modal dimming is a zero-radius rectangle covering the actual framebuffer, independent of that fitted UI canvas. This removes the uncovered strip and rounded dimmer corners at larger and non-16:9 sizes. The pause panel itself keeps its existing layout.

## Validation

- `dotnet test TinyFarm.slnx -c Release -m:1 --nologo -v:q`: 419 TinyFarm and 27 Spatial2D tests passed.
- Reflection-disabled focused title tests: 8 passed. Includes actual Machina hit testing, disabled Load, release-outside cancellation, InputMan isolation, checkpoint restoration and cover UV ratios.
- Native Release build with `JsonSerializerIsReflectionEnabledByDefault=false`: zero warnings/errors.
- Visible Vulkan proofs on NVIDIA RTX 3070: mouse New Game, Enter, checkpoint Load and Quit passed, all with real initial HWND focus rather than forced hidden-proof focus.
- Exact framebuffer captures: 1280 × 720, 1920 × 1080, 2560 × 1440 and 1600 × 1000. A 1600 × 1000 pause capture verifies full-screen modal coverage. Captures were visually inspected at 1440p and non-16:9.
- Semantic hashes remain identical through title resizing, New Game presentation, pause/resume and initial checkpoint restoration. Existing A1/A2 work is preserved.
- `git diff --check` passed.

Native proof commands, from repository root:

```powershell
dotnet Games/TinyFarm/TinyFarm.Native/bin/Release/net10.0-windows/TinyFarm.Native.dll --title-proof
dotnet Games/TinyFarm/TinyFarm.Native/bin/Release/net10.0-windows/TinyFarm.Native.dll --title-proof --title-keyboard
dotnet Games/TinyFarm/TinyFarm.Native/bin/Release/net10.0-windows/TinyFarm.Native.dll --title-proof --title-load
dotnet Games/TinyFarm/TinyFarm.Native/bin/Release/net10.0-windows/TinyFarm.Native.dll --title-proof --title-quit
```

The first command creates an isolated proof checkpoint for the Load command. Captures temporarily hide window decorations to reach exact framebuffer dimensions; normal launches retain their ordinary window. Proof input is injected into the existing portable host queues and real Machina hit-testing path; OS event delivery itself is not automated by this proof.

Launch normally with `Play-TinyFarm.cmd`. Evidence and screenshots are in `artifacts/tinyfarm-title-menu/`.
