# Diagram theme dogfood — M19p

## Proofs

Both captures use the M19o VehicleFlow vault, Page, Markdown / Diagram / Markdown Card order, all-expanded state, `250` page offset, and 2560×1440 viewport:

- light: `artifacts/m19p/diagram-card-light.png`;
- dark: `artifacts/m19p/diagram-card-dark.png`.

Both runs reported page extent `2472`, viewport `1421`, and offset `250`, confirming appearance did not change host geometry or diagram sizing.

## Visual review

The dark proof no longer contains a white canvas island. Its `#0f172a` Mermaid canvas joins the dark document surface, while the dark Mermaid theme keeps state labels, guard labels, edges, arrowheads, the initial marker, and the final marker readable. The diagram body feels like Card content rather than a pasted light image.

The light proof remains coherent: white canvas, dark labels and edges, readable guards, and visible initial/final markers. The semantic graph and its layout are identical between appearances. No obvious theme defect remained after capture inspection.

## Remaining Mermaid pressure

Mermaid still leaves generous unused space around this compact graph, and several transition labels remain close to or cross nearby paths. Those are the same tolerable layout pressures recorded in M19o. Theme qualification neither worsened them nor created a new semantic need. Direct artifact sizing remains unchanged and acceptable at this viewport.

Appearance-specific caching is operationally boring: light and dark are ordinary fixed render options, keys are deterministic, repeat requests hit, and sidecars explain why two artifacts exist. The pinned offline renderer remains sufficient for the diagram Codex actually chose.

## Dogfood answers

1. Mermaid is visually integrated enough in both appearances.
2. Remaining whitespace and label-layout pressure is tolerable for VehicleFlow.
3. Dark mode materially improves usability by removing the abrupt white island while retaining contrast.
4. Appearance-specific caching is deterministic and unsurprising.
5. Direct-SVG pressure is not stronger; the remaining pressure is layout-level, not theme-level.

## Decision and recommendation

`NATIVE_SVG_NOT_JUSTIFIED`

Outcome A applies: appearance qualification works cleanly and Mermaid remains sufficient. The next recommendation is to keep the current backend and gather another real compiler-derived Diagram Card with materially denser relationships before considering any layout or renderer milestone. Do not start native SVG from the remaining VehicleFlow whitespace alone.
