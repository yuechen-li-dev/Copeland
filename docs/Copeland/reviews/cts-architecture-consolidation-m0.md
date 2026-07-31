# CTS-ARCHITECTURE-CONSOLIDATION-M0

**Status:** complete as an audit-first consolidation. This review establishes
the current ownership/documentation baseline and deliberately does not perform
a speculative cross-repository browser-runtime rewrite.

## Deliverables

- [Feature inventory](../copeland-feature-inventory.md)
- [Semantic ownership map](../architecture/semantic-ownership.md)
- [Language overview](../language/overview.md) and
  [feature-status page](../reference/feature-status.md)
- [Generated artifact inventory](../reference/generated-artifacts.md)
- [Projected-table conventions](../tooling/projected-tables.md)
- [Diagnostics catalog](../reference/diagnostics.md)

The root Copeland documentation index now points at these current documents;
milestone/design records remain historical evidence rather than alternate user
manuals.

## Browser-host/runtime audit

| Concern | Implementation | Classification | Owner and finding |
| --- | --- | --- | --- |
| Semantic attachment fact | `Semantics/Bound/BoundNodes.cs: HostAttachmentMir` | Canonical source | Binder derives stable attachment/host/capability facts. |
| Attachment transport | `AttachmentPlanArtifactEmitter.cs` | Generated projection | `attachments.json` v1 is deterministic data transport. |
| Frame/transition transport | `ComponentFrameArtifactEmitter.cs` | Generated projection | compiler emits component-frame envelope V1; TSPack executes it through a fixed runtime executor. |
| C# attachment lifecycle | `RendererAttachmentRegistry` in `BoundNodes.cs` | Backend realization | Canonical for in-process C# semantic-runtime tests, not browser DOM. |
| C# component state/frame | `ComponentStateRuntime.cs` | Backend realization | owns typed state/effect test runtime; preserves attachment identity. |
| Browser host lifecycle | TSPack `cmd/tspack/runtime/browser-v1/index.js` | Canonical browser runtime source | owns host lookup/readiness/recovery, attachments, frames, adapters, traces, diagnostics, cleanup. Go materializes it. |
| Browser host JS in `dist/` | TSPack materialized `packages/copeland-browser-v1/index.js` | Generated projection | never edit as source. |
| Custom Element executor | adapter map inside TSPack browser host | Adapter-specific | owns custom-element creation/update/removal and opaque DOM subtree. |
| React root wrapper | website `ReactRendererAdapter.ts` / `HostAttachmentRuntime.ts` | Sample/application-only | owns only the application's outer React root, not compiler attachment plans. |
| Sample `runtime/browser-v1.js` modules | individual browser samples | Compatibility bridge | historical host API fixture inputs; not the attachment/frame runtime authority. |
| `Main.ts` bootstrap | sample application | Application bootstrap | should remain limited to its own root and app state. |

The browser proof in `samples/copeland-ts/copeland-website-m0/browser-proof.mjs`
exercises semantic host replacement, duplicate/missing host behavior,
attachment mount/update/unmount, plan replacement, frame registration, typed
event dispatch, state-selected child frames, deepest-first teardown, contextual
runtime diagnostics, and frame destruction. TSPack's materialization tests
cover deterministic host/artifact output.

### Browser topology decision

The intended M0 topology is:

```text
Copeland owns semantic facts and emits artifacts
TSPack validates/materializes artifacts and generates bootstrap
@copeland/browser-v1 owns browser lifecycle/runtime state
renderer adapters own private subtrees
application owns only app bootstrap
```

Do **not** move browser lifecycle into Copeland's binder, and do **not** teach
TSPack to infer component, adapter, capability, or host semantics.
TSPACK-BROWSER-RUNTIME-SOURCE-M0 extracted the runtime to ordinary checked-in
TSPack source. Go materializes and configures it; it does not independently
reimplement browser lifecycle semantics.

## Duplicate-semantics report

| Concept | Implementations | Difference / consumers | Canonical owner and safe action | Risk |
| --- | --- | --- | --- | --- |
| Browser host API | TSPack generated host; several sample `runtime/browser-v1.js` files | samples expose older/basic helpers; TSPack host adds attachments/frames | TSPack host is browser authority; retain samples as compatibility bridges until each fixture consumes generated host | Removing files could break manifest package resolution |
| Attachment lifecycle | C# `RendererAttachmentRegistry`; JS browser registry | same lifecycle vocabulary but different realizations/opaque roots | each is its runtime realization; share artifact/behavioral fixtures, not implementation code | accidental parity drift |
| Component transitions | C# `ComponentStateFrame`; compiler-emitted JS frame contracts + browser executor | C# supports typed effects/completion; browser subset is zero-payload bounded | binder owns transition facts; browser executor must remain a constrained realization | browser may overgrow into a second semantic binder |
| Attachment-plan validation | emitter; TSPack loader; browser runtime | repeated structural validation at trust boundaries | legitimate defensive validation, not semantic duplication | keep v1 requirements synchronized |
| Host identity | compiler `HostBoxId`/selector; browser `resolveRendererHost` | compiler projects selector; runtime resolves concrete node | legitimate projection/materialization boundary | runtime must not select alternate hosts |
| React mounting | sample React root wrapper; Custom Element attachment runtime | both call mount/update/unmount-shaped operations but own different subtrees | application root remains adapter-private app bootstrap; do not merge with attachment registry without a compiler plan | conflating root with semantic attachment |
| Project/module context | TSPack resolver; `CopelandProjectContext` | TSPack resolves manifests, compiler reopens descriptor | legitimate producer/consumer boundary | no fallback source scanning after context exists |
| Layout/document tables | domain models; CLI providers | tables serialize facts for inspection | projection, not duplication | avoid source reconstruction in providers |

No dead browser runtime path was deleted: sample files have explicit manifest
consumers and differ behaviorally. This is intentionally conservative.

## Bound/MIR organization assessment

`Binder.cs` is 9,739 lines and is the dominant consolidation risk. It currently
combines declaration discovery, type binding, control flow, records/enums,
TSON/tables, project/module facts, layout/stream/document binding, component
discovery/captures/state/presentation, adapter planning, and diagnostics.
`BoundNodes.cs` is 1,553 lines and contains program nodes, layout/component
facts, attachment contracts, and a C# runtime registry. `LayoutDataCompiler.cs`
(1,809 lines) and `TextDocuments.cs` are coherent domain owners but have
important binder ordering dependencies.

M0 leaves public semantic types in place because a mechanical split risks
breaking every consumer. M1 extraction order:

1. `Semantics/Components`: component discovery, capture analysis, state,
   presentation, attachment planning, and related bound types.
2. `Semantics/Layout` and `Semantics/Documents`: explicit services consuming
   symbols/types rather than syntax directly.
3. `Semantics/Tables` and `Semantics/Types`: table/TSON validation and generic
   specialization helpers.
4. Move C# in-process attachment runtime out of `BoundNodes.cs` into an
   explicitly named runtime-test/realization namespace without changing its
   semantic inputs.

This is domain partitioning, not a line-count split. Preserve one discoverable
entry point and existing common traversal patterns.

## Binder phase map

The current binder has hidden interleaving rather than a fully named pass
pipeline. The intended dependency-respecting sequence is:

```text
declaration scan -> symbols/imports/project contracts -> types/generics
 -> documents -> component definitions/captures -> layouts/streams/bindings
 -> component instances/state/transitions/presentation
 -> renderer/attachment planning -> validation -> backend-ready program/MIR
```

Existing code already has useful local helpers and canonical validation; M0
does not extract passes merely to match this diagram. The first high-value M1
move is a component-presentation planning service that consumes already-bound
instances/layout facts and returns attachments, avoiding any re-interpretation
of syntax by a backend or TSPack.

## Consolidations implemented in M0

| Before | After | Owner clarified | Compatibility impact / validation |
| --- | --- | --- | --- |
| authoring truth was a single guide amid many milestone records | overview, inventory, status, ownership, artifact, table, and diagnostics documents form a linked current map | one user-facing documentation surface | no code or wire-format change |
| browser source authority was easy to mistake for sample runtime or `dist` files | ownership map identifies TSPack's generator as authority and classifies sample modules | TSPack browser runtime | sample compatibility files retained |
| artifact/version expectations were scattered | one artifact inventory distinguishes v1 attachment plans from unversioned frame modules | compiler transport / TSPack materialization | flags, rather than silently changes, frame compatibility gap |
| table identity/provenance conventions were implicit | common conventions and family inventory are documented | canonical domain models | no table schema churn |

## Prioritized follow-up plan

1. **Version component frames.** Replace the unversioned executable object
   contract with a versioned envelope/fixed executor, preserving v1 loading.
2. **Binder component-phase extraction.** Create an explicit component/
   attachment planning service with snapshot tests before changing behavior.
3. **Diagnostic collision guard.** Add a cheap source/test catalog check and
   complete runtime contextual diagnostic coverage.
4. **Projected-table helper.** Centralize identity/provenance row construction
   and relation schema version policy, retaining domain-specific schemas.
5. **Runtime parity suite.** Execute the same attachment/frame behavioral cases
   against C# and browser realizations where their bounded contracts overlap.

## Completion assessment

The audit and documentation completion criteria are met: implemented features,
owners, browser paths, duplicate candidates, contracts, diagnostics, tables,
and extension rules are discoverable from current documents. Safe M0
consolidation is documentation/ownership clarification; the remaining code
refactors are explicitly isolated above. Browser runtime source extraction is
complete in sibling TSPack; generated browser output remains a projection,
never a source-of-truth file.
