# Oblivion Product Contract — M18b

> M18c status: this contract is now realized by the first-class `Oblivion.Model`, Persistence, UI, and App projects. See [the M18c extraction record](oblivion-first-class-extraction-m18c.md). The remaining presenter host-composition adapter is explicitly owned by M18d.

## Product definition

Oblivion is a persistent technical-work workspace for Visionary, organized as notebook pages containing cards. A workspace preserves the user's authored and collected technical context—content, artifact references, provenance, and product actions—while Machina projects that state into native UI. Oblivion is a first-class product, not a presenter section, Machina sample, renderer, layout tree, `DocumentMir`, or universal execution model.

The governing law is:

```text
Oblivion state is truth.
Machina View is a projection.
Layout is a realization.
Rendering is an output.
```

## Product loop

The primary unit of user work is a card in the context of a page. A user opens a workspace, chooses a page, reads or selects a card, follows its artifact/provenance links, and invokes an available product action. The application applies the resulting domain change or requests a bounded external effect, records durable output only when product semantics require it, then projects the updated workspace again.

```text
load workspace
  -> choose page
  -> inspect or act on card
  -> apply product transition or request effect
  -> record durable result/provenance when appropriate
  -> project to Machina View
```

Selection, expansion, scrolling, layout, and raster output do not participate in this truth loop.

## Durable product model

The M18c model should use explicit records and discriminated content records. It does not need repositories, providers, factories, dependency injection, or a universal application framework.

### Workspace

A `Workspace` is the durable root of one technical-work context. It owns stable identity, title, ordered pages (optionally grouped for navigation), the default page, and workspace-level metadata. A filesystem root and manifest path are persistence realization details, not workspace identity.

### Page

A `Page` is an ordered notebook surface inside a workspace. It owns stable identity, title, optional description/tags, and an ordered list of card identities or cards. It is not a presenter tab and must not contain `PresenterPageId`. A presenter or application host may map a page to navigation state outside the product model.

### Card

A `Card` is the principal durable unit of work. It owns stable identity, kind, product status where status has domain meaning, title/subtitle, tags, content, declared actions, artifact references, and provenance. It does not own rectangles, preferred height, expansion, scroll offsets, hit targets, a Machina node, or a rendered/compiled document.

The current kinds (`Note`, `Status`, `UiPreview`, `Artifact`, `CodeFact`, and `CodeTheory`) are useful evidence, not a mandate for an inheritance hierarchy. M18c should retain them as a small explicit enum plus content records until real behavior requires a different shape.

### CardContent

`CardContent` is the durable declaration of what a card contains. Initial explicit variants should cover:

- inline plain text;
- inline Markdown source;
- a relative Markdown content reference;
- artifact-oriented content/reference metadata;
- a bounded declaration/reference for future executable Copeland content.

The current `OblivionCardBody` combines durable source, persistence location, preview text, `DocumentMir`, and diagnostics. M18c must split that mixed record: source/reference is product state; resolved path is persistence state; `DocumentMir`, preview lines, and compiler diagnostics are derived application/UI state.

### Artifact

An `Artifact` is a stable product-level reference to a technical output or input, with identity, label, kind/media information, and a location or opaque reference understood by the application. `generated` may describe origin policy, but a raw filesystem path must not become semantic identity. Opening or generating an artifact is an effect, not behavior on the record itself.

### Provenance

`Provenance` answers where a durable card, content item, or artifact came from and how it was produced. The current model has fragments (`SourcePath`, `Generated`, workspace/page/card IDs) but no coherent provenance record. M18c should add a small explicit value such as source kind, source reference, producer/action identity, and optional parent artifact/card identity. Do not require every item to fabricate provenance; allow an explicit unknown/manual origin.

### ProductAction

A `ProductAction` is a typed semantic command the product exposes for a workspace/card, such as refresh content, open a source reference, export a card, or request execution. Its identity and intent are product-owned. UI controls lower to an action invocation; the application validates availability and either performs a pure product transition or emits an effect request.

The current `OblivionCardAction`, `OblivionCardActionDescriptor`, `OblivionCardActionInvocation`, and `OblivionCardEffectRequest` overlap. M18c should preserve compatibility while separating:

- durable action declaration: model;
- current availability and labels: derived UI/application projection;
- typed invocation: application contract;
- external effect request/result: application/runtime boundary;
- `UiActionId`: Machina projection detail.

Do not make string property bags the long-term semantic contract. Keep the existing strings as a compatibility adapter until each evidenced effect has a boring typed request.

### Agents, actions, and effects

Agents are application actors that may propose or invoke the same product actions as a person. They do not own a parallel card model. An action is validated against current product state; a pure action changes product state; an effect request crosses to a host-owned capability; an effect result may produce a product transition, artifact, diagnostic, or provenance entry. Effect execution does not belong in Machina.UI or Presenter. The current all-deferred `OblivionCardEffectRouter` is an application seam, not a UI framework primitive.

## State taxonomy

### `DURABLE_PRODUCT_STATE`

| Current or intended state | Decision |
|---|---|
| workspace ID/title/default page | durable product state |
| section/page hierarchy and ordering | durable product state; section is optional navigation grouping, not presenter ownership |
| page ID/title/description/tags/card order | durable product state |
| card ID/kind/title/subtitle/tags/content | durable product state |
| card status | durable only when it communicates product/work status; transient execution status belongs to runtime |
| declared product actions | durable when authored with the card; availability is derived |
| artifact references | durable product state |
| provenance | durable product state, currently incomplete |
| completed effect output intentionally attached as an artifact/history item | durable after an explicit product transition |

### `SESSION_UI_STATE`

| Current state | Decision |
|---|---|
| selected page/card | session UI state |
| expanded card | session UI state |
| card body scroll offset | session UI state |
| main-stack/page scroll offset | session UI state |
| inspector scroll offset | session UI state |
| raw-source scroll offset | session UI state |
| focused pane/control | session UI state |
| compact pane selection | session UI state |
| hover, pointer capture, drag/thumb state | session UI state |
| presenter section/tab selection | presenter-only session state, except an Oblivion app may have its own selected page |

These values may later participate in an optional session-restoration document. They must not be added to the core workspace/card persistence contract merely to restore a window.

### `DERIVED_VIEW_STATE`

| Current state | Decision |
|---|---|
| parsed `DocumentMir` | derived compiler projection |
| Markdown preview/inspector lines and compiler diagnostics | derived projection |
| action availability and action badges | derived from product/application capability state |
| `OblivionCardRuntimeModel`, compact view, inspector view, built card | derived application/UI projection |
| preferred/expanded heights and shell mode | derived UI state |
| interaction maps, hit regions, scrollbar geometry | derived layout/input projection |
| `UiNode`, `UiDocument`, resolved layout, presentation frame | Machina projection/realization/output |

### `RUNTIME_ONLY_STATE`

| Current state | Decision |
|---|---|
| in-flight effect requests and transient results | runtime/application state unless explicitly committed to product history |
| renderer/layout/Markdown caches and counters | runtime-only diagnostics/optimization |
| host window size and backend handles | runtime-only host state |
| playback runner state and trace assembly | test tooling state |
| loaded absolute paths and manifest root | persistence/runtime realization state |

## Markdown and DocumentMir

Markdown is one supported authored card-content format, not the Oblivion model. Oblivion owns the source text or a content reference and its product metadata. `Copeland.Markdown` compiles Markdown into `DocumentMir`. Oblivion.UI may consume an application-produced compiled projection to build Machina views.

`DocumentMir` is compiler/document infrastructure. It must not be persisted as Oblivion truth, become `CardContent`, own workspace/page/card semantics, or absorb actions, artifacts, and provenance. The current `OblivionCardBody.DocumentMir` field is a `KNOWN_PRE_M18C_VIOLATION` caused by combining source and projection in one sample-local record.

## Future executable content

Future executable Copeland or Copeland-TS content should be referenced by an explicit content declaration and executed through a typed application effect. The durable model may identify language, source/artifact reference, entry point, and declared inputs; it must not own compiler MIR, process handles, xUnit/Roslyn objects, execution services, or renderer state. Execution results become durable only through a deliberate product transition that creates content, diagnostics, artifacts, or provenance.

Native Machina must not depend on Copeland TS. Future C# and TS authoring should converge on compatible semantic UI contracts, not on a shared lifecycle/hook system.

## Non-goals

Oblivion is not:

- a presenter page, gallery, fixture, or diagnostic palette;
- a renderer, layout engine, widget lifecycle, virtual DOM, or hook model;
- `DocumentMir` or a generic document framework;
- a universal execution/orchestration framework;
- an editor, network service, OS automation layer, or Leviathan implementation in M18b/M18c;
- a reason to move Aurelian, alter VD-MIR, or redesign Copeland TS.

## Target dependency rules

```text
Oblivion.Model
    no Machina dependency
    no Copeland.Markdown / DocumentMir dependency
    no renderer, layout, presenter, host, or filesystem dependency

Oblivion.Persistence
    -> Oblivion.Model
    -> serialization libraries only as required

Oblivion.UI
    -> Oblivion.Model
    -> Machina.Core
    -> Machina.Standard
    -> minimal Machina.Runtime contracts only when required
    MAY consume an application-owned Markdown projection contract

Oblivion.App
    -> Oblivion.Model
    -> Oblivion.Persistence
    -> Oblivion.UI
    -> native Machina host/runtime and explicit compiler/effect adapters

Machina Presenter / DevTools
    -> Machina UI
    MAY -> Oblivion.UI and Oblivion.App hosting adapters for development

Oblivion.*
    MUST NOT -> Machina.Presenter.Sample or presenter navigation/playback types
```

Paths, Machina node IDs, presenter page IDs, and renderer commands are never product identity.

## Recommended M18c extraction

M18c should perform one coherent first-class extraction without redesigning behavior:

1. Create `src/Oblivion/Oblivion.Model` and move/split workspace, page, card, content, artifact, provenance, and durable action declarations. Remove `PresenterPageId`, resolved paths, `DocumentMir`, preview lines, and view state from these records.
2. Create `src/Oblivion/Oblivion.Persistence` and move JSON/TOML DTOs, readers/writers, loader, validator, path-safety logic, and diagnostics. Preserve format 1 byte/semantic compatibility and adapt it into `Oblivion.Model`.
3. Create `src/Oblivion/Oblivion.UI` and move card handlers/projections, Markdown presentation adapter, card/page/inspector renderers, interaction-map construction, and Oblivion session-state reducers. Replace presenter-named helpers with Oblivion-owned or Machina-standard equivalents only where needed by the moved code.
4. Create `src/Oblivion/Oblivion.App` as the small product composition/action/effect boundary. Move the deferred effect router and product navigation/dispatch out of Presenter. Do not add DI or execution.
5. Keep `Machina.Presenter.Sample` as a development host. Its navigation, component gallery, diagnostics, export UI, Avalonia adapter, and playback runner remain presenter/devtool owned; it references Oblivion rather than owning it.
6. Re-establish focused tests under `tests/Oblivion/*` and a playback integration project. The former `Machina.Presenter.Sample.Tests` project was deleted by JTF cleanup; the live CLI playback suite is the current regression oracle and must remain green throughout extraction.

Do not change the JSON/TOML format, action behavior, card behavior, layout behavior, or renderer output as part of the move. Add adapters first, then delete sample-local duplicates after complete test and playback comparison.

## M18b decision

**Outcome A — Boundary is clear.** The current assembly is severely mixed, but the concepts are sufficiently explicit to extract directly. M18c is relocation plus separation of product truth from compiled/view/session projections; it does not require a preliminary domain-invention milestone.
