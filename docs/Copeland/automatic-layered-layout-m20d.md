# Automatic layered native layout — M20d

## Contract and identity

`automatic-layered-v1` is the stable policy identity qualified by M20d. It consumes the unchanged semantic `Diagram` IR plus its existing `TD`/`LR` direction and emits resolved rectangles, orthogonal routes, label anchors, metrics, and bounded diagnostics. It does not consume viewport dimensions, domain priority tables, authored lanes, spines, coordinates, or graph-specific hints.

The SVG backend remains `native-svg-v1`; its version is `1.0.4` because M20d adds visible arrowheads to the production SVG output. Cache identity includes semantic fingerprint, renderer/version, layout-policy identity, appearance, output format, and fixed inert-SVG options. The same Diagram under automatic, phase-lanes, and branching-calls policies therefore has different keys.

## Algorithm

1. Normalize adjacency from canonical Diagram node and edge order.
2. Find strongly connected components with deterministic Tarjan traversal.
3. Rank the component DAG by stable topological order and longest predecessor pressure. Nodes inside a cyclic component receive stable semantic-identity order; edges against that order become back edges rather than disappearing.
4. Seed each layer by weak-component identity and semantic node identity.
5. Run four alternating, deterministic barycenter sweeps. Stable prior position and semantic identity break ties.
6. Center each layer on the cross axis. Production label measurement, padding, and minimum dimensions determine node rectangles.
7. Route ordinary adjacent edges orthogonally, cross-layer edges as retained segmented routes, back edges outside the forward layers, and self edges as bounded loops. Parallel semantic edges remain separate.
8. Emit canonical invariant-number SVG with stable accessibility/source IDs and arrowheads.

The main graph operations are `O(V + E)` plus fixed ordering sweeps of approximately `O(V log V + E)`. The diagnostic adjacent-layer crossing estimate is `O(E²)` under the existing 512-edge hard bound. There is no random seed, force simulation, global search, or unbounded optimization.

## Cycles, components, and bounds

Cycles are not erased. A stable SCC orientation determines ranking, and every reverse edge is retained as an outer routed back edge. A two-way pair consequently has one forward route and one spatially distinct return route. Self edges use a bounded loop. Multi-parent nodes remain single nodes with every incoming edge. Disconnected components are ordered by their lowest stable semantic identity and separated by a fixed component gap.

The existing bounds remain 1–256 nodes, at most 512 edges, 4096 UTF-8 bytes per label, and 2 MiB emitted SVG. Empty or over-bound graphs produce an explicit layout failure; the renderer boundary can then use the existing Mermaid fallback. Unknown policies remain explicit failures. Normal graphs receive no diagnostics; cycles, back edges, and cross-layer edges produce compact typed diagnostics in the resolved-layout metadata.

## Determinism and geometry

Repeated clean resolution and SVG emission for Graphs A, B, and C were byte-identical in focused tests. Graph C resolves to 11 layers, one weak component, one back edge, three cross-layer edges, and an adjacent-layer crossing estimate of one. Light and dark share geometry and differ only through appearance-qualified cache/provenance and colors.

`TD` and `LR` use the same ranking and ordering, with axes transposed. Graph C retains authored `LR`; viewport size never changes its topology. Fit/Zoom/Pan/Reset continue through the shared Diagram Canvas without a policy-specific path.

## Fallback verdict

The algorithm is a good default for Graph C, but not yet a universal replacement for specialized policies. Graph A's large strongly connected state region becomes a tall 432×1580 automatic result. Graph B's single high-fanout layer becomes a 574×852 automatic result. Both preserve all semantics deterministically, but constrained Fit requires zoom. Unsupported or over-bound inputs remain diagnostic and fall back through the existing renderer boundary rather than emitting silent nonsense.
