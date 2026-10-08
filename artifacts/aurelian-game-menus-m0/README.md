# Aurelian game menu qualification

Outcome: **Success** for the requested shared basic menu and Beacon Run integration.

The engine-owned `Aurelian.GameMenus` integration supplies the default page, configurable
shared button producer, InputMan bindings/navigation, retained Machina layout/hit testing,
and native analytic-shape/MSDF overlay. Beacon Run uses the complete template; Sunkill
and TinyFarm reuse its buttons with their existing artwork and game-owned actions.

## Actual native path

```powershell
samples/Integrations/Aurelian.Beacon3D/bin/Release/net10.0/Aurelian.Beacon3D.exe --proof --visible --output artifacts/aurelian-game-menus-m0
```

The run exited successfully on NVIDIA GeForce RTX 3070, with a native window and Vulkan
swapchain. It presented 1,305 frames, passed the menu/depth checks, collected three
beacons, and won through the InputMan/game path. `proof.json` records the measured
checks and hashes; `native-run.log` records completion. `menu-title.png`,
`menu-controls.png`, `menu-paused.png`, and `win.png` were inspected for layout.
The world remains visible behind menus; simulation freezes while they are open.

Checks include keyboard navigation, enabled-button hit geometry, mismatched release
cancellation, focus cancellation, returning from controls, held gameplay input isolation,
Space confirmation without a jump, stable repeated pixels, and retained font uploads.

## Regression evidence

- `dotnet build Aurelian.slnx -c Release -m:1`: succeeded, zero warnings/errors.
- `dotnet test Aurelian.slnx -c Release -m:1 --no-build`: 839 passed across 24 projects,
  zero failures/skips. Naga used the existing repository-local pinned executable.
- `dotnet test TinyFarm.slnx -c Release -m:1`: 476 passed, zero failures/skips.
- `dotnet build TinyFarm.slnx -c Release -m:1`: succeeded, zero warnings/errors,
  including the native and MonoGame hosts.
- Additional `Aurelian.Machina.Tests`: 42 passed, one existing threshold assertion failed.
  `AurelianGlyphRunAdapterM2Tests` expects `0.5`; `NativeMsdfParameters.Create` returns
  `0.555` for its small-glyph fixture. The test, adapter, and parameter implementation
  are unchanged from HEAD and do not call the extracted native font. See
  `machina-integration-tests.log`; this is outside the menu change.

The default template is bounded to four entries, ASCII fonts, and surfaces at least
640 by 600. Game actions, save behavior, specialized screens, and simulation remain
game-owned. This proof does not claim arbitrary UI operation support or manual playtesting
of the changed Sunkill/TinyFarm hosts.
