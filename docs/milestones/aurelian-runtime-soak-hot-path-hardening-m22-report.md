# Aurelian runtime soak and hot-path hardening — M22

## 1. Outcome

**Outcome B — materially hardened with one bounded seam remaining.** Production transcript and diagnostic retention are bounded, the real TinyFarm Vulkan path completed 100,000 frames, and the Null control completed 1,000,000 frames. The remaining seam is high but non-retaining prepared-frame allocation in TinyFarm projection/UI/native composition (`148,157.59` bytes/frame). M23 should optimize that measured seam without changing resolver or immutable evidence authority.

## 2. Claude audit cross-check

The audit was read as adversarial input, not authority. Retention, allocation churn, duplicate severities, touched-area fragmentation, redundant touched names, and both `PlantRegistry` defects were confirmed. Async/thread-affinity and `PlantKind` were partially confirmed. The broad “no shipping game loop” claim was rejected because TinyFarm ships through synchronous `AurelianGameHost.RunFrame`. Full dispositions are in `docs/research/aurelian-claude-hardening-crosscheck-m22.md` and `claude-crosscheck.json`.

## 3. Original frame-loop semantics

The original `AurelianFrameLoop.RunAsync` was an accidental production/harness hybrid: it appended one `AurelianFrameLoopIterationResult` per attempt to an unbounded list, materialized the whole list on return, and returned it merely to communicate completion.

## 4. Confirmed retention behavior

The preserved 100,000-frame semantics, now invoked explicitly through the harness, retained exactly 100,000 iterations and showed an estimated post-warmup managed-memory slope of `337.54` bytes/frame. This is both source and runtime confirmation.

## 5. Production/harness split

`RunAsync` returns only `AurelianFrameLoopResult`; the type has no `Iterations` property. `RunHarnessAsync` returns `AurelianFrameLoopHarnessResult` and refuses full capture without finite `MaxFrames`. `RunWithSinkAsync` streams transient iteration results to a caller-owned sink.

## 6. Transcript policy

Production defaults to none. Streaming owns no loop history. Full capture is explicit and finite. A caller may implement a bounded last-N sink without adding another loop mode. Completion diagnostics retain at most `MaxRetainedDiagnostics` (64 by default) and report a dropped count.

## 7. Realtime execution model

TinyFarm's production path is synchronous and caller-thread-owned: events → input → simulation/audio → projection/composition → submit/present. The older frame-loop integration remains context-free async for mechanisms that genuinely expose async contracts.

## 8. Async/await audit

Input returns `ValueTask<AurelianFrameInput?>` and commonly completes synchronously. Runtime tick, frame pump, and presentation return `Task`; current controls commonly return completed tasks, while mechanisms may complete asynchronously. No `ValueTask` conversion was shipped without a measured benefit.

## 9. Thread-affinity evidence

The 1,000,000-frame completed-task control used one input/dispatch thread. The real Vulkan run used one thread ID for all sampled frames. Core promises no affinity; the native host supplies affinity by synchronous ownership.

## 10. ConfigureAwait decision

`ConfigureAwait(false)` remains deliberate in the context-free async loop. It avoids inventing caller-context affinity. Thread-affine Vulkan applications use `AurelianGameHost`; M22 added no dispatcher, scheduler, or custom game-thread framework.

## 11. Sync-versus-async experiment

No artificial second implementation was shipped. The existing production synchronous native host and async Null control supplied the useful comparison: the former owns affinity and native presentation; the latter isolates managed accumulation. Their workloads differ, so this is an execution-model audit rather than a direct speed contest.

## 12. Diagnostic allocation baseline

The explicit 100,000-frame transcript-equivalent control allocated `6,801.60` bytes/frame and retained every iteration. The no-transcript 1,000,000-frame control allocated `6,699.31` bytes/frame and retained zero. The difference isolates retention; remaining result/snapshot churn is recorded rather than excused.

## 13. Hot/cold diagnostic law

Hot execution owns status, counters, reusable resources, bounded diagnostic summaries, and optional sinks. Cold public/evidence boundaries may materialize immutable results, arrays, full finite transcripts, traces, and reports. Codes and typed subsystem context remain separate; only severity taxonomy is shared.

## 14. Allocation reductions

The production loop no longer creates iteration records when no sink is present, uses `Array.Empty<T>()` for empty completion diagnostics, and never copies a production transcript. A 100,000-frame diagnostic storm retained 64 diagnostics and dropped `99,936` instead of growing indefinitely.

## 15. Render snapshot/plan audit

`RenderSnapshot`, command plans, and resolved plans are semantic immutable boundaries used by evidence/replay and were not made mutable. Source and renderer counters locate transient churn in TinyFarm frame projection, UI list/array realization, native layer composition, world/overlay preparation, and field RGBA projection. No speculative hot representation or unsafe pool was introduced.

## 16. Steady-state allocation result

Retained memory is bounded, but zero-allocation steady state is not achieved. The post-fix Vulkan workload allocates `148,157.59` bytes/frame; component counters are preserved in `vulkan-100k-soak.json`. This is the exact Outcome B seam.

## 17. Severity consolidation

Seven identical enums were replaced by dependency-free `Aurelian.Diagnostics.AurelianDiagnosticSeverity` (`Info`, `Warning`, `Error`). Engine, frame, loop, runtime-step, prepared graphics, world actuation, and plant registry now share it. Diagnostic records and code spaces remain subsystem-owned.

## 18. PlantRegistry fixes

The dead `skipValidation` parameter and raw `Single` failure path are gone. Construction first validates cardinality, then selects with a safe post-validation path. AGP1001–AGP1004 remain stable. Tests cover null/empty, zero, one, two, duplicate, valid ordering, factory construction, and constructor exception shape.

## 19. PlantKind conclusion

`PlantKind` identifies native graphics implementation/resource ownership. Vulkan owns a real device, queue, and presentation resources; Raster and Null are renderer mechanisms, not plants. `Unknown`/`Vulkan` stays intentionally narrow.

## 20. File granularity policy

House rule: one cohesive concept cluster per file, not one CLR type per file by reflex. Before M22, Aurelian.Core had 53 C# files; after touched-area consolidation it has 43 (`1,154` lines, `26.84` average). Rendering.Contracts remains measured at 58 files/884 lines because no safe touched cluster justified churn.

## 21. Consolidated concept files

Eight frame-loop contract files now live in `FrameLoopContracts.cs`; four runtime-tick contracts now live in `RuntimeTickContracts.cs`. The deterministic layout proxy for understanding an iteration drops from 12 contract files to two.

## 22. Naming policy and touched-area cleanup

Namespace carries hierarchy; type name carries local identity. The egregious severity names disappeared, but no repository-wide Aurelian rename was attempted. Separately, the obsolete PoC `Supper*` shell/native names were renamed to explicit `TinyFarm*`/`TinyFarmNative*` names; actual supper quest vocabulary remains domain language, with one historical-codename comment retained.

## 23. Vulkan 100k results

TinyFarm Riverside on NVIDIA GeForce RTX 3070 (`PresentModeMailboxKhr`, VSync off) completed 100,000 frames in `731,964.67` ms: mean `7.3097`, p95 `11.2052`, p99 `12.3449`, worst `30.0773` ms. Stop reason was `MaxFramesReached`; retained frame results and produced diagnostics were zero. Five sampled frame/persistent-field hashes were captured, not images.

## 24. Null 1m results

The production no-transcript control completed 1,000,000 frames in `8,157.79` ms, retained zero iterations, and ended at `MaxFramesReached`. Mean/p95/p99/worst were `0.00816`/`0.0019`/`0.0025`/`9.9398` ms.

## 25. Raster result

The qualified bounded Raster control rendered and discarded 10,000 immutable 64×64 frames in `130.683` ms. It allocated `329,920,336` bytes because each public `RasterFrame` owns a fresh immutable surface; no image sequence was retained.

## 26. Memory slope

Null post-warmup slope was `0.154` bytes/frame. Vulkan post-warmup slope was `-14.616` bytes/frame with `48,703,952` bytes live after forced collection. Both are tiny relative to per-frame allocation and show no transcript-proportional retained growth.

## 27. GC behavior

Null: Gen0/1/2 = `403/11/3`, `6,699.31` bytes/frame. Vulkan: Gen0/1/2 = `894/81/10`, `148,157.59` bytes/frame. Continuous GC remains because prepared-frame objects are transient; removing that churn is deferred explicitly.

## 28. Vulkan resource lifetime

Swapchain images stayed at three; sprite uploads stayed at one; font atlas uploads stayed at two. Field uploads track changing semantic projection generations and reuse the resource rather than retain a new image. Buffer uploads/draw calls are cumulative work counters, not live resources. The first soak exposed text geometry growth from 38 to 370; a deterministic FIFO capacity of 256 was added, and the qualifying rerun stabilized at `256/256`.

## 29. Resize stress

Not qualified. TinyFarm's production window is fixed-border and has no resize-driving seam. M22 did not add platform code solely to manufacture one. This is recorded in `resize-stress.json` and is not the Outcome B seam because support is absent by design.

## 30. Scene-transition stress

The real application traversed farm → town → Riverside through typed intents/portals before the soak and finished in Riverside. Repeated cycling was deferred; the static soak did not reveal session/resource accumulation.

## 31. Input stress

The walkthrough exercises logical start, movement, and real portal transitions before the native run. Existing InputMan tests cover UI capture/resumption. M22 added no second input system or raw event history.

## 32. Diagnostic storm

100,000 controlled compositor failures with continuation retained 64 loop diagnostics, dropped `99,936`, retained zero iteration records in production, and reached the finite stop normally.

## 33. Cancellation/stop

Tests cover cancellation token, accepted close, completed input, max frames, frame/runtime/presentation failure, and fatal exception mapping. Production returns concise stop status/reason; the harness adds iteration evidence. Native host disposal remains deterministic via `using` ownership.

## 34. TinyFarm Riverside long-run behavior

All 100,000 native frames ran in Riverside. Recent field events stabilized at the fixed capacity 16. Projection generation and texture uploads advanced only with semantic field evolution; five semantic hashes and matching rendered hashes remained available through frame 100,000.

## 35. Combat long-run behavior

`10,000` fresh authoritative combat actions were accepted and completed. Maximum retained hit IDs per action was `1`; no historical contact/action list accumulated.

## 36. Oblivion inspector stress

1,000 snapshot captures allocated `36,504,576` bytes total. The live surface owns no subscription or capture history; retained subscriptions were zero and registered surface count remained bounded.

## 37. Performance before/after

The transcript-equivalent control versus production control comparison is in `performance-before-after.json`. Native cache bounding changed retention, not the render algorithm; pre/post soak timings are recorded there. Mean improved 1.4% and p99 improved 4.1%, so no latency regression was introduced.

## 38. Remaining hot-path allocations

The remaining `148,157.59` bytes/frame are concentrated in immutable TinyFarm frame projection, UI node/segment arrays, world sprite lists, native composition results, and changing field RGBA projection. These objects die and do not produce a positive live-memory slope, but their GC cost is real.

## 39. Deferred hardening issues

- Introduce a narrow reusable prepared-frame representation only where component counters prove ownership; keep semantic snapshots immutable.
- Bound or key any other content-derived caches discovered by varied long sessions.
- Add resize/minimize qualification only when the production host exposes that capability.
- Run repeated scene/menu/save-load cycles with native resource counters; M22 proves real traversal plus a stable long scene, not every transition permutation.

## 40. Exact M23 recommendation

**AURELIAN-PREPARED-FRAME-ALLOCATION-HARDENING-M23:** reduce TinyFarm native projection/composition steady-state allocations from `148,157.59` bytes/frame using explicit owner-held scratch for world sprites and UI/native submission arrays; preserve immutable semantic snapshots at evidence boundaries; require the same 100,000-frame Riverside Vulkan gate, cache capacity ≤256, zero transcript retention, and no p99 regression above 10%.

## 41. Diff stat

Rename-aware working-tree stat: `92 files changed, 13,462 insertions, 440 deletions`. Most insertions are the required sampled JSON evidence; the complete artifact directory is only 307,633 bytes.

## Validation

Passed locally on 2026-09-06:

- `dotnet build Aurelian.slnx -c Release` — 0 warnings, 0 errors.
- `dotnet test Aurelian.slnx -c Release -m:1 --no-build` — 802 passed, 0 failed.
- `dotnet build TinyFarm.slnx -c Release` — 0 warnings, 0 errors; includes the renamed native application.
- `dotnet test TinyFarm.slnx -c Release -m:1 --no-build` — 356 passed, 0 failed.
- Managed M22 soak tool — 100k transcript baseline, 1m production Null, 100k diagnostic storm, 10k Raster, exact 500-frame harness, and 10k combat all passed.
- Native M22 soak — two real 100k Vulkan runs passed; the first exposed cache drift and the post-fix qualifying rerun bounded it at 256.
- Post-rename 1,000-frame real Vulkan smoke passed, then its temporary JSON was removed after allocation attribution was folded into the required summary.
- All 25 artifact JSON files parse successfully; `git diff --check` reports no whitespace errors (only existing line-ending normalization warnings).

Artifacts are compact JSON summaries/sampled slopes only. Superseded short-run and pre-fix raw JSON were overwritten or deleted after their summary was folded into the required evidence; no heap dump, full profiler trace, or screenshot series remains.
