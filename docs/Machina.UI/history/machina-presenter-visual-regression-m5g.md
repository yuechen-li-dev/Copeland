# Machina Presenter Visual Regression Audit (M5g)

M5g converts GUI-observed regressions into headless proofs on the real presenter sample path.

## Rules applied

- Screenshot observations are hypotheses only.
- Headless geometry and render-command assertions are authoritative.
- Text visibility must be proven via `DrawTextCommand` + non-transparent/non-white text color on light card backgrounds.
- Checkbox checked visibility must be proven via explicit mark geometry and checked-state fill command.
- Dynamic text fitting is deferred; default style geometry must be internally consistent first.

## Coverage added

- `PresenterSample_TextNodes_HaveVisibleTextStyles`
  - Asserts `DrawTextCommand` exists for title/count/footnote.
  - Asserts color is explicit and visible.
  - Asserts text rects are inside card content.
- `PresenterSample_IncrementButton_TextFitsShell`
  - Asserts a single `Increment` text command.
  - Asserts text size matches button style.
  - Asserts measured text bounds fit label region.
- `PresenterSample_CheckedCheckbox_MarkIsVisible`
  - Asserts checked mark rect exists, has area, and is inside checkbox shell.
  - Asserts checked state emits visible mark fill; unchecked emits transparent mark fill.
  - Asserts row identity stability across checked/unchecked states.
- `StandardButton_DefaultStyle_TextFitsDefaultShell`
  - Locks default button text-size/shell consistency with deterministic text measurement.

## M5g outcomes

- Raw `UI.Text` in hosted `SettingsCard` now uses explicit theme foreground color in source authoring.
- Presenter button text-fit behavior is headlessly verified without introducing dynamic font sizing.
- Checkbox checked mark visibility is headlessly verified using explicit mark fill + geometry assertions.
