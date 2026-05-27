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
