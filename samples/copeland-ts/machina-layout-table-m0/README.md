# Machina layout table M0 fixtures

Each directory contains a single focused typed-source fixture for the CSV-shaped
stream overlay surface. The compiler tests in
`LayoutTableSurfaceM0Tests.cs` assert their language laws; `10-stream-browser`
is the TSPack-owned browser proof.

| Fixture | Law |
|---|---|
| `01-basic-overlay` | typed overlay rows become named stream slots |
| `02-default-layer-z` | omitted layer/z are `default` / `0` |
| `03-column-reordering` | headers, not positions, assign cells |
| `04-nested-table` | table overlay has one ordinary nested parent |
| `05-nested-equivalence` | table and nested forms normalize alike |
| `06-layout-type-conformance` | rows infer exact layout topology |
| `07-invalid-cells` | diagnostics identify row and column |
| `08-complex-content-expression` | call commas remain inside content cells |
| `09-layer-paint-order` | row order resolves equal paint ties |
| `10-stream-browser` | TSPack/Playwright geometry and overlap proof |
| `11-third-party-react` | imported React component is a content cell |
