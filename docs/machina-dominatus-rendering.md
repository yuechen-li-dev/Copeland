# Machina Dominatus Rendering (M0a + M0b)

## Purpose

Machina.Dominatus models rendering as typed Dominatus actuation so Machina UI artifacts can drive deterministic render command streams through a real `ActuatorHost` seam.

## Implemented scope

- `Machina.Dominatus` defines a minimal typed render command surface using `IActuationCommand`.
- `ActuatorHost` is the render backend seam through command handler registration.
- `RenderSnapshotRecorder` provides deterministic, ordered frame snapshots for tests.
- M0a proves manual command emission via `Ai.Act`.
- M0b proves real Machina UI declaration output can be bridged to deterministic render commands and actuated through Dominatus.

## Command model

The command surface is intentionally small:

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

No pixel rendering or rasterization is performed.

## M0b UI-to-command bridge

M0b adds a deterministic bridge from Machina UI artifacts to Dominatus render actuations:

- `MachinaRenderBridge.BuildCommands(lowering, resolved, options)` consumes `UiLoweringResult` and `ResolvedLayoutDocument`;
- output command order follows deterministic document traversal order;
- command stream always includes frame boundaries (`BeginFrame`, `EndFrame`);
- node background styles become `FillRectCommand`;
- textual semantics become `DrawTextCommand`;
- actions remain runtime/input metadata and are not emitted as render commands.

M0b still does not rasterize pixels, does not perform hit testing, and does not route actions/events.

## Dependency boundary

`Machina.Layout`, `Machina.Core`, and `Machina.Standard` remain Dominatus-free.
`Machina.Dominatus` is the dedicated integration adapter layer for Dominatus-backed runtime behavior.

## Explicit non-goals

- no CPU rasterizer;
- no windowing or graphics framework integration;
- no hit testing or input routing;
- no UI runtime host behavior;
- no browser/runtime/Copeland host integration.
