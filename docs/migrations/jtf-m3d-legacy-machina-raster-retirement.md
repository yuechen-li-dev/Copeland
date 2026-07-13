# JTF-M3d — Legacy Machina raster retirement

## Status

Completed. The `Machina.Renderer.Raster`, `Machina.Renderer.Raster.Text`, and `Machina.Renderer.Raster.Dominatus` production projects and their renderer-only test projects are deleted. The renderer-oriented portion of `Machina.Dominatus`, including `LegacyMachinaRenderCommandAdapter`, is deleted.

## API and ownership migration

`MachinaRasterPipeline.Render` and its raster-bearing result were removed. The replacement is `MachinaPresentationPipeline.Prepare`, which ends at `MachinaPreparedPresentation.PresentationFrame`. This intentionally breaks callers that treated Machina as a pixel producer.

The presenter and gallery samples now explicitly compose `MachinaPresentationPipeline`, `Aurelian.Machina`, and `AurelianCpuRasterRenderer`. Optional diagnostic overlay staging remains sample-local after Aurelian has produced its raster; no Machina production project owns it.

## Coverage migration

Machina pipeline tests now assert lowering, layout, hit-testing, deterministic presentation operations, text placement, and viewport semantics. Pixel realization remains covered by Aurelian raster tests. Cross-system bridge tests own translation and frozen output regression evidence.

M3b/M3c's live legacy/new comparisons established exact RGBA and PPM parity before retirement. M3d freezes representative empty, nested-clip, mixed-operation, and authored Standard-document PPM SHA-256 fixtures. The actual side exclusively uses Machina presentation, the bridge, and the Aurelian CPU raster backend.

## Topology and next scope

The deleted projects were removed from all solutions and the stale renderer/Dominatus dependency exception was removed. `Machina.UI.slnx` remains Machina-only; `Aurelian.slnx` remains Aurelian-only; bridge checks remain under `JointTaskForce.Integration.slnx`. No additional integration slow solution was needed after renderer-command sample tests were retired.

JTF-M4 may now reconcile presenter, screens, and input ownership. It must not reintroduce backend selection or raster composition into Machina.

## Non-goals

M3d does not alter Machina presentation operations, Aurelian resolved-2D operations, raster semantics, Vulkan, shaders, screen-stack behavior, or input policy. The non-rendering `Machina.Dominatus` runtime proof remains JTF-M5 debt.
