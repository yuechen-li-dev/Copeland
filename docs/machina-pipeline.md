# Machina.Pipeline (M0a)

Machina.Pipeline is not a presenter.
It does not own windows, input loops, platform scaling, or application state.
It converts a UiNode into a complete frame artifact:
  lowering + layout + hit-test + render commands + raster frame.

## Purpose

`Machina.Pipeline` extracts the reusable UI-to-raster flow from the presenter sample into a package any presenter can call.

## M0a stages

`UiNode` -> `UiLoweringResult` -> `LayoutDocument` -> `ResolvedLayoutDocument` -> `UiHitTestIndex` -> render commands -> `RasterFrame`.

## Public API

- `MachinaFrame`: immutable output containing lowering, layout artifacts, hit-test index, render commands, and raster frame.
- `MachinaRasterPipelineOptions`: width, height, optional text rasterizer.
- `MachinaRasterPipeline.Render(...)`: executes the full deterministic path and returns a `MachinaFrame`.

If `TextRasterizer` is null, the pipeline uses `ReadableBitmapTextRasterizer` by default.

## Dependency shape

`Machina.Pipeline` depends on layout/core/runtime/dominatus/raster packages but has no Avalonia/window dependency.

## Presenter relationship

`samples/Machina.Presenter.Sample` now delegates frame construction to `MachinaRasterPipeline` and keeps only presenter concerns (windowing, bitmap conversion, pointer input, and state).

## Non-goals

- Not a presenter abstraction
- No platform scaling/DPI policy
- No input routing changes
- No new rendering features
- No new UI components
