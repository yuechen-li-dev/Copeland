# CTS-WEBSITE-TABLE-LAYOUT-M0

> Components execute. Layouts describe spatial relations.

> The website is authored through streams and normalized as tables.

The Copeland website now has three stream roots in `samples/copeland-ts/copeland-website-m0/src/App.tsx`: Desktop (`1440×900`), Tablet (`768×1024`), and Mobile (`390×1620`). The reducer selects explicit roots at `<600`, `600–1023`, and `>=1024`; it does not assemble page geometry.

## Migration inventory

| Previous structure | Classification | M0 disposition |
| --- | --- | --- |
| Profile-specific React JSX trees | Page layout | Replaced by streams. |
| `Machina*Root_0_*Class` imports | Generated/legacy structure | Removed. |
| HStack/VStack Machina source, C# generator, generated CSS | Legacy layout | Removed. |
| Brand, navigation, hero, cards, command control, footer | Content/interaction | Ordinary bound components. |
| Tokens, surfaces, local flex/grid | Visual/component-local style | Retained as CSS. |
| Headings, body, code, command labels | Intrinsic text dependency | Browser-shaped inside known hosts. |

Desktop is lateral command-bar/page topology; Tablet has a compact header and two-column collection; Mobile is one-column reading flow. Stable semantic hosts include `commandBar`, `hero`, `heroCopy`, `heroAccent`, `codeBadge`, `languageExample`, `featureGrid`, `architecture`, `callToAction`, and `footer`. `featureGrid` binds four bounded items, not fake positional slots.

## Derivations and tables

The hero uses `centerXIn(heroCopy)`, `expandFrom(heroCopy, ...)`, desktop `placeRightOf(heroCopy, 32px)`, and mobile `placeBelow(heroCopy, 8px)`. These are immutable projected `layout::Derivations` rows; the browser receives resolved rectangles, with no formula evaluator. The expected normalized relations are `layout::Layouts`, `layout::Boxes`, `layout::Bindings`, `layout::CollectionItems`, `layout::Derivations`, and `layout::Sources`.

The migration found and fixed a compiler defect: stream `row`/`column` lowering had swapped physical width and height tracks. `StreamCompositionM0Tests` now asserts emitted real rectangles. The browser proof also exposed a CSS selector that matched every mobile host rather than only the mobile root; it is now constrained to the semantic root.

## Browser evidence

TSPack scenario evidence verifies all three roots, breakpoints, hosts, bounded cards, focus/command usability, reduced motion, horizontal overflow, and clean console/page/request diagnostics. `browser-proof.mjs` reads numeric DOM rectangles: root extent, desktop command-bar width, footer containment, feature containment, centering, halo expansion, desktop adjacency, mobile adjacency, and overflow. It shuts down browser and host in `finally`.

Generated, ignored evidence:

- `samples/copeland-ts/copeland-website-m0/artifacts/cts-website-table-layout-m0/desktop-1440x900.png`
- `samples/copeland-ts/copeland-website-m0/artifacts/cts-website-table-layout-m0/tablet-768x1024.png`
- `samples/copeland-ts/copeland-website-m0/artifacts/cts-website-table-layout-m0/mobile-390x844.png`
- `samples/copeland-ts/copeland-website-m0/artifacts/cts-website-table-layout-m0/rectangle-report.json`

## Text and style findings

> Text content lives inside compiler-known boxes, but glyph shaping and intrinsic measurement remain host concerns.

The fixture covers a responsive heading, bounded prose, monospace code, and button labels. The compiler knows assigned outer boxes and policies, not final glyph advances, font load/fallback, wrapping, zoom, accessibility scaling, or unbroken-token metrics. No DOM measurement or text shaper was added; future real `fit` text may need an explicit host intrinsic-size channel.

Tokens (color, typography family, spacing, radii, borders) are clean immutable records/custom properties. Region backgrounds, clipping and borders are clean semantic-host CSS. Button/card/code/focus treatment remains component-local. Generated positioning is backend CSS. Font and wrapping behavior is host-metric entanglement.

No recurring style relationship required stable row identity, so **style tables are not recommended for M0**. This refactor is evidence gathering for style semantics, not permission to invent a CSS replacement from vibes.

## Conclusion

The website now demonstrates canonical streams, explicit profiles, neutral hosts, bounded collections, derivations, visual coherence, and numeric browser proof. Manifest-aware `tscl table` inspection for React/TSPack projects remains the next tooling increment; current source-only inspection lacks the manifest's React/browser contracts. The next semantic milestone should close that projection-tooling boundary before style-table authoring.
