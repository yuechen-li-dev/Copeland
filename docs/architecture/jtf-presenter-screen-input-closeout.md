# JTF-M4d presenter, screen, and input closeout

JTF-M4 is complete. Machina owns the immutable `UiInputBatch` vocabulary, frontend lifecycle messages, screen/layer composition, presentation preparation, and logical-to-presented mapping. Aurelian owns lifecycle facts, `AurelianCloseRequest` acceptance, engine stopping, and stop reasons. `Aurelian.Machina` only translates typed frontend messages. Platform callbacks, temporary accumulation, rendering backend selection, and native disposal belong to integration hosts.

## Host iteration

An integration-owned collector appends normalized callback values in arrival order. `Publish()` atomically copies and drains those values into one immutable `UiInputBatch`; an empty iteration still produces an empty batch. A callback arriving after the publication lock is released belongs to the next batch. There is no global queue, timestamp sorting, or static sequence source.

The host fans that exact batch value out to `PresenterUiInputRouter` and `MachinaFrontendInputRouter`. Both consume it without mutation. Presentation preparation uses `MachinaPresentationPipeline.Prepare`. On a resize, the host updates its effective surface and recomposes before the presenter router resolves later coordinate-dependent events from the same batch.

## Lifecycle and shutdown

`MachinaFrontendSurfaceResized` becomes an `AurelianHostExtent`; `MachinaFrontendCloseRequested` becomes an `AurelianCloseRequest` in `Aurelian.Machina`. The frame loop accepts the command through `AurelianFramePump`, records engine acceptance, stops the engine, and returns `CloseRequested` before beginning another frame. Repeated commands are idempotent. Provider completion remains the separate `InputProviderCompleted` stop reason.

Events before close remain ordered and observable. The close command is accepted once; the host disposes native resources only after the frame loop reports its close stop result. Hosts do not mutate engine state or terminate the process.

## Topology and scope

Cross-system hosts remain under `samples/Integrations` and are exercised by `JointTaskForce.Integration.slnx`; subsystem solutions remain free of them. Playback produces the same foundational `UiInputBatch` records as live collection and does not use a platform collector. Mixed screen composition remains Machina-owned and renderer-neutral.

InputMan is deliberately deferred:

```text
completed: platform normalization -> UiInputBatch -> UI routing/lifecycle messages
future:    UiInputBatch -> device/action normalization -> action maps/rebinding -> higher actions
```

M4d adds no action maps, controller support, navigation policy, event bus, raster work, Vulkan change, or general frame-loop redesign. The repository is ready for JTF-M5 Dominatus ownership consolidation.
