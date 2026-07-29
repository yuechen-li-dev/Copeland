# CTS-MACHINA-LAYOUT-PROFILES-M1 review

## Status

**Success.** The Copeland TS landing page now renders exactly one of three
intentionally authored Machina layout roots. It is no longer a desktop page
whose sidebar, hero, grid, and navigation negotiate their composition through
CSS breakpoints.

## Profile law and recomposition

`src/LayoutProfiles.ts` is the only classifier:

```text
width < 600       Mobile
600 <= width < 1024 Tablet
width >= 1024     Desktop
```

`Main.ts` owns one immutable `SiteState` and a single mount-lifetime viewport
subscription supplied by the generic browser host. A resize dispatches a
profile event, React replaces the selected root, and the reducer retains the
active section and copy labels. Leaving mobile closes the mobile menu; no
desktop/tablet menu state can leak. Only the selected React root mounts, so
there are no hidden parallel trees, duplicate subscriptions, or duplicate
clipboard effects.

The concrete profile value is a named primitive discriminator rather than a
nominal enum because the current JavaScript backend has module-local nominal
enum validation. This is a bounded interop representation choice, not a second
state system; profile names and thresholds remain centralized.

## Root compositions

`machina/LayoutProfiles.machina.ts` contains all three source functions and
shares its native style records. The projection invokes each at its proof
viewport and writes namespaced class maps and one CSS asset:

```text
DesktopLayout (1440 x 900)
  PersistentSidebar | MainContent(WideHero, LanguageStrip, four cards, Footer)

TabletLayout (768 x 1024)
  TabletHeader, StackedHero, LanguageStrip, two rows of two cards, Footer

MobileLayout (390 x 1604 document)
  MobileHeader, LinearHero, CommandStack, CapabilityFlow, LanguageStrip,
  FeatureList, Footer
```

Generated React projection classes are namespaced (`m-frame-desktop-*`,
`m-frame-tablet-*`, `m-frame-mobile-*`). Machina style classes remain
content-addressed, so common immutable styles deduplicate without frame-class
collisions. React still realizes `header`, `nav`, `main`, `section`, `article`,
buttons, links, headings, and footer.

## Shared application pieces

`App.tsx` has one inventory of `Brand`, navigation items, command panel,
capability chips, hero text, proof card, language strip, feature card, and
footer. Layout wrappers only choose placement: `DesktopSidebar`,
`TabletHeader`, `MobileHeader`, `DesktopHero`, `TabletHero`, and `MobileHero`.
Copy text, feature copy, proof data, and navigation data are not forked into
three websites.

`SiteState` holds `activeSection`, both copy-feedback labels, `mobileMenuOpen`,
and the profile. Its named events cover copy success/failure, section selection,
mobile menu open/close, and profile changes. The browser host now provides a
single removable viewport subscription and a clipboard fallback for ordinary
localhost/browser test contexts.

## Ownership and adaptation boundary

MachinaLayout owns root geometry and attachment points. MachinaStyle records in
the native source derive layout surfaces from common canvas, header, hero,
strip, and card styles. CSS supplies shared tokens, focus/reduced-motion rules,
and local presentation details such as chip wrapping and touch sizing. It does
not decide which root composition is active. React owns semantics and events.
TSPack owns materialization, build, host lifecycle, browser scenarios,
diagnostics, screenshots, and shutdown.

## Browser proof

The generic `tspack scenario` command consumes a declared JSON scenario,
starts the named RunTarget, waits for readiness, runs Playwright viewports and
steps, captures console/page/request diagnostics, writes screenshots/report,
and stops the target even when a scenario fails.

`scenarios/layout-profiles-m1.json` proves desktop (1440x900), tablet
(768x1024), mobile (390x844), plus 599/600/1023/1024 classification boundaries.
It verifies visibility/absence of the correct headers, card count, no target
horizontal overflow, copy feedback, keyboard reachability, mobile-menu open and
close, reduced motion, and clean browser diagnostics. Generated, ignored proof
artifacts are under `samples/copeland-ts/copeland-website-m0/artifacts/cts-machina-layout-profiles-m1/`.

## Additional work performed

- Observed limitation: independently projected Machina roots used the same
  `m-frame-root` names. Generic fix: `LowerForReact` accepts a validated
  namespace. Layer: Machina projection. Test: deterministic non-collision test.
  Effect: all three layouts can share one generated CSS asset.
- Observed limitation: TSPack only had one-off inspection. Generic fix:
  lifecycle-owned JSON browser scenario command and Playwright adapter. Layer:
  TSPack. Effect: repeatable desktop/tablet/mobile proof and cleanup.
- Observed limitation: clipboard APIs are unavailable in some local browser
  contexts. Generic fix: fallback to a temporary textarea/`execCommand`.
  Layer: browser host. Effect: both shared command controls show copied state.

## Conscious non-goals and review questions

Deferred: arbitrary orientation matrices, a fourth ultrawide root, a general
responsive-property abstraction, a native Machina DOM renderer, a virtual DOM,
and a public deployment. The earlier native-renderer audit remains separate.

Recommended Claudefood review questions:

1. Does every composition-changing decision live at a root rather than a CSS
   breakpoint or shared component?
2. Are the generated attachment points meaningful enough for a future second
   page without lowerer changes?
3. Does the named primitive profile discriminator remain acceptable until the
   generic nominal-enum module contract is repaired?
4. Should `tspack scenario` be promoted from this bounded JSON API into the
   manifest language only after more than one consumer needs that declaration?
