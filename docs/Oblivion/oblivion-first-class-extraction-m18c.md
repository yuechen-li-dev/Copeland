# Oblivion First-Class Extraction M18c

## Purpose

M18c makes Oblivion independently buildable and loadable, with Presenter acting as a development host. This is Outcome B: the ownership inversion is complete, with one bounded Presenter host-composition adapter retained for playback compatibility.

## Before M18c

Model, format-1 persistence, Markdown compilation, card projection, session state, deferred effects, and the canonical workspace fixture were compiled into `Machina.Presenter.Sample`.

## After M18c

```text
Oblivion.Model
  ^
  +-- Oblivion.Persistence
  +-- Oblivion.UI -- Machina.Core / Layout / Runtime / Standard / Copeland.Markdown
          ^
          +-- Oblivion.App -- Oblivion.Persistence
                    ^
                    +-- Machina.Presenter.Sample (development host)
```

No Oblivion assembly references Presenter. `Machina.UI` remains Oblivion-neutral.

## Model extraction

`Oblivion.Model` owns workspace, section, page, card, stable IDs, ordered content, action declarations, artifact references, source/reference content variants, and explicit provenance. It has no filesystem, Machina, Presenter, Avalonia, Aurelian, or Markdown compiler reference.

## Persistence extraction

`Oblivion.Persistence` owns format-1 JSON/TOML DTOs, deterministic writers, validation, diagnostics, path safety, materialization, and persistence location state. Absolute root and manifest paths are returned as `OblivionWorkspaceLocation`, not stored in the product model.

## UI extraction

`Oblivion.UI` owns card handlers, compact/inspector view contracts, card rendering, Markdown rendering, reading styles, projection caching, and `OblivionSessionState`. Reusable card layout and scroll geometry helpers were promoted to `Machina.Standard.Components` because both Presenter and Oblivion use them.

## App extraction

`Oblivion.App` owns product composition, deferred effect routing, runtime effect state, action invocation, docs dogfood composition, and a standalone console entry point. The canonical workspace fixture now lives under the first-class app.

## Presenter host inversion

Presenter references all four Oblivion projects and hosts their real code paths. `PresenterNavigationState` owns only palette navigation and presenter-page scrolling; its Oblivion state is a nested product-owned session/application value. Playback and Avalonia/Aurelian development hosting remain in Presenter.

## Session state separation

Selection, expansion, main-stack scroll, inspector scroll, raw-source scroll, expanded-body scroll, and compact-pane choice are owned by `OblivionSessionState`. Presenter forwarding methods are compatibility shims, not state storage.

## Markdown / DocumentMir separation

The durable `OblivionCardBody` contains only format plus source/reference content. `OblivionMarkdownProjection` owns derived `DocumentMir`, preview lines, and compiler diagnostics. Projections are cached at runtime and are never serialized.

## Actions and effects

Durable declarations remain in Model. UI handlers translate declarations into typed request records. `OblivionApplication` validates/routes requests, applies deferred results, and owns runtime effect state. Presenter only invokes the application and records its returned state.

## Avalonia strangler boundary

| Usage | Disposition |
| --- | --- |
| Presenter window host | `AVALONIA_FALLBACK` |
| native input adapter | `AVALONIA_FALLBACK` |
| Machina card, text, stack, grid, and scroll projection | `NATIVE_NOW` |
| future complex editor/widget | `NATIVE_LATER` |
| OS window/clipboard integration | `NO_REASON_TO_REPLACE_YET` |

Avalonia is an implementation fallback, not product truth. Machina earns native widget replacement one control at a time.

## Test restoration

Four focused xUnit projects cover model ordering and dependency purity, JSON/TOML compatibility and path safety, session/projection behavior, typed actions, deferred effects, and assembly boundaries. Canonical Presenter CLI playback remains the end-to-end oracle.

## Compatibility

Format-1 property names and TOML spellings are unchanged. JSON and TOML writers remain deterministic. Existing missing-field, invalid-input, and safe-path diagnostics remain persistence-owned. The presenter page ID is now derived by the host from section and product page IDs.

## Behavior preservation

The canonical 14-scenario playback suite passes after extraction. The standalone app loads the same workspace and composes docs dogfood without Presenter.

## M18d closure

M18d deleted the bounded Presenter host adapter and interaction companion. Page
composition is now Oblivion.App-owned, interaction mapping is Oblivion.UI-owned,
generic input/scroll mechanics are Machina-owned, and product actions/effects
cross typed contracts. The M18 extraction arc is closed as Outcome A.
