# Machina M5c: style records and explicit root theme

- Style is ordinary immutable C# record data.
- Theme is explicit input data passed from root composition.
- Advanced customization uses C# `with` expressions.
- No CSS cascade, no selector matching, and no ambient mutable style globals.
- Layout-affecting geometry stays explicit in component style fields and produced rows/frames.


## M5c split update (M5c0 -> M5c1)

- M5c0 stabilized the style/theme scaffold and kept existing behavior green.
- M5c1 fully wires Button and Card style records in `StandardUI.Button` and `StandardUI.Card`.
- `StandardButtonStyle` now drives shell dimensions (`Width`/`Height`) and label `TextStyle` when explicitly passed.
- `StandardCardStyle.ContentInset` is layout-affecting and defines an explicit anchored content row.
- Typical calls stay simple; advanced overrides are immutable C# `with` customizations.


## M5c2 Input style record contract

- `StandardInputStyle` is now fully wired in `StandardUI.Input`.
- Input shell metadata (background/foreground/border), shell size (`Width`/`Height`), content inset (`ContentInset`), and text styles are all explicit style-record fields.
- Input builds an explicit `*.content` anchored row for text geometry; `UiStyle.Padding` stays non-layout paint metadata (`0` for input shells).
- Placeholder rendering is deterministic and uses `PlaceholderTextStyle`; value rendering uses `TextStyle`.
- Style resolution precedence is simple and explicit: `style:` parameter > `theme.Input.Default` > `StandardTheme.Default.Input.Default`.


## M5c3 Checkbox and Switch style wiring

M5c3 fully wires `StandardCheckboxStyle` and `StandardSwitchStyle` into `StandardUI.Checkbox` and `StandardUI.Switch`. Checkbox and switch geometry, visual style, gap spacing, and label text style now resolve deterministically from the selected style record (`style:` if supplied, otherwise theme default). Checked/on state changes values (for example mark fill and thumb X) without changing row identity.

## M5c4 consolidation note

M5c4 consolidates the M5c style model as one coherent system: consistent `StandardTheme.<Family>.Default` naming, explicit layout-vs-paint guidance, clarified StandardUI vs StandardView roles, and canonical presenter sample positioning. See `docs/machina-style-theme-m5c4.md`.

## M5d follow-up note
M5c style records remain unchanged. M5d clarifies authoring contract only: `StandardUI` is the primary component surface; `StandardView` is metadata-oriented with advanced sub-part helpers for manual composition. See `docs/standard-ui-vs-standard-view-m5d.md`.

- M5f update: presenter sample is the canonical reference app and is contract-tested in tests/Machina.Presenter.Sample.Tests (document shape, hosted component boundary, localized StandardUI internals, plain C# dispatch, theme propagation, and geometry/hit-target stability).


## M5g presenter visual regression audit
M5g requires converting presenter screenshot regressions into headless geometry/render-command tests. Verify UI.Text visibility by DrawTextCommand presence + visible text color + in-card rect; verify default button text-style size fits default shell; verify checkbox checked mark via explicit mark geometry and visible fill when checked (transparent when unchecked). Dynamic text fitting remains deferred.
