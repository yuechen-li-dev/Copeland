# Machina Presenter Geometry Regression (M4e)

M4e adds headless regression coverage for the real presenter sample document path.

## What is covered

- Real sample document creation is exposed through `DemoDocumentFactory` and reused by tests.
- Tests resolve at `640x360` and assert card/content/control rectangles.
- Tests assert button, checkbox, and switch internals including hit-test behavior.
- Checkbox checked marker now uses explicit centered geometry (`mark-slot` + `mark`) instead of text glyph positioning.

## Validation policy

Manual GUI screenshots are secondary confirmation only.
Primary proof is deterministic headless assertions on lowered rows, resolved rectangles, metadata, and hit-test targets.

If a control looks wrong, add a resolved-geometry assertion first, then patch the control.


## M4f note
M4f adds semantic-text separation and state-stable control geometry. Semantic labels are not paint; explicit text visuals emit draw text. Checkbox/switch state changes should preserve row identity/shape and adjust stable style/geometry values instead of adding/removing rows.
