# CTS-MACHINA-SITE-M0 architecture review

## Status

**Honest stop — the current Machina browser realization cannot truthfully own
the requested responsive landing page.**

This review is intentionally recorded before adding a page fixture. A static
HTML/CSS page, a React implementation, or a second browser host would make the
reference image look plausible while bypassing the architecture that this
milestone is intended to dogfood.

## What is available today

The repository has a bounded, working Copeland-to-Machina browser proof at
`samples/copeland-ts/machina-m1`:

```text
Copeland TS static view source
  -> MachinaSourceCompiler
  -> resolved fixed viewport boxes
  -> semantic HTML plus generated absolute-position CSS
```

That proof has `Root`, `Container`, vertical/horizontal stacks, text, buttons,
and toggles. It resolves a document against one fixed viewport before browser
lowering. The generated page runtime owns only `Save` and `ToggleDarkMode`
demonstration transitions.

TSPack also has an independent, real project lifecycle implementation at the
sibling `tspack` checkout. It supports materialization, declared run targets,
readiness, process-tree shutdown, and Playwright-backed structural inspection.
It does not currently provide a TSPack-owned Playwright *test scenario* or
screenshot runner that a Copeland website project can declare and reuse.

## Concrete gaps exposed by the requested page

The landing page needs browser-flow composition instead of pre-resolved boxes:

- semantic landmarks and links (`nav`, `main`, `section`, headings, anchors);
- responsive row/column wrapping, breakpoint adaptation, and scrolling;
- browser CSS features for gradients, layered decorative texture, clipping,
  hover/focus states, and reduced-motion media queries;
- named accessible controls and an explicit copy effect with success/failure
  state;
- a reducer that is authored by the page, rather than the current hard-coded
  `Save` / `ToggleDarkMode` sample runtime;
- a TSPack-owned Playwright scenario API with screenshots, console/request
  capture, clipboard handling, and lifecycle cleanup proof.

The current implementation deliberately rules out the first two categories:
`MachinaLayoutResolver` resolves all frames before the browser is involved, and
`MachinaBrowserLowerer` emits fixed `position: absolute` geometry. Its source
compiler only accepts static, parameterless entries and the small fixed view
vocabulary. The emitted page builder also embeds a sample-specific reducer.

## Why a page-local workaround is not acceptable

The following tempting shortcuts were deliberately not taken:

- A hand-authored HTML/CSS page would bypass Machina presentation.
- Extending the existing `browser-m0` host to mutate raw DOM nodes would create
  a second browser UI boundary instead of a Machina realization.
- The existing standalone web sample is React-based; duplicating it would
  violate the stated no-parallel-React requirement.
- `performative-ui` is also React-based, so it is useful visual inspiration but
  not compatible with the ownership law for this milestone.

## Recommended prerequisite: Machina browser flow M2

Build this as a generic Machina capability first, with its own focused tests:

1. Add backend-neutral semantic elements with a small, typed set of landmark,
   link, button, heading, and decorative-node properties.
2. Add browser-owned flow layout intents (row, column, wrap, gap, min/max
   sizes, and breakpoint variants). Keep the static resolved-box M1 path
   intact; do not translate browser flow into fake absolute frames.
3. Extend the Machina browser projection for typed style tokens, media queries,
   focus/hover/pressed state, `prefers-reduced-motion`, and ARIA/name data.
4. Replace the sample reducer in `MachinaBrowserPageBuilder` with a generic,
   Copeland-authored state/effect contract. Clipboard copy must be an explicit
   effect with success and failure events.
5. Add an explicit TSPack browser-scenario surface that starts a declared run
   target, launches Playwright, captures diagnostics/screenshots, and always
   stops the run-target process tree.

Only after those capabilities exist should the landing page be introduced at
`samples/copeland-ts/machina-site-m0` (or promoted to a product website
project). Its component tree can then be real Machina source rather than an
exception-shaped fixture.

## Proposed website shape after the prerequisite

```text
CopelandLandingPage
|- PageShell
|  |- Sidebar
|  |- MainContent
|     |- Hero
|     |  |- CommandPanel
|     |  |- CapabilityChips
|     |  `- ProofCard
|     |- LanguageMarquee
|     `- FeatureGrid
`- Page reducer/effects
   |- copy primary command
   |- copy secondary command
   |- navigation state
   `- mobile navigation state
```

The page should use typed local design tokens for the dark surface, magenta,
purple, cyan, blue, status color, spacing, radii, and font stacks. Decorative
ASCII must be seeded at authoring time and projected as accessibility-hidden.

## Validation performed for this review

- `.NET SDK 10.0.302` is installed and available on `PATH`.
- `samples/copeland-ts/machina-m1` was inspected as the only current Machina
  browser source path.
- `MachinaLayoutResolver`, `MachinaBrowserLowerer`, and
  `MachinaBrowserPageBuilder` were inspected. They confirm fixed, absolute
  browser output and a sample-specific state runtime.
- The sibling TSPack checkout was inspected for declared run targets and the
  experimental Playwright-backed `inspect` capability.
- The external `vorpus/performativeUI` README was inspected. It describes the
  project as React components, which is incompatible with this milestone's
  no-React page constraint.

No product website, second browser host, hand-authored HTML generator, React
implementation, package materialization, or test harness has been added.
