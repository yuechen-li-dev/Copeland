# Aurelian runtime execution modes

## Production native runtime

`AurelianGameHost.RunFrame` is the shipping native-host mechanism. The platform/application thread synchronously performs event pumping, input sampling, semantic simulation, audio update, application rendering, compositor submission, and presentation. The host retains only counters and owned subsystem state. Vulkan objects are created, used, resized, and disposed by their graphics owners on that host path.

`AurelianFrameLoop.RunAsync` is the context-free production API for integrations built on the older compositor-policy frame pump. It returns only a bounded completion result. It never creates or retains an iteration transcript. Loop diagnostics retain at most `MaxRetainedDiagnostics` entries and report the dropped count.

## Diagnostics and evidence runtime

`AurelianFrameLoop.RunHarnessAsync` requires an explicit finite `MaxFrames`. It retains the complete iteration transcript and returns it beside the concise completion result. This is the correct path for exact assertions such as “run 500 frames and inspect all 500 outcomes.”

`AurelianFrameLoop.RunWithSinkAsync` emits transient iteration records to a caller-owned sink. The sink owns its retention law: discard, stream, bounded ring buffer, or explicit finite full capture. The production loop does not silently turn a sink into history.

## Editor and notebook inspection

Inspection is snapshot-based and caller-triggered. TinyFarm's Oblivion live surfaces materialize bounded read-only field/combat workspaces on request and hold no event subscription or snapshot history. Editor/notebook consumers may retain artifacts because they are cold evidence owners, not the realtime runtime.

## Test harness

Tests use `RunHarnessAsync` when iteration evidence is part of the assertion and `RunAsync` when only completion behavior matters. Repeating warnings/errors in production is bounded by the loop diagnostic capacity; a finite harness can still preserve per-frame typed results.

## Async and thread-affinity decision

The older frame loop keeps `ConfigureAwait(false)`: it is a synchronization-context-free integration/evidence API and must not imply return to an arbitrary caller context. A completed-task 1m control remained on one thread, but callers must not infer affinity from that observation. A backend that requires stable thread ownership uses a synchronous native host such as `AurelianGameHost`; M22 does not introduce a job system, dispatcher, or custom scheduler.

The async chain remains because its input, runtime ticker, compositor mechanism, and presentation contracts can genuinely complete asynchronously. `IAurelianFrameInputProvider` already uses `ValueTask` for its common synchronous path. M22 does not cargo-cult `ValueTask` through public result APIs without measurements proving a benefit.

## Hot/cold diagnostic law

Hot execution retains bounded status, counters, reusable backend resources, and at most a bounded diagnostic summary. Cold public/evidence boundaries may materialize immutable results, diagnostic arrays, traces, screenshots, and transcripts. Diagnostic code spaces and typed context remain subsystem-specific; only severity is shared.
