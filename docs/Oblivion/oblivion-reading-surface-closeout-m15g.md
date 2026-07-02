# Oblivion Reading Surface Closeout M15g

## Purpose

M15g closes out the M15 Oblivion reading-surface arc as a documentation, backlog, and planning milestone only.

M15g does not change runtime behavior, scroll behavior, rendering behavior, or input routing behavior.

The goal is to record the current baseline clearly enough that future work can move deliberately instead of continuing accidental scroll churn.

## M15 arc summary

M15 returned primary focus from the Aurelian migration arc to Machina and Oblivion workbench usability.

The M15 arc progressed in a narrow sequence:

- M15a audited the current workbench and isolated the real usability blockers.
- M15b made the runtime presenter resizable on a controlled `16:9` surface and made card previews readable.
- M15c made the main card stack the primary Markdown reading surface by adding inline expansion with local body scroll.
- M15d hardened expanded Markdown readability, moved the inspector to raw Markdown source, and made expanded cards document-scale.
- M15e separated main-stack and inspector scrolling into independent panes with explicit nested scroll routing and partial document viewport rendering.
- M15f stabilized the M15e regressions, restored main-stack scrolling, and removed repeated raw-source layout work from inspector scroll ticks.

The M15 reading-surface arc is now golden-pathed enough to pause.

## Current golden path

Required closeout doctrine:

```text
The M15 reading-surface arc is now golden-pathed enough to pause.

The current workbench reading loop is:

  resize presenter
  browse collapsed cards
  expand one Markdown document
  read rendered Markdown inline in the stack
  scroll the expanded body locally
  inspect metadata/actions/diagnostics/raw source in the inspector
  scroll inspector independently
  collapse or select another card

Selection couples main stack and inspector content.
Scrolling does not.
```

Current baseline:

```text
Runtime surface:
  resizable 16:9 presenter surface
  default 1280x720
  minimum 960x540
  letterboxed inside arbitrary OS window

Card stack:
  collapsed cards are scannable
  one Markdown card can expand per page
  expanded card is document-scale
  rendered Markdown is inline in stack

Inspector:
  independent pane
  metadata/actions/diagnostics/artifacts
  raw Markdown source
  independent scroll

Scroll/input:
  main card stack scroll
  expanded body scroll
  inspector pane scroll
  raw source scroll
  direct scrollbar drag/capture
  deepest-region wheel routing

Document viewport:
  real clipping
  partial block/line rendering
  no all-or-nothing paragraph disappearance
```

## Current presenter sizing model

The runtime presenter now uses a controlled `16:9` surface instead of startup-only fixed sizing.

- default runtime surface: `1280x720`
- minimum runtime surface: `960x540`
- arbitrary host windows are letterboxed around the effective presenter surface
- layout recomputes from the live effective surface instead of startup dimensions only

This keeps resize behavior deliberate without introducing an arbitrary freeform `2D` layout solver.

## Current card preview model

Collapsed cards are now intended to be scanned, not deciphered.

- preview text uses bounded wrap-or-elide behavior
- known dark-on-dark preview states were removed in M15b
- collapsed cards stay compact enough for browsing volume
- preview readability is no longer delegated to the inspector alone

## Current expandable card model

The stack itself is now the primary reading surface for Markdown documents.

- one Markdown card can expand per page
- selection and expansion are related but distinct state
- collapsed cards remain scannable while the expanded card becomes the active document surface
- collapsing or expanding another card returns the stack to the browse loop without changing the overall shell model

## Current Markdown reading model

Expanded Markdown now renders inline in the card stack with a document-oriented presentation.

- expanded Markdown uses a shared readable style record
- expanded cards are document-scale rather than tiny preview cells
- the expanded body scrolls locally
- partial block and paragraph rendering works when content intersects the viewport
- the inspector no longer duplicates formatted Markdown body rendering

## Current inspector model

The inspector remains the secondary pane for card-specific context, not the primary reading surface.

- metadata, actions, diagnostics, and artifacts remain visible there
- the inspector shows raw Markdown source rather than formatted Markdown body
- inspector content follows the selected card
- inspector scroll is independent from main-stack scroll
- raw source owns its own local scroll region inside the inspector

## Current scroll/input model

Wide Oblivion now uses explicit presenter-local scroll regions instead of one shared long page surface.

- main card stack scroll
- expanded Markdown body scroll
- inspector pane scroll
- raw Markdown source scroll

Wheel routing goes to the deepest scrollable region under the pointer.

Direct scrollbar drag/capture is supported for the main stack, expanded body, inspector pane, and raw source region.

Selection couples main stack and inspector content.
Scrolling does not.

## Current document viewport model

The current document viewport behavior is real clipping plus partial rendering, not all-or-nothing block visibility.

- partially visible Markdown blocks remain renderable
- partially visible lines remain renderable
- clip-to-bounds is enforced for expanded body and raw source regions
- wide Oblivion no longer depends on rendering entire documents just because one region is visible

## Known limitations

Known limitation carried forward from M15f:

```text
Inspector scroll is not composition-only yet.

M15f fixed the visible lag by caching prepared raw-source layout.

A future performance pass may separate inspector scroll from page rerender/composition if profiling proves it worthwhile.
```

M15g does not attempt to fix this.

## Remaining UX papercuts

Near-term reading and navigation papercuts:

- clearer selected versus expanded visual affordances
- pane focus indication: which scroll region owns wheel/keyboard focus
- collapse/hide inspector for focused reading mode
- stronger document title/source breadcrumb when expanded
- keyboard navigation between cards
- keyboard collapse/expand polish
- remember last expanded doc per page/workspace

Search and filtering papercuts:

- quick filter by title/source/tags
- doc search within current expanded Markdown
- workspace-wide doc/card search later

Inspector ergonomics papercuts:

- inspector collapse/resize/hide
- better grouping of metadata/actions/diagnostics/artifacts
- raw source copy button later
- diagnostics jump-to-source later

Card organization papercuts:

- card sections/groups
- pin/favorite/recent cards
- sort/filter controls
- workspace breadcrumbs

Performance and density papercuts:

- inspector composition-only scroll path
- raw source layout cache diagnostics
- large Markdown document stress tests
- scroll event coalescing only if profiling proves event flood
- TOML loading for reading style record
- density/font scale controls later
- stronger theme audit

## Recommended next milestone

Primary recommendation:

```text
M16a — Oblivion reading navigation and focus affordances
```

Recommended scope:

- no editor or execution work
- improve selected versus expanded state visibility
- show the active scroll/focus region more clearly
- add or polish keyboard navigation for cards
- add hide/collapse inspector or focused reading mode if that path is preferred
- improve the document title/source breadcrumb when expanded

Alternative note:

```text
M16a could instead be a search/filtering milestone if browsing volume becomes the bigger pain.
```

M15g recommends navigation and focus affordances as the primary next step because the reading surface now exists, but its intent and focus ownership are still more expensive than they should be.

## What changed

What changed across M15a through M15f:

- presenter sizing moved from startup-fixed to controlled live `16:9` resizing
- collapsed card previews became readable enough to browse
- one Markdown card can expand inline per page
- expanded Markdown became the primary reading surface in the stack
- the reading style was hardened for readable document-scale Markdown
- the inspector moved to raw Markdown source instead of duplicating rendered body content
- main stack and inspector now scroll independently
- nested body/source regions gained local scroll plus direct thumb dragging
- viewport rendering now supports partial block/line visibility
- main-stack regression routing was fixed
- raw-source layout work is cached across repeated inspector scroll ticks

## What did not change

M15g is closeout/planning only.

- no feature behavior changed
- no new UI behavior
- no new card behavior
- no new scroll behavior
- no new inspector behavior
- no Markdown editing
- no notebook execution
- no Roslyn/xUnit execution
- no Aurelian work
- no `VD-MIR` work
- no renderer architecture changes
- no font pipeline rewrite
- no arbitrary `2D` layout solver

## Deferred work

Required closeout stance:

```text
M15g does not mean Oblivion is finished.

It means the reading surface has a documented baseline, and future work should move deliberately toward navigation, editing, search, or execution rather than continuing accidental scroll churn.
```

Deferred work remains intentionally separate from M15g:

- reading/navigation affordances
- search and filtering
- inspector ergonomics improvements
- card organization improvements
- Markdown editing
- card creation/editing
- raw source editing
- notebook execution
- Roslyn/xUnit execution
- action/effect execution
- composition-only inspector scroll if profiling proves it worthwhile
