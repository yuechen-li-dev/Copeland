# Machina Presenter M0a

## Backend choice

M0a uses **Avalonia** as a lightweight desktop presenter backend.

Why Avalonia was chosen:

- It provides a stable, managed .NET desktop window path with straightforward bitmap presentation.
- It keeps the presenter dependency isolated to the sample project.
- It avoids injecting any presenter concerns into Machina core packages.

## What M0a proves

M0a proves:

- Machina UI declarations can become raster pixels and appear in a desktop window.

The sample executes:

- `UiNode` declaration
- lowering (`UiLowerer`)
- layout compilation + resolution
- Dominatus command bridge (`MachinaRenderBridge`)
- raster actuation (`AddRasterRenderer` + `DebugBitmapTextRasterizer`)
- `RasterFrame` pixel presentation in a desktop window

## What M0a does not prove

M0a does not implement:

- click handling
- hit testing
- count mutation
- Dominatus UI runtime state loop
- real typography
- GPU rendering

## Run the sample

```bash
dotnet run --project samples/Machina.UI/Machina.Presenter.Sample
```

The sample opens a `640 x 360` window titled `Machina Presenter M0a` and draws one static Machina-rendered frame.

## Solution linkage

The sample project is included in `Machina.UI.slnx` as a buildable sample.

No automated tests open a desktop window.
