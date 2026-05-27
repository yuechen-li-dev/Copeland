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


## M5g presenter visual regression audit
M5g requires converting presenter screenshot regressions into headless geometry/render-command tests. Verify UI.Text visibility by DrawTextCommand presence + visible text color + in-card rect; verify default button text-style size fits default shell; verify checkbox checked mark via explicit mark geometry and visible fill when checked (transparent when unchecked). Dynamic text fitting remains deferred.

## M6a Machina.Text boundary note

M6a establishes `Machina.Text` as a separate subsystem contract. Frame/stack/table layout still places component rectangles; `Machina.Text` will lay out text only inside those assigned boxes.

Wrap, overflow, leading, block/list spacing, and text alignment are text-domain primitives and must not be added to general layout semantics.

Headings remain a component/layout responsibility (for example title variant selection in `StandardUI.Card`), not a supported inline markup mechanism inside restricted Machina text source.

The current simple `UI.Text` path is transitional until `Machina.Text` parser/model/layout integration milestones (M6b+) are complete.
