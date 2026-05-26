# Machina M4f: semantic-text separation and stable control-state geometry

## Summary

M4f separates semantic labels from visual text paint in the render bridge and stabilizes checkbox internal structure across checked and unchecked states.

## Semantic text rule

- Semantic labels (`UiSemantics.Label`) are accessibility/action metadata.
- Semantic labels on non-text roles (`Button`, `Checkbox`, `Switch`, `Input`) do not emit `DrawTextCommand` by themselves.
- `DrawTextCommand` is emitted only for explicit text visuals (`UiRole.Text` / `UiRole.Label`).

## State-stable structure rule

- Standard control state changes should keep row identity and row shape stable unless explicitly documented.
- Checkbox now always emits a persistent `mark-slot` and `mark` row.
- Checked state changes mark paint (filled vs transparent), not row existence.
- Switch internals remain persistent; only thumb X offset and track style differ by state.

## Regression strategy

Primary validation is headless:

- draw-command count assertions,
- lowered row-shape stability assertions,
- resolved-rectangle stability assertions,
- hit-test coverage assertions.

Manual GUI runs are secondary confirmation and not the primary proof path.
