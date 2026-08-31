# Diagram IR — VIZ-M0

`Diagram` is the smallest backend-independent semantic graph result accepted by
the existing typed-template evaluator:

```text
Diagram
  nodes: DiagramNode { id, label }[]
  edges: DiagramEdge { from, to, label? }[]
  direction: TopDown | LeftRight
  provenance: { template, reflectedType? }
```

Node IDs are non-empty, stable, case-sensitive semantic identities. Reflection
proofs use `type:<name>`, `field:<name>`, and `case:<name>`; no GUID, timestamp,
path, backend token, or label text is used as incidental identity. Duplicate IDs
diagnose as `COPE-DIAGRAM-0001`. Every edge endpoint must name an existing node;
unknown endpoints diagnose as `COPE-DIAGRAM-0002`.

Construction normalizes nodes by ID and edges by `(from, to, label)` using
ordinal ordering. Identical source, semantic model, inputs, and template
therefore produce byte-identical previews. `Diagram` contains no coordinates,
sizes, fonts, CSS, SVG paths, Mermaid syntax, or native layout policy.

Templates may construct a direct graph with `diagramNode`, `diagramEdge`, and
`diagram`, or use the bounded `recordDiagram` and `enumDiagram` adapters to turn
the existing reflected metadata values into the same node/edge IR. These
adapters exist because the M0 static language has no general collection-map
primitive; they do not add a separate visualization language.
