# JTF-M3b — Aurelian CPU raster backend

Status: completed.

JTF-M3b establishes an Aurelian-owned deterministic CPU raster realization without a production connection to Machina. Aurelian.Rendering.Contracts owns the immutable resolved-2D plan and Aurelian.Rendering.Raster realizes it synchronously into an immutable inspectable surface.

## Changes

- Added resolved-2D viewport, finite geometry, RGBA, text placement, and five-operation plan vocabulary.
- Added AurelianCpuRasterRenderer, RasterFrame, RasterSurface, and deterministic P6 encoding.
- Added focused fast raster tests to Aurelian.slnx and JointTaskForce.slnx.
- Added exact cross-system parity tests only to Aurelian.Integration.Tests in JointTaskForce.Integration.slnx.
- Extended dependency validation so the backend may reference only Rendering.Contracts and may contain no Machina, Dominatus, Core, Runtime, Graphics, Vulkan, Silk, or windowing dependency tokens.

The existing snapshots and world command plans were audited and retained. They describe mesh/material/pipeline work; resolved 2D paint work requires the deliberately narrow sibling plan.

## Evidence and temporary state

Eleven integration cases prove exact dimensions, RGBA buffers, encoded P6 bytes, and SHA-256 equality with the retained Machina renderer, including current Standard component presentation output. The test-only mapping creates no production dependency edge.

The legacy Machina realization and Dominatus adapter remain by design. M3b contains only an Aurelian-owned mechanical port of deterministic raster behavior; it does not move, delete, or switch the old path.

## Recommended M3c scope

Create Aurelian.Machina as the sole production consumer of Machina.Presentation and translate a frame into Resolved2DPlan. Do not change the backend, Machina presentation types, or legacy retirement in M3c. M3d is the earliest allowed removal milestone after the bridge is canonical and exact evidence remains green.
