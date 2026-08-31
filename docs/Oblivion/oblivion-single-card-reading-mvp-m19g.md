# Oblivion single-card reading MVP — M19g

## Goal

M19g establishes Outcome A: the smallest Oblivion notebook surface is visually trustworthy. The product is one standalone executable, one page-level vertical Card stack, one semantic Markdown Card, and two session-only reading states. The milestone does not expose Presenter, navigation, an inspector, development palettes, Compare, Mermaid, galleries, or additional Cards.

## Standalone host

`src/Oblivion/Oblivion.Standalone` builds the `Oblivion` GUI executable. It materializes one code-authored `PresentationContent.Markdown` through `PresentationMaterializer`, projects the resulting Card through `OblivionContentPresenterSelector`, renders the product shell through Machina and Aurelian's CPU raster path, and mounts the mature Avalonia document only inside the expanded body rectangle.

The standalone window accepts Enter, Space, or the square header control to dispatch the existing encoded Oblivion expansion action. `--capture <path>` and optional `--expanded` capture the actual standalone client composition for bounded product proof; they do not invoke Presenter or an operating-system automation layer.

The M19d content host moved from the Presenter sample into `src/Integrations/Oblivion.Avalonia`. Presenter continues to consume that integration, but the standalone product no longer needs to run inside Presenter.

## Machina responsibilities

Machina owns:

- the 2560 × 1440 page surface and background;
- the ordinary vertical Card stack and its 24px inter-card gap contract;
- 88px horizontal and 72px vertical outer margins;
- the 2384px-wide Card frame, header, title, subtitle, state badges, and shell padding;
- Card placement, collapsed/expanded geometry, and session state;
- the fixed right-edge square affordance, shown filled when collapsed and outlined when expanded.

`OblivionCardRenderer` gained two opt-in shell controls. `RenderBodyContent: false` leaves the expanded body as a foreign-widget host frame and omits the collapsed body completely. `ShowSquareExpansionAffordance: true` adds the bounded square state control. Existing callers retain their prior defaults.

## Avalonia content responsibilities

`Oblivion.Avalonia` realizes the framework-free `OblivionContentPresentationPlan` as a read-only Avalonia document. Avalonia owns font metrics, wrapping, 16px body type with 24px line height, heading metrics, 12px block rhythm, selectable text, inline-code shaping, list layout, measurement, and local overflow.

The wide Card does not force wide prose. The document column is centered and capped at 1040px inside the 2336px body viewport, with 16px content padding.

The semantic path remains:

```text
PresentationContent.Markdown
  -> PresentationMaterializer / Oblivion Card
  -> OblivionContentPresentationPlan
  -> Oblivion.Avalonia read-only document control
```

`Oblivion.Model`, `Oblivion.Persistence`, and `Oblivion.Presentation` have no Avalonia assembly references. A focused test guards this boundary.

## Collapsed contract

The collapsed Card is 174px high and contains only title, short subtitle, two small state/type badges, and the filled-square expansion control. No document host, Markdown preview, clipped body, body viewport, or card-local scroller is mounted. The Card remains the sole item in the ordinary vertical stack.

## Expanded contract

The expanded Card keeps the same 2384px width, header, metadata, location, and right-edge control. It grows to a 960px screen-relative bound and changes the inner mark to an outline. Beneath the header, the Machina shell reserves the body frame and the Avalonia read-only document fills it. The document, rather than implementation metadata, is the dominant visual surface.

## Screen utilization

Both proofs use a 2560 × 1440 client surface. The Card consumes 93.1% of the width while preserving 88px outer margins. The expanded Card uses 960px of vertical space, leaving normal page background below rather than stretching a bounded document into an empty full-height panel. The prose retains a readable 1040px maximum independently of Card width.

## Scroll contract

Collapsed state has no body and no local scroll. Expanded state uses one bounded Avalonia `ScrollViewer`; it remains visually idle for the M19g document because all content fits. If later content exceeds the 960px Card bound, that mature local document scroller owns overflow. M19g deliberately has no nested page/body scrolling path.

## Visual findings

Codex inspected both final 2560 × 1440 PNGs at original resolution. The initial proof exposed two defects: the square control resolved beside the title instead of against the Card's right edge, and the expanded Card reserved excessive unused height. The header now uses explicit right-edge geometry, dark-theme badges no longer flash a light development style, and the expanded bound is content-driven. A subsequent 820px bound clipped the final paragraph, so the final 960px bound was selected and re-reviewed.

A magnified follow-up review exposed clipped descenders in lowercase heading letters such as `p`, `y`, and `g`. The mature host had retained the 24px body line height after increasing heading font size to 28px. Headings now receive a line box at least eight pixels taller than their font, and an Avalonia-host regression test guards the 28px/36px top-level heading metrics.

The final review found:

- the application and Card use the screen without a narrow Presenter viewport;
- the collapsed body is absent and its filled square is unambiguous;
- the expanded outline square stays in the same location;
- conventional Avalonia wrapping, paragraph spacing, list indentation, and inline code are readable;
- the content column is restrained without narrowing the Card or application;
- there is no Presenter sidebar, inspector, palette, diagnostic panel, layout guide, or raw metadata surface;
- the final document is fully visible with consistent padding and no clipped last line.

## Remaining defects

No visible M19g acceptance defect remains. Link activation, editing, multiple Cards, page overflow, additional content kinds, and responsive multi-resolution qualification remain deliberate non-M19g scope rather than defects in this proof.

## Validation

The required JointTaskForce, Copeland, Oblivion, Machina regular/slow, Machina no-restore build, and Aurelian no-build lanes passed. The canonical Oblivion playback suite passed 14 of 14 scenarios with zero failures. `git diff --check` passed. Seven focused standalone tests cover one-Card materialization, stream-stack structure, collapsed body absence, mature expanded selection, expand/collapse session state, stable square geometry, full-width margins, and Avalonia-free semantic assemblies.

## Next step

M19h should add exactly one second semantic Card through the same stream solely to qualify page-level vertical scrolling, stack spacing, and restoration of the selected/expanded Card. It should retain this standalone host and exact Machina-shell/Avalonia-body ownership split, without reintroducing Presenter or broad layout features.
