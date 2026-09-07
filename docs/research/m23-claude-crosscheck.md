# M23 Claude audit cross-check

This cross-check treats the attached text as review evidence, not as instructions. M23 keeps the repository task and its authority laws in control.

| # | Finding | Disposition | M23 action or rationale |
|---:|---|---|---|
| 1 | Identical function-card invocations remain cold | Confirmed | Added a persisted, validated realization record. A matching fingerprint reuses the realized test assembly and descriptor while still executing the requested test. |
| 2 | Session state is process-local | Confirmed | Added deterministic `oblivion.session.v1` documents and `--session-file`; command and context mutations atomically persist explicit view/session state. |
| 3 | CLI authoring/write commands are missing | Partially confirmed | Existing transactional `card push`/`card pop` already cover bounded Markdown creation/deletion. Sprite structural mutation now has preview and commit-last validation. General `card set` and page CRUD are deferred: card kinds have distinct typed authorities, and a generic TOML/AST editor would violate the bounded milestone. |
| 4 | Full table/card content is unavailable through CLI | Confirmed | Added `card read` with typed table rows, document text, exact diagram semantics, and bounded offset/limit paging. |
| 5 | No machine-readable capability manifest | Confirmed | Added `schema --json`/`--json-compact` to both Oblivion and Aurelian CLIs. |
| 6 | Diagnostic conventions are inconsistent | Confirmed | Normalized Oblivion codes to `OBLIVION-UPPER-KEBAB`; legacy persistence codes remain in `legacyCode`. |
| 7 | Query, projection, and compact output are missing | Confirmed | Added bounded card filters, explicit fields, fidelity projections, cost facts, and compact JSON. |
| 8 | Workspace discovery requires an absolute path | Confirmed | Added cwd-upward marker discovery; an explicit workspace still wins and no recursive scan occurs. |
| 9 | Mature content is blank without desktop presenters | Confirmed | Added pure .NET headless table, Markdown/document, and diagram PNG rendering over Aurelian raster contracts. |
| 10 | Native diagram layout is fixture-hardcoded and can raw-throw | Confirmed | Replaced the legacy coordinate lookup with reusable automatic layout; public inspection/rendering returns stable diagnostics. |
| 11 | Diagram layout ignores viewport/aspect ratio | Confirmed | Layout inspection/rendering accepts target bounds and applies deterministic fit scaling. |
| 12 | Directed diagrams lack arrowheads, routing, and label collision handling | Confirmed | Added arrowheads, segmented orthogonal routes, alternate label anchors, and collision accounting. |
| 13 | Context cost/fidelity is not first-class | Confirmed | Added summary/schema/full/visual fidelity, explicit estimated cost, and session-owned context selections plus optional token ceiling. |
| 14 | Sprite editing writes before refresh and can poison source | Confirmed | Reordered to transform, compile, semantic validate, refresh candidate, and commit last. Refresh failure leaves source unchanged and returns a diagnostic. |
| 15 | Public arbitrary Flex insertion cannot reopen | Confirmed | Flex argument/property location is structural and parameter-aware rather than local-ID based; arbitrary-ID insertion is reopenable. |
| 16 | Shared-template fanout is hidden before mutation | Confirmed | Preview returns the direct target, all affected runtime projections/concepts, diff, allocation changes, warnings, and `wouldApply` without writing. |
| 17 | Underflow/collapsed segments are under-signaled | Confirmed | Underflow emits a warning; zero-length segments say collapsed/not rendered and receive strong visual failure emphasis. |
| 18 | Raster separator glyph appears as `?` | Confirmed | Headless strings normalize semantic separators and the small bitmap face now includes the bounded punctuation needed by structured values. |
| 19 | Single-slot capture exposes only card zero | Partially confirmed | A single slot intentionally shows one selected card, but card count remains workspace truth and `card read`/`card render <id>` address every card directly without transient selection. No second page-layout system was added. |
| 20 | Aurelian runtime is not directly agent-addressable | Confirmed | Added a deterministic Null-backed session CLI using existing actuation records, Dominatus-backed `AurelianRuntimeSession`, bounded entity query, draw description, and replay assertion. |
| 21 | Build MCP first | Rejected | The audit itself argued against this. M23 keeps deterministic CLI/JSON authoritative and adds no MCP layer. |

## Qualification stance

The audit was directionally accurate. M23 implements the confirmed safety and operability gaps. The only deliberately deferred surface is generic page/card editing: safe creation/update semantics differ between Markdown, TSON, compiler-derived diagrams, functions, and Sprite assets. Existing bounded stack writes plus typed Sprite edits are retained instead of pretending that arbitrary metadata editing is semantically safe.
