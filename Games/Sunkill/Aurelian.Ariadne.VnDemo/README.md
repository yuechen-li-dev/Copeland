# SUNKILL — RenC# VN M13

This project is the playable `RENC#-VN-M13` proof: a minimal native visual-novel
application profile over Ariadne, Dominatus, Deliverance, InputMan, Machina.UI,
and Aurelian. It is deliberately an application, not a new VN engine.

Launch the product from the repository root:

```powershell
.\Play-Sunkill.cmd
```

or directly:

```powershell
dotnet run --project Games/Sunkill/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj
```

Run deterministic qualification separately:

```powershell
dotnet run --project Games/Sunkill/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj -- --proof
```

Run the bounded real-window launch smoke test without nested `dotnet run`:

```powershell
pwsh ./tools/Test-SunkillLaunch.ps1
```

Normal launches use `SunkillGame : IGame` and `GameApp`: a resizable native Vulkan window with the common title, pause, display, audio and controls menus. The launch smoke verifies four swapchain presentations and clean exit; it allows shader compilation time.

Enter or Space advances dialogue, arrows select choices, and clicking activates the original Machina dialogue buttons. Escape opens the shared pause menu, including save/load and settings. World saves and preferences use the shell's `%LOCALAPPDATA%/sunkill-vn/saves` directory. The first old save at `%LOCALAPPDATA%/SUNKILL/Saves/slot-1` is imported into an empty shell slot; its original file is preserved.

`--proof`, `--m14-proof` and `--m15-proof` retain the historical RenC# proof paths and their own artifact directories. They qualify the original dialogue/panel contracts independently of the application shell. Use `Games/Playtests/common-shell.json` for the shared native lifecycle proof.

SUNKILL is absurd alternate-history parody. Its generated background and
Oppenheimer portrait are original assets and do not copy movie stills or actor
likeness references.
