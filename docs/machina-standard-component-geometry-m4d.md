# Machina Standard component geometry hardening (M4d)

## Summary

M4d hardens `StandardUI.Button`, `StandardUI.Checkbox`, and `StandardUI.Switch` so their internal geometry is explicit and headlessly testable.

## Contract

- Component internals that affect geometry lower into explicit rows and frames.
- Style padding is paint metadata only and does not control child/text placement.
- Button now uses explicit `label-region` + `label` child rows.
- Checkbox exposes explicit `box`, optional `marker`, and `label` rows.
- Switch exposes explicit `track`, `thumb-slot`, `thumb`, and `label` rows.

## Validation approach

Headless xUnit geometry/hit-test tests are the source of truth.

Manual GUI runs remain a confirmation pass and are not used to discover layout correctness.

## North star

Layout and component correctness should be unit-testable without browser, Avalonia window, or screenshot-based debugging.
\n- M4e note: presenter sample geometry is now validated with headless resolved-rectangle assertions; manual GUI checks are secondary.


## M4f note
M4f adds semantic-text separation and state-stable control geometry. Semantic labels are not paint; explicit text visuals emit draw text. Checkbox/switch state changes should preserve row identity/shape and adjust stable style/geometry values instead of adding/removing rows.
