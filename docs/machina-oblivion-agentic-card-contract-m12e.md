# Machina Oblivion Agentic Card Contract M12e

## Purpose

M12e hardens Oblivion around one architectural rule: every card is a self-contained applet.

This milestone is not about new execution. It is about putting a stable contract between the workspace shell and each card kind so bugs and changes stay local.

## Card-as-applet doctrine

Every Oblivion card is a self-contained applet.

The shell owns navigation, selection, scrolling, routing, page ordering, card ordering, and persistence loading.

Each card kind owns its model, local state, actions, diagnostics, artifacts, compact view, inspector view, and future effect requests.

## Locality of change

Locality of change is the core design rule.

A Markdown rendering bug should be fixable inside the Markdown card handler.

A future `ImageCard` bug should be fixable in that card kind.

A future `CodeFact` execution bug should be fixable through shared action and effect routing without turning the shell into another per-kind switchboard.

## Shell responsibilities

The workspace shell still owns:

- page ordering
- card ordering
- selection
- scrolling
- high-level routing
- persistence loading
- generic compact-card layout
- generic inspector-panel layout

## Card responsibilities

Each card kind now owns:

- domain model
- local state defaults
- action descriptors
- diagnostics adaptation
- artifact references
- compact rendering data
- inspector rendering data
- future effect request metadata

## Card handler contract

M12e introduces an explicit card handler registry under `samples/Machina.Presenter.Sample`.

The registry resolves a handler by `OblivionCardKind`.

Each handler builds:

- a runtime model from persisted card data
- a compact view model
- an inspector view model
- action descriptors
- deferred effect requests

Unknown or missing handlers stay bounded and surface an error diagnostic instead of crashing the presenter.

## Local card state

M12e adds `OblivionCardLocalState` as deterministic per-card metadata keyed by card id.

The first version is intentionally small:

- `IsExpanded`
- `SelectedArtifactId`
- free-form string properties

The current shell does not expose rich UI mutation for that state yet, but the contract now has a dedicated place for it.

## Actions and future effects

Actions remain descriptors only in M12e.

They describe:

- id
- label
- enabled state
- intent
- whether a future effect is required

Effect requests are also metadata only in M12e.

They are not executable, do not run Roslyn, do not run xUnit, and do not dispatch through Dominatus yet.

## Diagnostics and artifacts

Diagnostics are card-local first and aggregate second.

Markdown diagnostics are adapted into a shared card diagnostic contract so compact cards can show badges and the inspector can show a stable list.

Artifacts are normalized into metadata references only.

M12e does not generate or execute artifacts at runtime.

## Markdown card as first implementation

The `note` handler is the first real agentic-card proof.

It owns:

- Markdown runtime model data
- `DocumentMir` presence
- preview lines
- diagnostics adaptation
- compact Markdown preview routing
- inspector Markdown body routing

Existing doc-dogfood cards continue to use the same Copeland Markdown frontend and now flow through the same handler contract.

## Placeholder code cards

`CodeFact` and `CodeTheory` now also go through the handler registry.

They remain placeholder-only:

- no Roslyn compilation
- no xUnit execution
- no action execution
- no effect execution

They expose deferred action and effect metadata only.

## Relationship to Dominatus

Dominatus remains the intended future execution/orchestration path.

M12e only establishes the card-side contract so a future action can become:

`card action -> effect request -> Dominatus orchestration`

That runtime path is deferred.

## What changed

- added explicit card contract types
- added handler registry and per-kind handlers
- added runtime models, local state, diagnostics, artifact refs, and effect-request metadata
- routed presenter compact cards and inspector through handler-produced views
- added an agentic-card doctrine card to the sample workspace
- added M12e tests and manifest generation

## What did not change

- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` execution
- no Markdown editor
- no file watcher
- no Visionary implementation
- no new card species beyond the contract proof
- no broad presenter shell rewrite

## Deferred work

- executable action dispatch
- Dominatus-backed effect routing
- artifact generation runtime
- richer local state UI
- image/table/video card implementations
- live editing and file watching
