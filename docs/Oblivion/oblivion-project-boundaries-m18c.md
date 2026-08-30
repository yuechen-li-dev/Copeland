# Oblivion Project Boundaries M18c

## Oblivion.Model

- Owns: durable workspace/page/card truth, ordering, IDs, content declarations, artifacts, actions, provenance.
- May depend on: .NET base libraries only.
- Must not depend on: Machina, Presenter, Avalonia, Aurelian, Copeland.Markdown, `DocumentMir`, filesystem or renderer APIs.
- Important contracts: `OblivionWorkspace`, `OblivionWorkspacePage`, `OblivionCard`, `OblivionCardContent`, `OblivionProvenance`.
- Deferred: typed executable content only when execution gets its own milestone.

## Oblivion.Persistence

- Owns: format-1 JSON/TOML DTOs, readers/writers, validation, diagnostics, safe path resolution, materialization locations.
- May depend on: Model and Tomlyn.
- Must not depend on: Presenter, Machina, Avalonia, Aurelian, Markdown MIR.
- Important contracts: `OblivionWorkspaceLoader`, format readers/writers, `OblivionWorkspaceLoadResult`, `OblivionWorkspaceLocation`.
- Deferred: additional formats only with compatibility fixtures and explicit versioning.

## Oblivion.UI

- Owns: semantic card/inspector projections, native Machina rendering, Markdown projection, reading style, product session state.
- May depend on: Model, Copeland.Markdown, Machina.Core, Layout, Runtime, and Standard.
- Must not depend on: Presenter, Avalonia controls, Aurelian rendering contracts.
- Important contracts: `OblivionSessionState`, `OblivionMarkdownProjection`, `OblivionCardHandlerRegistry`, `OblivionCardRenderer`.
- M18d: owns typed interactions, nested product scroll priority, and the isolated Machina-action compatibility codec.

## Oblivion.App

- Owns: product composition, workspace loading coordination, typed invocation, effect validation/routing/results, runtime application state, standalone entry.
- May depend on: Model, Persistence, UI.
- Must not depend on: Presenter navigation or host window types.
- Important contracts: `OblivionApplication`, `OblivionInteractionDispatcher`, `OblivionWorkbench`, `OblivionHostCapabilities`, and typed effect request/result variants.
- Deferred: concrete host capability implementations only when product behavior requires them; no execution or networking is implied.
