# Machina Presenter Keyboard Input M12g

## Purpose

M12g adds keyboard input plumbing to the presenter shell.

This milestone is intentionally narrow:

- extend the presenter input backend seam to include keyboard input
- translate Avalonia key and text input into backend-neutral presenter events
- route basic keyboard navigation and selected-card behavior through the existing reducer path
- keep Markdown editing, Roslyn execution, xUnit execution, and Visionary deferred

## Input backend boundary

Avalonia is the current sample input backend, not the architecture.

The architecture is now:

```text
Avalonia KeyDown/KeyUp/TextInput
  -> AvaloniaPresenterInputBackend
      -> PresenterInputEvent
          -> PresenterKeyboardInputRouter
              -> PresenterNavigationActions
                  -> PresenterNavigationDispatch
                      -> PresenterNavigationState
```

Avalonia event args stop at `samples/Machina.Presenter.Sample/AvaloniaPresenterInputBackend.cs`.

Backend-neutral state, actions, routing, and Oblivion card handlers do not reference Avalonia types.

## Avalonia keyboard adapter

`AvaloniaPresenterInputBackend` now translates:

- `KeyDown`
- `KeyUp`
- `TextInput`
- modifier flags
- unknown keys as deterministic `PresenterKey.Unknown`

Pointer, wheel, and scrollbar behavior remain on the same sample adapter and keep the M10b/M11c behavior intact.

## Backend-neutral keyboard model

M12g adds sample-local but backend-neutral keyboard input records:

- `PresenterInputKind.KeyDown`
- `PresenterInputKind.KeyUp`
- `PresenterInputKind.TextInput`
- `PresenterKey`
- `PresenterKeyModifiers`
- `PresenterKeyboardInput`

These types are intentionally small and deterministic.

They are enough for:

- section and tab navigation
- page scrolling
- deferred selected-card action routing
- future text/editor routing

They are not a text buffer, editor model, or caret model.

## Supported shortcuts

Current M12g keyboard routing supports:

- `Ctrl+ArrowUp` selects the previous sidebar section
- `Ctrl+ArrowDown` selects the next sidebar section
- `Ctrl+ArrowLeft` selects the previous local tab
- `Ctrl+ArrowRight` selects the next local tab
- `ArrowUp` scrolls the selected page up by one line step
- `ArrowDown` scrolls the selected page down by one line step
- `PageUp` scrolls the selected page up by one page step
- `PageDown` scrolls the selected page down by one page step
- `Home` scrolls the selected page to the top
- `End` scrolls the selected page to the bottom
- `Escape` clears the selected Oblivion card selection
- `Ctrl+R` routes the selected Oblivion card's first deferred effect action through the existing M12f action/effect seam

`Ctrl+R` still does not execute Roslyn, xUnit, artifacts, or shell commands. It only produces the same deferred effect result the M12f router already exposes.

## Text input without editing

Text input is now translated into backend-neutral `TextInput` events.

M12g still does not edit Markdown or any card body.

Current behavior is deliberate:

- text input can be captured by the backend seam
- the presenter keyboard router currently ignores it as a deterministic no-op
- future editor or card-local input targets can consume it later without reintroducing Avalonia types into the shell

## Relationship to Oblivion cards

Oblivion cards still remain applets with card-local models, diagnostics, artifacts, views, and deferred action/effect metadata.

M12g does not move keyboard behavior into card handlers.

The shell owns:

- keyboard navigation
- page scrolling
- selected-card clearing
- selected-card action routing

Card handlers still own:

- action descriptors
- effect request creation
- deferred effect semantics

## Relationship to future Markdown editor

M12g is the input plumbing step before any editor work.

It does not add:

- text buffers
- caret movement
- selection ranges
- inline editing UI
- file save/write behavior
- Markdown mutation from keyboard input

That future editor can reuse the neutral `TextInput` and key events introduced here.

## What changed

- extended presenter input records with keyboard kinds and keyboard payloads
- added backend-neutral key and modifier models
- translated Avalonia key/text input through the sample input backend
- added `PresenterKeyboardInputRouter`
- routed keyboard navigation through the existing presenter action/reducer path
- kept pointer/wheel/scrollbar behavior intact
- added M12g keyboard manifest output and tests

## What did not change

- no full Markdown editor
- no text buffer model
- no caret or selection model
- no file save/write from keyboard input
- no Roslyn execution
- no xUnit `[Fact]` / `[Theory]` runtime
- no Visionary implementation
- no production renderer/core/layout rewrite

## Deferred work

- Markdown editing UI and text model
- inspector action hit regions and richer keyboard focus
- scaling and zoom input backend support
- Dominatus-backed real effect execution
- Roslyn compilation and execution
- xUnit runtime execution
- Visionary code editor/source workspace
