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
