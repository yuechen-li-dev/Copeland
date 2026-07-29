# CTS-COPLAND-WEBSITE-M0 review

## Status

**Meaningful progression, not honestly complete.** A real responsive Copeland
TS React site is materialized, built, hosted, and inspected through TSPack.
The desktop hero now uses Copeland's native Machina MIR as its primary geometry
and style input, but the whole page does not yet, so the corrected milestone is
not complete.

The prior native-renderer audit remains at
`docs/Copeland/reviews/cts-machina-site-m0.md`. It is deliberately preserved:
that document is evidence for a future native Machina browser renderer and is
not a prerequisite for this React-realized website experiment.

## Project and architecture

Project: `samples/copeland-ts/copeland-website-m0`.

```text
Copeland TS / TSX                 machina/Hero.machina.ts
  -> React component tree          -> native Machina source compiler
  -> semantic DOM                  -> resolved Machina MIR
  -> attach generated classes      -> React class/CSS projection
  -> browser CSS realization       -> TSPack materialization and RunTarget
```

`App.tsx` contains small React components for the desktop rail, mobile header,
navigation, hero, command cards, deterministic code texture, language strip,
feature cards, and footer. `Main.ts` owns a closed immutable reducer for the
mobile menu and copy confirmation labels. `Events.ts` exposes named primitive
dispatch values. This is deliberately a reducer, not a `flow`/`state` machine:
the interaction has no asynchronous intermediate state beyond clipboard
completion and does not benefit from a larger transition model.

`machina/Hero.machina.ts` is direct Copeland Machina source. It uses static
style records, `Anchor`, `VStack`, nested `HStack`, `Fixed`, and `Fill` to
author hero geometry at the desktop design viewport. The TSPack-owned
`generate-machina` RunTarget invokes the small projection tool, which compiles
the source through `MachinaSourceCompiler`, resolves it, and writes
`src/generated/MachinaHero.ts`, `machina-hero.generated.css`, and a diagnostic
geometry trace. React imports the generated named class accessors and attaches
them to semantic `h1`, `h2`, `p`, `div`, and `button` elements. No native DOM
renderer or second browser host was introduced.

React provides `header`, `nav`, `main`, `section`, heading, button, link,
article, code, and footer realization. The copy operation is a generic typed
browser-host effect with success and failure callbacks. No external state
library was added.

## Visual and accessibility behavior

The page uses local CSS variables for the near-black canvas, panels, text,
magenta/purple/cyan accents, borders, radii, shadows, typography, and spacing.
Native Machina owns the desktop hero title, copy, and command-panel geometry;
small responsive CSS overrides deliberately switch those fixed resolved frames
to a vertical mobile flow because the native profile has no responsive variant
or wrapping primitive yet. The site provides a desktop sidebar, mobile
header/menu, responsive feature grid, wrapped chips, focus styling,
reduced-motion overrides, readable command buttons, meaningful metadata, and
an honest proof card. The deterministic code texture is a CSS background SVG
rather than DOM text, so it has no accessibility tree entry.

The proof card says only `compiler tests` and `Copeland TS / TSPack
materialized`; it makes no fabricated test-count, customer, adoption, or
availability claim. The small `server.mjs` serves TSPack-built browser assets
with JavaScript MIME types and is started and stopped solely as the declared
TSPack `site` RunTarget.

## TSPack proof and general fixes

Focused validation used the current sibling TSPack source:

```text
tspack update -> sync -> build -> run --once
tspack inspect --run site --viewport 1440x900 --selector h1 --json
```

The inspect proof returned a visible `h1`, correct title, no diagnostics, and
showed TSPack stopping the RunTarget. A fresh browser session also mounted the
app without console errors or desktop horizontal overflow.

Additional work performed:

- `MachinaBrowserLowerer.LowerForReact` is a generic backend-neutral React
  projection: it reuses native resolved frame/style CSS and returns stable
  class names by MIR identity, while React retains semantic element choice.
  Focused Machina tests verify deterministic output and that the projection
  still uses neither flex nor grid as a hidden layout resolver.
- Native source stack construction now carries `main` and `cross` tracks, so a
  nested `HStack`/`VStack` can participate in an outer stack without an
  irrelevant absolute frame. A direct Copeland source test proves the layout.
- TSPack browser materialization now copies safe local assets linked from the
  authored `index.html`, rather than only a hard-coded `styles.css`. This is
  needed for generated CSS and is covered by a focused TSPack test.

- TSPack now exposes `copyText` in its generated `@copeland/browser-v1` host;
  this belongs in the generic browser-host contract, rather than a sample copy.
  `TestMaterializeBrowserGraph` asserts that contract.
- TSPack now accepts an absolute workspace root while resolving a local path
  package. This fixes a real `tspack update --root <absolute-path>` failure.
  The focused resolver test passes.
- Copeland's bounded React intrinsic vocabulary now accepts the semantic tags
  needed by ordinary React websites, and its browser contract recognizes the
  typed clipboard effect.
- A compiler diagnostic now reports an unbound imported source module instead
  of dereferencing null.

## Remaining blockers

Copeland TS already has a native bounded Machina source profile: ordinary
Copeland functions and TS-XML can construct `Root`, `VStack`, `HStack`,
`Anchor`, `Absolute`, `Fixed`, `Fill`, static style records, `Text`, `Button`,
and `Toggle`. `samples/copeland-ts/machina-m1/Settings.ts` is the direct
working example, with compiler coverage in `MachinaSourceM1Tests`.

The native profile now has the narrow React projection needed for the website
hero, but it remains a static-viewport M1 model. It does not yet have responsive
variants, wrapping, an ordinary semantic-host vocabulary, dynamic properties,
or automatic participation in the normal `tscl` project graph. Consequently,
the sidebar, chips, feature grid, and mobile flow remain CSS-driven. The
projection target is deliberately invoked through an explicit TSPack RunTarget
rather than concealed as an ad hoc post-build script.

Two additional Copeland backend limitations surfaced while making the real
page mount:

- nominal records/unions passed across compiled JavaScript modules validate
  against module-local symbols;
- top-level `const` declarations can be omitted while their references remain.

The page avoids both only for primitive browser interaction values. The correct
follow-up is a compiler regression test and generic backend fix, not a more
elaborate page-local workaround.

## Recommended follow-up

Reuse the existing native Copeland Machina MIR rather than importing
MachinaLayout.JS. Add the narrow React/TSPack projection needed to consume
`HStack`, `VStack`, anchor/absolute frames, and static style records as React
semantic-element layout/style values. Then replace CSS layout rules
incrementally, starting with shell/sidebar, hero, chip wrap, and feature grid.
Add a declared TSPack Playwright scenario API for multi-viewport interaction
assertions and saved screenshots before promoting this proof to the product
website.
