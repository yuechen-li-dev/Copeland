# Machina Dominatus Rendering (M0a)

## Purpose

M0a establishes the render-actuation seam by treating rendering as typed Dominatus actuation commands.

## Implemented scope

- `Machina.Dominatus` defines a minimal typed render command surface using `IActuationCommand`.
- `ActuatorHost` is used as the renderer backend seam through command handler registration.
- `RenderSnapshotRecorder` provides deterministic, ordered frame snapshots for tests.
- `Machina.Dominatus.Tests` drives a minimal Dominatus node that emits one frame through `Ai.Act`.

## Command model

The M0a command surface is intentionally small:

- `BeginFrameCommand`
- `EndFrameCommand`
- `FillRectCommand`
- `DrawTextCommand`
- `PushClipCommand`
- `PopClipCommand`

These commands are immutable records and all implement `IActuationCommand`.

## Snapshot renderer backend

Snapshot rendering is implemented as an actuator backend:

- handlers record each accepted command in deterministic order;
- handlers complete immediately via `ActuatorHost.HandlerResult.CompletedOk()`;
- recorder validates frame lifecycle and clip-stack balance.

No pixel rendering or rasterization is performed in M0a.

## Explicit non-goals in M0a

- no CPU rasterizer;
- no windowing or graphics framework integration;
- no hit testing or input routing;
- no UI runtime host behavior;
- no traversal of Machina UI lowered/resolved output yet.

## Dependency boundary

`Machina.Layout`, `Machina.Core`, and `Machina.Standard` remain Dominatus-free.
`Machina.Dominatus` is the dedicated integration adapter layer for Dominatus-backed runtime behavior.

## Next direction

Future rendering backends (including CPU raster) should implement the same typed command surface and register through `ActuatorHost` similarly.
