# CTS-WEB-CONTENT-FIT-M0

The website now uses a fixed profile root with one explicit `page` `scrollY`
surface. Its oversized content lives in an overlay child so scrolling exposes
content without changing compiler-known root or sibling geometry.

The hero title is an explicit text slot with bounded scale-down policy; its
summary and action controls are separate stable boxes. The browser fitter only
measures the title target, uses actual loaded-font metrics, respects the
authored minimum, and records `data-machina-text-fit` plus selected size.
Code badges use `overflow: auto` and retain long tokens for local scrolling.
CTS-TEXT-DOCUMENT-M0 now realizes the title, card, pipeline, code, and footer
copy as local immutable document trees; the fit contract remains unchanged.

Inspection projects box overflow facts and `text::Regions`. This is
layout-adjacent content policy, not an implicit typography or intrinsic-sizing
system. Compiler glyph shaping and content-driven ancestor reflow remain out
of scope.

## Closure evidence

Overflow syntax is `overflow: visible|clip|auto|scroll|scrollX|scrollY`.
`layout::Boxes` projects `overflowPolicy`, `overflowX`, and `overflowY`.
Text slots use `fontSize`, `minFontSize`, `lines`, `wrap`, `textFit`, and
`textFallback`; `text::Regions` projects identity, owning box, preferred and
minimum px sizes, maximum lines, wrap/fit/fallback modes, and source.

The host starts at the preferred px size and descends in one-pixel bounded
steps to the authored minimum. It measures real DOM text, reruns after font
readiness and local `ResizeObserver` changes, and reports `fit`,
`minimum-overflow`, or `fallback`. The minimum-fallback mutation uses the
authored ellipsis fallback. Runtime-selected size is intentionally absent from
compiler tables and static editor hover.

The browser proof covers short, long breakable, long unbroken, and
minimum-fallback mutations at Desktop, Tablet, and Mobile. It asserts unchanged
title/action/feature box geometry, no horizontal page overflow, stable page
scrolling, footer reachability, and explicit code-pane scroll extent. Evidence
and screenshots are in `samples/copeland-ts/copeland-website-m0/artifacts/
cts-web-content-fit-m0/`.
