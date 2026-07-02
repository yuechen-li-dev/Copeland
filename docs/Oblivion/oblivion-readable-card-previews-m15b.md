# Oblivion Readable Card Previews M15b

## Purpose

M15b makes compact Oblivion cards readable enough to serve as a real browsing surface instead of a blind-selection surface.

## Previous failure mode

Before M15b:

- plain previews were clipped as tiny single-line fragments
- Markdown previews mixed wrapped and non-wrapped rendering paths
- body text often collapsed into unreadable bars
- a Markdown summary path could render dark text on a dark preview frame

## Preview readability doctrine

```text
A workbench surface is not valid until its primary content is readable.

Speed does not compensate for unreadable content.

Cards must be useful before selection, not only after inspection.

Text must wrap, elide, or intentionally clip.

Text must never become accidental bars.
```

## Word wrap / elision policy

M15b uses one bounded preview rule:

```text
Card preview body:
  wraps within card body region up to a small fixed number of lines,
  then intentionally clips/elides.

Card title:
  stays single-line and bounded by the existing card shell.

Metadata chips:
  remain bounded and do not take over the body preview contract.

Inspector:
  stays denser than the preview surface, but does not regress.
```

## Contrast policy

- dark preview frame uses explicit light foregrounds
- light card surfaces continue using dark foregrounds
- Markdown preview text no longer inherits an accidental dark default on a dark frame
- selected state remains readable and border-led instead of relying on low-contrast text accents

## Card body preview behavior

- plain preview bodies now wrap or intentionally elide inside the card body
- Markdown preview entries use the same bounded preview discipline instead of mixing wrapped summaries with non-wrapped rows unpredictably
- preview text stays inside the body region rather than painting outside the intended card frame

## Inspector relationship

The inspector remains the richer reading surface, but compact previews are now useful before selection instead of only after selection.

M15b improves compact preview readability without turning the inspector into a separate editor or document-viewer milestone.

## Export evidence

M15b proof exports live under `artifacts/m15b/`:

- `m15b-oblivion-cards-960x540.png`
- `m15b-oblivion-cards-1280x720.png`
- `m15b-oblivion-docs-1280x720.png`
- `m15b-oblivion-docs-1600x900.png`
- `m15b-oblivion-docs-compact-960x540.png`
- `m15b-oblivion-inspector-1280x720.png`

## What changed

- compact preview body text now uses wrap-or-elide behavior
- preview colors explicitly respect dark-frame readability
- Markdown preview rows now use a more consistent bounded-preview policy
- text no longer degrades into accidental dark bars in the known failure path

## What did not change

- no Markdown editor
- no full document renderer inside every compact card
- no broad theme rewrite
- no execution/runtime feature work
- no Aurelian or VD-MIR work

## Deferred work

- deeper preview semantics or ranking
- density controls after the default preview contract proves stable
- inspector-local scrolling if later needed
- any future editor/runtime behavior

## Follow-through

M15c keeps the readable collapsed preview discipline from M15b, but changes the reading contract:

- collapsed card remains the scannable summary surface
- expanded card becomes the inline document-reading surface
- inspector remains metadata/actions/diagnostics/artifacts, not the only body-reading surface

M15c still does not add Markdown editing or notebook execution.
