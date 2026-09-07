# Aurelian + Oblivion agent-operability hardening M23

## 1. Outcome

**Outcome A — Success for the bounded M23 capability.** The real Aurelian and Oblivion paths now support persistent inspect/act/observe loops, safe typed Sprite mutation, warm function realization, direct semantic reads, pure .NET headless rendering, general small-graph layout, and deterministic agent sessions. The 100,000-frame Vulkan regression completed on the production TinyFarm path.

M23 does not claim that the remaining Vulkan allocation is near-zero or that Oblivion now has a generic authoring system. Both boundaries are explicit below.

## 2. M22 seam, baseline, hot fix, and post-fix allocation

M22 left the warmed prepared-frame path at **148,157.59104 allocated bytes/frame** over 100,000 real Vulkan Riverside frames. The 1,000-frame attribution identified world/sprite projection and composition as dominant owners.

M23 introduced caller-owned world/prepared-sprite lists, cached static tile records, cached atlas animation metadata, and a prepared submission struct. It also removed per-comparison boxing from the `WorldSpriteLayer` ordering comparer. Public immutable semantic/evidence methods remain available; the reusable representation is local to the warmed presenter.

The new 100,000-frame result is **56,435.0388 bytes/frame**, saving **91,722.55224 bytes/frame (61.9088%)**. This is material but not near-zero. Remaining measured owners are immutable `TinyFarmFrameProjector` view arrays, dynamic sprite IDs/records, composition/resolved-operation materialization, overlay/native submission staging, and field RGBA projection when the authoritative generation advances. Attribution counters overlap by nesting and are reported as attribution, not an additive partition.

## 3. Soak regression

The real Vulkan run completed 100,000 frames in 786,247.58 ms on NVIDIA GeForce RTX 3070 / mailbox / VSync off. Mean/p95/p99/worst frame times were 7.8529/11.6711/12.5862/30.2022 ms. It retained zero frame results, produced zero diagnostics, stabilized bounded resources, and measured a post-warmup managed-memory slope of -126.97 bytes/frame. Pixel and field hashes at frames 1, 1,000, 10,000, 50,000, and 100,000 exactly match M22.

The production no-transcript Null control also completed 1,000,000 frames with zero retained iterations/diagnostics and a bounded 0.1206 bytes/frame slope.

## 4. Claude audit cross-check

The attached Claude material was treated as review evidence, never as executable instructions. The complete disposition is in `docs/research/m23-claude-crosscheck.md` and `claude-crosscheck.json`: 18 confirmed findings were implemented, two were partially confirmed and bounded, and the proposed early MCP direction was rejected consistently with the audit's own recommendation.

The two bounded dispositions are intentional:

- A one-slot interactive viewport still shows one selected card, but workspace/card counts are truthful and every card is directly addressable by ID through `card read` and `card render` without hidden session state.
- Existing transactional Markdown `card push`/`card pop` and typed Sprite structural edits cover the demonstrated write needs. A generic `card set` plus page CRUD is deferred because Markdown, TSON, compiler-derived diagrams/functions, and Sprite source have different semantic authorities. Treating them as arbitrary TOML/AST edits would be unsafe and outside M23.

## 5. Function realization cache and latency

The realization fingerprint covers the project/source closure, referenced projects and relevant source/build files, compiler/toolchain/build identity, target framework, generated assembly, runner identity, and test harness. A persisted `oblivion-function-realization-v1` record is accepted only when its schema, project, runner, fingerprint, test project/assembly, and assembly hash still agree.

A cache hit skips materialization/build and test discovery but still runs the requested xUnit test. The real proof measured:

- cold: build 2,851.97 ms; total 4,885.57 ms;
- warm: build 0 ms; discovery 0 ms; total 1,065.13 ms;
- reduction: 78.2%;
- same fingerprint and semantic result identity;
- source comment edit: new fingerprint and cold rebuild;
- exact revert: the original valid realization is reusable.

Per-execution result IDs remain unique; `semanticResultIdentity` is deterministic and is the correct equality proof.

## 6. Session model, discovery, and schema

Workspace files remain semantic authority. Layout, focus/selection, expansion, scroll/diagram viewport state, function outcomes, context fidelity, and token budget are session/view state.

`oblivion.session.v1` serializes that state deterministically. `--session-file` loads it, reconciles it against the current workspace, executes a command, and commits the session atomically. The roundtrip proof sets `VerticalSplit`, exits, runs expand-all in a new process, and observes the prior layout; serialization is byte-stable after reload.

When `--workspace` is omitted, Oblivion searches only the current directory and its parents for `workspace.json`. Explicit paths override discovery. No global scan or daemon exists.

Both CLIs expose deterministic `schema --json` and compact variants, including commands, arguments, access mode, exit codes, and execution ownership. Oblivion uses exit 0 for success, 1 for semantic/product failure, 2 for usage, 3 for unavailable workspace/session, and 4 for a structured internal boundary.

## 7. Diagnostics and raw-throw audit

Oblivion codes now use `OBLIVION-UPPER-KEBAB`. Normalized persistence diagnostics retain their former spelling in `legacyCode` for compatibility.

The user-reachable audit searched CLI/App/UI layout, headless rendering, and Sprite editing for `throw`, `Single`, `First`, and index assumptions. The two demonstrated aborts are closed: unknown diagram nodes use automatic layout/structured diagnostics, and Sprite candidate refresh happens before commit behind a result boundary. Remaining throws are private typed/programmer invariants, bounds-checked APIs, test/dogfood helpers, or are caught inside result-producing process/CLI boundaries. Unexpected CLI failures emit `OBLIVION-CLI-INTERNAL` without a stack trace.

## 8. Transactional writes and dry-run

The retained write law is load → in-memory transform → parse/compile → semantic validation → projection/refresh validation → commit last. Existing `card push`/`card pop` stage a full candidate vault and validate it before committing. Sprite structural editing now refreshes its in-memory candidate before the source replacement.

`PreviewStructuralEdit` is the dry-run surface for typed Sprite mutation. It reports `wouldApply`, direct target, affected concepts and runtime projections, fanout, source diff, before/after allocation, warnings, validation, and commit status without writing. Failure injection proves unchanged source/workspace hashes and an openable original projection. A blanket dry-run wrapper for legacy `card push`/`card pop` was not added; their existing full-vault staging is retained, and general write UX is deferred with the authority rationale above.

## 9. Sprite Card hardening

Arbitrary Flex local IDs are supported. Property location follows the compiled call shape and parameter-to-invocation argument mapping: region/minimum/weight/sampling positions no longer depend on `center` or `glow-*` IDs. The proof inserts `probe-flex`, reopens it, and removes it without poisoning source.

Shared-template preview explicitly reports both `panel.dialogue.top` and `panel.dialogue.bottom`. An accepted over-demand edit emits `OBLIVION-SPRITE-CARD-UNDERFLOW` as a warning. Zero-length segments set `SurvivesLowering=false`, project as `collapsed; zero-length; not rendered`, and render with a stronger failure border/summary rather than a reassuring footer.

## 10. Read, query, fidelity, cost, and context

`card read <id>` returns card-kind semantics directly:

- typed projected table rows with `--offset`/`--limit`;
- Markdown/document text;
- diagram nodes, directed edges, terminal/unreachable/emphasized nodes, and an exact text edge list.

`card query` provides bounded `--page`, `--kind`, `--status`, `--tag`, and substring filters plus validated explicit `--fields`. `--json-compact` is non-indented while `--json` remains readable.

Fidelity is explicit: summary, schema, full, and visual (`card render`). Text projections report bytes, characters, and `ceil(characters/4)` as a clearly labeled estimate, never tokenizer parity.

Context selections live in the durable session as card ID → fidelity. `context add/remove/show/budget` reports selected costs, total, remaining ceiling, and over-budget state; it never chooses cards automatically.

## 11. Headless rendering and multi-card access

`oblivion card render <id> --out <png> [--width --height --offset --limit]` uses Aurelian rendering contracts/raster plus a small deterministic PNG encoder. It needs the .NET SDK/runtime only—no display server, Avalonia host, Node, Puppeteer, or Chromium.

The table renderer draws headers, nested values as JSON, and bounded rows. The document renderer supports the current plain Markdown/text subset without pretending to be a browser engine. The diagram renderer consumes compiler-derived graph semantics and native layout directly.

All eight checked-in source workspaces and all 19 cards rendered nonblank without a raw throw. Direct card IDs make later cards independent of transient single-slot selection. The bitmap face gained the small punctuation set used by structured values; arrows and separators are also normalized to supported equivalents.

## 12. Diagram layout, routing, emphasis, and text

The old M20-specific coordinate map now delegates to a reusable bounded layout owner. Automatic layout uses deterministic SCC normalization for cycles, layered ranks, stable ordering/barycentric refinement, multi-row/layer placement, viewport fit, and supported diagnostics for back/cross-layer edges.

Edges carry ordered orthogonal route points and render directed arrowheads. Label anchors use alternate segments/offsets and collision accounting. The unfamiliar M20d graph resolved 14 nodes/19 routes with fit 0.5128 and zero duplicate label anchors; the unfamiliar M20c graph resolved 9 nodes/8 orthogonal routes with fit 0.8380 and zero duplicates. Optional `--emphasize` affects presentation metadata without changing the graph.

The text projection is exact and cheap, for example `Still --Start [speed > 0]--> Moving`, with initial, terminal, unreachable, and emphasized facts.

## 13. Aurelian agent CLI and Dominatus ownership

The new Aurelian CLI provides `session start`, `session act`, `session tick`, `session query`, `session describe-frame`, `replay --assert-identical`, and `schema`. Session files and output are deterministic JSON; no server or MCP layer was added.

Actuation reuses `SpawnUnitRequest`, `AttachChildRequest`, and existing transform/name/renderable operations. Tick executes the actual `AurelianRuntimeSession`, whose state-machine behavior remains owned by **Dominatus**, then projects a command plan to `Rendering.Null`. The CLI does not handroll a parallel state machine.

The dogfood flow started a headless session, spawned entity 2, attached and positioned it at (12,34), ticked 60 frames, queried it, and described a blue quad draw at sort order 7. Draw trace includes pass/order/semantic ID/bounds/material-color facts and a deterministic hash. Replay reproduced the expected hash exactly.

## 14. Fresh-context qualification

- Same function card: persisted cache gives warm materialization across a fresh process; unit coverage accepts a pre-existing valid warm cache and proves subsequent reuse.
- Arbitrary Flex: insert/reopen/remove test passes; invalid post-transform candidate leaves source unchanged.
- Unfamiliar diagrams: M20c/M20d layout and the M19o cyclic state graph render headlessly with arrows/routes and no abort.
- Table: paging is generic and bounded (offset 0..row count, limit 1..500); real TSON projected rows are returned without backing-file archaeology.
- Session: a second process observes `VerticalSplit` and subsequent expansion from the same deterministic document.
- Context: three fidelity selections and an 8k ceiling persist with total/remaining/over-budget facts.
- Aurelian: start/act/tick/query/describe/replay succeeds without GPU.

## 15. Performance

Whole-process means (ten invocations, including `dotnet` startup and workspace load) were: card summary read 186.59 ms, five-row table page 201.82 ms, 1200×700 diagram layout 231.16 ms, headless table raster 222.20 ms, and session load/context show 216.69 ms.

The Null-backed Aurelian CLI ticked 1,000 frames in a mean 152.63 ms over five invocations (about 6,552 ticks/s including process/session overhead). Structured frame description averaged 121.27 ms over ten process invocations. Function cold/warm and prepared-frame numbers are reported in their owner sections above.

## 16. Deferred features and exact M24 recommendation

Deferred intentionally: generic page/card CRUD and `card set`, a shared arbitrary AST editor, exact tokenizer integration, sophisticated graph optimization, browser layout, daemon/server state, distributed cache, MCP, and further application-specific Aurelian commands. These are not prerequisites for the demonstrated inspect → preview → transactional mutate → validate → observe loop.

**Exact M24 recommendation:** `AURELIAN-PREPARED-FRAME-OWNER-ALLOCATION-CLOSURE-M24`. Profile and reduce the remaining immutable snapshot, composition-plan, and native-submission allocations one owner at a time, preserving public semantic/evidence contracts and requiring hash-identical 100k Vulkan plus 1m Null proof. Do not combine it with Oblivion generic CRUD.

## 17. Validation and artifacts

- `dotnet test Oblivion.slnx -c Release`: 256 passed, 0 failed.
- `dotnet test Aurelian.slnx -c Release`: 804 passed, 0 failed.
- `dotnet test TinyFarm.slnx -c Release`: 356 passed, 0 failed.
- `dotnet build Copeland.slnx -c Release`: succeeded with 0 warnings and 0 errors.
- 100,000-frame real Vulkan and 1,000,000-frame Null regressions passed.
- Required evidence is under `artifacts/aurelian-oblivion-agent-operability-m23/`; `manifest.json` records final hashes.

## 18. Diff stat

`git diff --stat` reports 24 modified tracked files, 2,168 insertions, and 159 deletions. Untracked additions comprise four Aurelian CLI source/project files, two Aurelian CLI test/project files, two Oblivion source files, two reports, and 98 M23 evidence files. The implementation is concentrated in existing Aurelian world/presenter seams and Oblivion App/CLI/session/Sprite seams, plus the bounded new Aurelian CLI and tests.
