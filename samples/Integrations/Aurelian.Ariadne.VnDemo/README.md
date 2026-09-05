# Aurelian Ariadne VN demo

This sample is the executable proof for `AURELIAN-ARIADNE-MACHINA-DIALOGUE-M7B`.
It runs a compact branching after-school scene through Ariadne dialogue operations,
projects the active semantic state into Machina, and composites background, portrait,
and UI through Aurelian's native Vulkan layer path.

Run the playable presenter from the Copeland repository root:

```powershell
dotnet run --project samples/Integrations/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj
```

Run the deterministic qualification separately:

```powershell
dotnet run --project samples/Integrations/Aurelian.Ariadne.VnDemo/Aurelian.Ariadne.VnDemo.csproj -- --proof
```

The deterministic proof and screenshots are written to
`artifacts/aurelian-ariadne-machina-dialogue-m7b/`.

Controls are Enter/Space to advance or confirm, arrows to select, A for auto, S for
skip, Escape to cancel skip, F to save, and I to load. The pointer can activate
choices and visible controls. Auto advances one line per host-requested pulse; skip
uses the same bounded pulse and always stops at choices.

The original art is generated for this sample. `rei-soft-cutout.png` was repaired
from an accidentally baked checkerboard by applying MachinaCanvas's existing
`deriveAlphaMapPixels` algorithm through `tools/remove-checkerboard-with-machina-canvas.ts`.
