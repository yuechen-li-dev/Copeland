# Machina Badge Intrinsic Layout M7d

## Purpose

M7d fixes the one concrete StandardUI component defect left open by the M7c gallery sweep: `StandardUI.Badge` did not own a real local intrinsic-size or text-placement contract.

The fix is local to Badge. No general layout resolver changes were made. No constraint solver was introduced. No CSS-like padding cascade was introduced.

## M7c defect

M7c documented two coupled badge problems:

- badge text sat too close to the top edge of the badge shell in gallery exports
- small badge-only experiments quickly caused row overflow or negative stack-space failures

That work stopped intentionally because the current badge implementation depended on generic rect/text behavior instead of a Badge-owned layout rule.

## Diagnosis

`StandardUI.Badge` is defined in:

- `src/Machina.Standard/Authoring/StandardUI.cs`
- `src/Machina.Standard/Components/Badge.cs`

Relevant overloads/surfaces:

- `StandardUI.Badge(...)`
- `StandardView.Badge(...)`

`StandardUI.Badge` uses `UI.Text` inside `UI.Rect`. It does not use `StandardView.Badge`, manual flat rows, or `StandardUI.TextBlock`.

Pre-M7d badge path:

1. `StandardUI.Badge(...)` called `Components.Badge.Create(...)`.
2. `Badge.Create(...)` built `UI.Rect(UI.Text(...))` with generic `UiStyle.Padding`.
3. The badge shell had no explicit width or height.
4. In a stack, the shell therefore lowered as `FillFrame`.
5. The child text lowered as a direct anchored text node sized to measured glyph bounds.
6. The renderer received a glyph-sized text rect at the top-left of the shell, so center alignment metadata had no larger label region to work against.

Why the label sat too close to the top:

- the text style said `AlignY.Center`
- but the actual text row rect was only the measured text box at `(0, 0)`
- centering inside a glyph-sized rect is visually the same as top-left painting

Why small local fixes caused overflow or negative stack failures:

- the badge shell was participating in stack layout as `FillFrame`, not as a finite intrinsic-width control
- trying to fake spacing with generic rect padding or ad hoc shell tweaks changed fill behavior instead of defining a badge-local width contract
- when badge width experiments moved toward fixed size without a stable measurement rule, badge rows could exceed available stack width and trigger normal stack remaining-space failures

Badge was therefore participating in Stack/Fit/Fixed/Fill in a fragile way: visually it behaved like an intrinsic leaf, but structurally it was a fill-sized generic rect.

Pre-existing badge coverage was limited to:

- `tests/Machina.Standard.Tests/StandardComponentSnapshotTests.cs`
- `tests/Machina.Standard.Tests/StandardViewFlatTests.cs`

Those tests covered determinism and metadata, but not badge intrinsic sizing or label-region geometry.

## Badge local layout contract

M7d introduces a Badge-local style contract through `StandardBadgeStyle` and `StandardBadgeStyles`.

Key local fields:

- `MinWidth`
- `Height`
- `HorizontalAllowance`
- `TextAlignX`
- `TextAlignY`
- `TextOffsetX`
- `TextOffsetY`
- `TextStyle`

Badge now owns:

- a finite shell width and height
- an explicit `*.label-region`
- centered text inside that region
- optional small deterministic text offsets through asymmetric local insets

This stays entirely inside Badge internals.

## Intrinsic sizing rule

M7d badge intrinsic sizing is:

```text
intrinsicWidth = max(style.MinWidth, measuredTextWidth + style.HorizontalAllowance)
intrinsicHeight = style.Height
```

The text measurement is deterministic and local to Badge. The shell now lowers as a fixed-size stack child instead of a fill-sized generic rect.

## Text placement rule

Badge text placement now uses:

- an explicit `*.label-region` anchored inside the shell
- badge-local horizontal and vertical text alignment
- badge-local `TextOffsetX` / `TextOffsetY` mapped to asymmetric insets so the region stays inside the shell

Default behavior:

- horizontal center
- vertical center
- small downward local offset (`TextOffsetY = 1`) for better visual centering with the current bitmap text renderer

The text command rect stays inside the badge shell.

## Tests

M7d adds or updates headless tests for:

- `Badge_DefaultIntrinsicSize_IsDeterministic`
- `Badge_TextRegion_StaysInsideShell`
- `Badge_TextPlacement_UsesVerticalCenterOrOffset`
- `Badge_Row_DoesNotOverflowWithGalleryExamples`
- `Badge_CustomStyle_OverridesPlacementLocally`
- `Gallery_BadgeRow_ResolvesWithoutOverflowOrOverlap`
- `ComponentGallery_BadgeSection_RenderCommandsAreStable`

These assert geometry and render-command contracts. No pixel-diff system was added.

## Gallery validation

Validation export command:

```powershell
.\tools\Export-MachinaComponentGallery.ps1 -OutputDir artifacts\m7d
```

Artifacts inspected:

- `artifacts/m7d/component-gallery-default.png`
- `artifacts/m7d/component-gallery-interactive.png`

Observed result:

- badge labels no longer hug the top edge
- badge row remains finite and readable
- no new section overlap was introduced
- the rest of the gallery remained visually stable for the current workbench purpose

M7e follow-up:

- the post-fix gallery baseline is now treated as stable enough for ongoing local audits
- remaining roughness is documented in `docs/machina-component-gallery-known-limitations-m7e.md`

## Deferred issues

- the current raster text renderer still has coarse bitmap text limits unrelated to badge layout
- secondary badge chrome remains intentionally simple; M7d fixes layout contract, not broader visual redesign
- broader text-system migrations remain deferred; Badge still uses primitive text with a local placement contract
