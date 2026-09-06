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
dotnet run --project samples/Integrations/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj
```

Run deterministic qualification separately:

```powershell
dotnet run --project samples/Integrations/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj -- --proof
```

The proof writes native runtime screenshots and structured evidence to
`artifacts/renc-vn-m13/`.

Keyboard controls:

- arrows navigate menus and choices;
- Enter or Space confirms and advances dialogue;
- Escape opens or leaves the in-game menu;
- F saves to slot 1;
- I loads slot 1.

The mouse can activate every visible Machina button. Settings and saves are kept
under `%LOCALAPPDATA%\SUNKILL` in the interactive application. The proof runner
uses repository-local artifact paths so qualification is isolated.

SUNKILL is absurd alternate-history parody. Its generated background and
Oppenheimer portrait are original assets and do not copy movie stills or actor
likeness references.
