# Machina Presenter Sample Contract (M5f)

## Canonical sample status

`/samples/Machina.Presenter.Sample` is the canonical Machina reference app for authoring, state, dispatch, and theme handoff.

## Authoring contract

- Screen/document layout is authored as a flat `UiDocument` row table.
- Top-level document places one hosted component row: `settings-card`.
- `SettingsCard` internals stay localized in `DemoDocumentFactory.SettingsCard(...)` using `StandardUI` controls.
- App-level document does not manually decompose checkbox/switch internals.

## State and dispatch contract

- State is immutable record data: `DemoState`.
- State transitions use plain C# dispatch in `DemoStateDispatch.Dispatch(...)`.
- No DispatchTable path is required for the canonical sample.

## Theme contract

- Root theme is explicit C# data (`StandardTheme`) and may be customized with `with`.
- Sample presenter window defines an explicit `AppTheme` and passes it into `DemoDocumentFactory.Build(...)`.

## Test contract

Dedicated project: `tests/Machina.Presenter.Sample.Tests`.

This project validates:

1. document builds in default state,
2. top-level layout is flat and hosted,
3. key resolved geometry for button/checkbox/switch internals,
4. hit targets return expected typed actions,
5. geometry stability across state toggles,
6. plain C# dispatch behavior,
7. theme propagation for card/button and checkbox/switch styles.

Headless tests are the source of truth; manual GUI checks are secondary.
