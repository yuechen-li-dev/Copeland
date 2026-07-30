# CTS-LAYOUT-Z-M0 review

CTS-LAYOUT-Z-M0 gives every normalized layout box a deterministic, explainable
paint key. The implementation uses `layers Name { ... }` declarations, a
root-level `layers: Name;` selection, and node `layer:` / `z:` properties.

The default layer set is `DefaultLayers { default; }`; local z defaults to
zero. z is static, integral, and limited to `-5..5`. Ordering is always
`(layer rank, local z, authored node order)`, with later authored nodes above
earlier ones on a complete tie.

Layer sets are ordinary compiler symbols and work through project exports and
aliases. The normalized graph carries layer identity/rank, local z, authored
ordinal, and `NormalizedPaintOrder`. Composition retains its source layer
space; nested descendants inherit a containing layer when they omit one, and
cannot escape it. Fixed collection items inherit the collection region's
paint properties and retain source item order.

React projection uses an isolated root and generated bounded z values derived
from declaration rank plus local z. DOM child order remains authored order.
No component owns the ordering policy and no arbitrary user z-index is
accepted. Unsupported M0 features are portals, runtime z mutation, dynamic
layers, item-specific collection z, and cross-root layer escape.

Focused compiler tests cover default values, same-z source-order ties, layer
precedence over z, invalid z forms, deterministic repeated CSS, inheritance,
and imported aliases.

## Browser closure

`samples/copeland-ts/machina-layout-z-m0/browser-proof/` is the focused
TSPack-owned browser fixture. `src/Main.tsx` declares `AppLayers` in this
order: `background`, `content`, `overlay`, `modal`. Its overlapping stream
boxes normalize as follows (the root host is not shown):

| Box | Layer | Rank | Local z | Authored order | Expected paint position |
|---|---:|---:|---:|---:|---:|
| `backgroundBox` | `background` | 0 | 5 | 1 | 1 |
| `contentBox` | `content` | 1 | 0 | 2 | 2 |
| `lowOverlay` | `overlay` | 2 | -1 | 3 | 3 |
| `earlyOverlay` | `overlay` | 2 | 0 | 4 | 4 |
| `lateOverlay` | `overlay` | 2 | 0 | 5 | 5 |
| `highOverlay` | `overlay` | 2 | 5 | 6 | 6 |
| `modalBox` | `modal` | 3 | -5 | 7 | 7 |

`DefaultProof` omits both properties. Its two boxes therefore use
`DefaultLayers.default`, local z `0`, and authored orders one and two.

Generated hosts carry deterministic inspection attributes:

```html
<div data-machina-layout="LayerProof" data-machina-box="modalBox">
```

They derive only from the authored root and box names, not generated tree
coordinates, random IDs, geometry, or component details. Non-identifier React
property names are emitted as quoted JavaScript object keys so these attributes
reach the browser.

TSPack adds the reusable scenario assertion:

```json
{ "kind": "topmost-at-point", "x": 300, "y": 250,
  "expected": "[data-machina-box='modalBox']" }
```

It validates integer in-viewport coordinates and exactly one expected element,
uses `document.elementFromPoint`, and reports the actual semantic host. The
fixture assertions passed:

| Point | Expected and actual topmost box | Law proved |
|---|---|---|
| `(100, 100)` | `contentBox` | `content` outranks `background` despite z 5 |
| `(200, 180)` | `highOverlay` | local z orders boxes in `overlay` |
| `(200, 340)` | `lateOverlay` | later authored equal-z sibling paints above earlier sibling |
| `(300, 250)` | `modalBox` | `modal` outranks `overlay` despite z -5 |
| `(40, 540)` | `defaultLate` | implicit default layer/z and authored tie law |

The generated root establishes `isolation: isolate`; child z values are
compiler-generated from the bounded rank/z plan. The browser launcher attaches
exactly one generated stylesheet link before rendering, instead of attempting
an invalid native ESM CSS import. The fixture contains no transform, fractional
opacity, filter, containment, `will-change`, or blend-mode stacking context.

`artifacts/cts-layout-z-m0/layer-proof.png` is a generated, ignored proof
artifact (SHA-256
`3396A87E5FE3F7547E7DA441210A17D80FFB02BF1CBB2CD5FF4FC00DC9CA2C0C`).
`scenario-report.json` records assertion results and empty console/page/request
diagnostics. TSPack started the host, waited for readiness, launched and closed
Playwright, captured the screenshot, and stopped the host on each run.

Two identical TSPack builds produced byte-identical browser launcher,
generated source, and layout CSS:

| Artifact | SHA-256 |
|---|---|
| `main.js` | `680A3E5EC7F29BE20CA6EE36991CD386B9159A4C83C0A1C9E25E33A74DDD4A6D` |
| `src/Main.js` | `578BFB2453232467E036272BB342F3284D379ACAAA6122952C0BF19A62BD63CC` |
| `generated/layouts.css` | `A844014FC9980E5949B8CA9C9BDC6050A69CABE9AE34B9EFE3B4DF676E68E41F` |

The screenshot was also byte-identical across two successful runs. TSPack unit
coverage includes success, missing expected element, invalid point, mismatch
reporting, and malformed coordinates; command coverage proves
executable-relative runner lookup. Portals, cross-root layer escape, dynamic
layers, and runtime z changes remain deferred.
