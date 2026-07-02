# Machina/Oblivion UX Backlog M15g

## Purpose

This document records the remaining usability backlog after the M15 reading-surface closeout.

M15g is closeout and planning only. It preserves the current runtime behavior and turns the remaining work into an explicit backlog instead of more accidental churn.

## Backlog categories

The backlog is grouped by the next user-facing pain points rather than by implementation subsystem.

The current reading baseline is good enough to pause. The next work should be chosen deliberately from navigation, focus affordances, search/filtering, editing, or execution.

## Reading/navigation

Priority items:

- clearer selected versus expanded visual affordances
- stronger expanded-document title/source breadcrumb
- remember last expanded doc per page/workspace
- focused reading mode that can collapse or hide the inspector
- smoother collapse/expand intent when moving between cards

Rationale:

The reading loop now works, but intent is still more implicit than it should be when many cards are visible.

## Inspector ergonomics

Priority items:

- inspector collapse/resize/hide
- better grouping of metadata/actions/diagnostics/artifacts
- raw source copy button later
- diagnostics jump-to-source later

Rationale:

The inspector is now useful and independent, but it still feels more like a packed utility column than a deliberate tool surface.

## Search and filtering

Priority items:

- quick filter by title/source/tags
- doc search within current expanded Markdown
- workspace-wide doc/card search later

Rationale:

Once browsing volume increases, discoverability pain will likely outgrow raw scrolling pain.

## Card organization

Priority items:

- card sections/groups
- pin/favorite/recent cards
- sort/filter controls
- workspace breadcrumbs

Rationale:

The current stack is readable enough for linear browsing, but not yet structured enough for larger workspaces.

## Keyboard workflow

Priority items:

- keyboard navigation between cards
- keyboard collapse/expand polish
- clearer indication of which pane owns wheel/keyboard focus
- explicit focus movement between stack, expanded body, inspector, and raw source

Rationale:

The shell already has keyboard plumbing. The missing part is workbench-level focus clarity and deliberate card navigation behavior.

## Editing

Explicitly deferred items:

- Markdown editing explicitly deferred
- card creation/editing deferred
- raw source editor deferred

Rationale:

Editing is important, but M15g closes a reading baseline first. Editing should not be smuggled in as incidental scroll or inspector work.

## Execution

Explicitly deferred items:

- notebook execution deferred
- Roslyn/xUnit deferred
- action/effect execution remains gated/deferred

Rationale:

Execution remains a separate milestone family. The current closeout is about a readable and navigable document workbench baseline.

## Performance

Priority items:

- inspector composition-only scroll path
- raw source layout cache diagnostics
- large Markdown document stress tests
- scroll event coalescing only if profiling proves event flood

Rationale:

M15f fixed the visible inspector lag safely, but performance work should stay evidence-driven rather than speculative.

## Styling and density

Priority items:

- TOML loading for reading style record
- density/font scale controls later
- stronger theme audit

Rationale:

The baseline is readable now, but not yet intentionally configurable or audited across density preferences.

## Recommended priority order

Primary recommendation:

1. `M16a — Oblivion reading navigation and focus affordances`
2. search/filtering
3. inspector ergonomics
4. card organization
5. editing
6. execution

Why this order:

The current baseline already supports reading, but the next friction is understanding state, focus, and movement across many cards and panes.

Search/filtering may move ahead of navigation work if browsing volume becomes the dominant pain.

## Non-goals for immediate next step

The immediate next step should not include:

- Markdown editing
- notebook execution
- Roslyn/xUnit execution
- Aurelian work
- `VD-MIR` work
- renderer architecture changes
- arbitrary `2D` layout solving
- feature creep disguised as “just one more scroll fix”
