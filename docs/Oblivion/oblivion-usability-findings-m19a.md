# Oblivion usability findings — M19a

## Outcome

**Outcome B — useful foundation, but missing product surfaces materially limit serious use.**

Cards and pages improve technical reasoning once their semantics are directly inspectable. They were not sufficiently usable to an external LLM before M19a because the complete workspace, derived documentation cards, runtime actions, and effect availability were only reconstructable from implementation. The M19a facade and CLI make a bounded workflow genuinely useful. Artifact navigation and most effectful host capabilities remain the smallest important gaps.

## What worked

- JSON/TOML/Markdown persistence is deterministic, reviewable, and friendly to normal source editing.
- Stable IDs exist for workspace, pages, cards, actions, and artifacts.
- Referenced Markdown gives a card bounded context without burying the durable source.
- Typed M18d action/effect contracts were reusable directly; no second command model was needed.
- `refresh-markdown` can now perform a real cache-free reload and validation from a shell.
- Existing diagnostics already carry useful code, source, line, and column data.
- The human inspector and expanded reading surface survived unchanged.

## Friction audit

| Rating | Observation | Owner | M19a response |
|---|---|---|---|
| BLOCKING | `inspect --json` was treated as a manifest path; no external semantic discovery existed. | Oblivion.App / CLI | Added product facade and CLI. |
| ANNOYING | The root manifest listed 17 durable cards, while the product actually projected 34 because docs cards are derived. | Oblivion.App | `inspect` and `cards` expose the realized product. |
| ANNOYING | Runtime actions differ from persistence-declared actions and required handler-source reading. | Oblivion.UI / Oblivion.App | `show` exposes both lists; `actions` exposes runtime descriptors. |
| ANNOYING | Card commands initially repeated unrelated workspace diagnostics. | Oblivion.App | Diagnostics are scoped to their owning card where references permit. |
| ANNOYING | The trial card's two Markdown links looked reasonable but were rejected by the actual safety policy. | sample content / documentation | Corrected the product-owned source and verified zero card errors. |
| ANNOYING | Artifact references do not state existence, resolved absolute path, media metadata, or a unique workspace artifact identity. Duplicate artifact IDs occur on different cards. | Oblivion.Model / Persistence | Recorded for M19b; no speculative redesign. |
| ANNOYING | Only reload has a standalone host capability. Open source, copy path, open artifact, export, and preview remain typed but deferred. | host/platform / Oblivion.App | Availability and required host capability are explicit. |
| ACCEPTABLE | Durable edits occur in an external code editor rather than inside Oblivion. | code-first workflow | Intended for M19a; no rich editor added. |
| ACCEPTABLE | Source references are workspace-relative and require the inspect root to resolve. | Persistence / CLI | `inspect` exposes `rootDirectory` and `manifestPath`. |
| ACCEPTABLE | Workspace-wide validation surfaces 8 pre-existing Markdown warnings from real docs. | Copeland.Markdown / documentation | Preserved with card IDs and locations. |
| ACCEPTABLE | Session output represents initial defaults, not a live visual host session. | Oblivion.App / host | Explicitly labeled `initial-session-defaults`. |
| GOOD | No pointer coordinates are needed for semantic discovery, reload, validation, or invocation. | product surface | Preserved. |

Initial measured friction was 1 blocking, 6 annoying, and 4 acceptable items. M19a removed the blocker and four annoying items. Two annoying items remain: artifact identity/resolution and missing host capabilities.

## What required implementation knowledge

Before M19a, an agent had to read `Program.cs` to learn the positional manifest contract, `OblivionDocsDogfoodCatalog` to discover derived docs cards and IDs, `OblivionCardHandlers` to discover effective actions, `OblivionCardEffectRouting` to understand deferred results, and UI session code to separate selection/expansion from durable state. Raw assets alone were insufficient.

After M19a, normal work needs no implementation source. Implementation knowledge is still required to understand why the docs page is curated rather than general and why artifact IDs are card-local in practice despite lacking an explicit scoped identity type.

## Card assessment

Cards are useful bounded context, source/provenance anchors, artifact holders, and action targets. The trial card made a technical document discoverable, editable, reloadable, and diagnostically attributable. Cards become ceremonial when they only restate a placeholder; the current code-fact, code-theory, and some artifact cards demonstrate that risk. Keep the abstraction, but require a card to bind at least useful content, provenance, an artifact, an action, or status evidence.

## Page assessment

Pages usefully partition cards into `cards`, `execution-roadmap`, `artifacts`, and `docs`, and make filtered discovery cheap. They are more than visual tabs because order, metadata, default selection, and card membership are durable. The generic `cards` page is weakly named and mixes unrelated kinds, while the curated `docs` page is valuable but incomplete. Pages should remain context partitions, not become workflow engines.

## Inspector and expanded view

The inspector is valuable to humans because it consolidates source, diagnostics, artifacts, and actions beside the card list. Those facts must also be semantic; `show` now provides them. Expanded cards represent a meaningful reading mode for long Markdown, but expansion and scroll are session presentation state, not durable product meaning. An LLM should request full body text rather than manipulate expansion.

## Artifact and provenance findings

Card provenance is strong enough for code-first edits: the card TOML and referenced Markdown are distinct and explicit. Artifact ownership is discoverable through the owning page/card, but artifact provenance is weaker. `SourceReference` identifies artifact metadata when present, while `Reference` names the payload; neither reports resolution or existence. The same `workspace-manifest` and `presenter-proof` IDs appear on multiple cards. M19b should make artifact inspection resolvable and scoped before adding generation.

## Diagnostics findings

Structured diagnostics now answer what, where, why, and which workspace/page/card/action/effect was involved. Invalid IDs include a recovery command. A remaining inconsistency is that Copeland diagnostics may have persistence severity `Warning` but display severity text such as `Error`; workspace validation currently uses the former while card runtime diagnostics use the latter. M19b should define one product severity contract rather than silently reclassifying.

## Human UI findings

No human behavior needed removal or replacement. Pages, cards, split inspector, Markdown reading, and expanded view remain useful. The M19a sample-content correction improves both CLI and visual projections. Canonical playback is the regression authority. No visual edit surface is justified by this trial.

## Text/layout observability

No layout defect blocked the workflow. Product state was fully inspectable without pixels, and screenshots remain appropriate only for final visual confirmation. Existing playback already captures semantic targets and state. Add text run/container bounds, baseline, font metrics, wrap, clip, overflow, and semantic region ID only when a real text/layout defect demands them; do not put them in default product JSON.

## Product changes caused by actual use

1. Added an Oblivion-owned deterministic product facade and CLI after semantic discovery failed.
2. Added typed reload/validation through the existing refresh effect after the code-first edit needed a verification path.
3. Scoped diagnostics and corrected the selected technical card's unsafe links after the real workflow exposed both failures.
4. Corrected the playback wrapper's suite-report path after canonical playback passed but the wrapper falsely reported a missing result.
5. Marked docs-catalog cards as generated by `oblivion.docs-dogfood.project`, so durable TOML cards, derived cards, and session state are distinguishable.

## Recommended exact M19b scope

Build **M19b — Artifact Resolution and Local Host Capability Completion**:

1. Define card-scoped artifact identity explicitly and expose resolved path, existence, payload kind, source metadata, and provenance without loading payload bytes by default.
2. Add safe local capabilities for `open-source`, `copy-source-path`, and `open-artifact` behind the existing typed requests; retain structured unavailable results on unsupported hosts.
3. Define a single product diagnostic severity/display contract and preserve original compiler severity separately if needed.
4. Add `show artifact <card-id> <artifact-id>` or an equivalent facade method, plus deterministic tests for missing files, duplicate card-local IDs, and recovery.
5. Do not add generation, execution, networking, rich editing, or new widgets.

That milestone follows the two remaining annoying frictions from actual use rather than returning to abstract architecture.

## Validation and boundaries

The requested `Oblivion.slnx`, regular Machina, slow Machina, Machina build, Aurelian, and Presenter sample commands passed. Canonical playback passed 14/14 with zero failures after the wrapper report-path correction. `git diff --check` passed.

Boundary searches found no Presenter reference in `src/Oblivion`, no Avalonia dependency in Model/Persistence/UI, no Copeland Markdown dependency in Model, no network-service term, and no pixel-golden term. `OblivionWorkbench` retains three pointer/coordinate strings and one `UiActionId` adapter as valid visual-host implementation; the new machine surface contains none. Machina retains `RichTextNode` rendering primitives, not editing machinery. No Copeland TS, Aurelian, or VD-MIR source was modified.

As anticipated by the milestone brief, `dotnet build JointTaskForce.slnx --no-restore -m:1` still fails in the unrelated Copeland CLI with 30 `CS0436` duplicate workspace-ownership type conflicts. M19a did not modify those files.
