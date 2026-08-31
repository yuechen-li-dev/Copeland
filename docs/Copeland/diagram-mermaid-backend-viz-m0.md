# Diagram to Mermaid backend — VIZ-M0

The bootstrap backend lowers `Diagram` to inspectable Mermaid source:

```text
Copeland TS -> reflect -> Diagram -> MermaidEmitter -> diagram.mmd
                                                    -> external renderer -> PNG
```

`TopDown` emits `flowchart TD`; `LeftRight` emits `flowchart LR`. Semantic node
IDs are mapped deterministically to backend-local `n0`, `n1`, and so on. Nodes
and edges are emitted in normalized Diagram order.

The emitter owns Mermaid syntax. It escapes ampersands, quotes, square and curly
brackets, angle brackets, and newlines in semantic labels. Templates never
concatenate Mermaid source.

`tscl template preview ... --format mermaid` writes source to stdout.
`tscl template materialize ... --output <directory>` writes
`<directory>/diagram.mmd`; an explicit `.mmd` output path is also accepted. The
source is the inspectable artifact and remains useful when no renderer exists.

The maintained proof uses the repository-qualified
`@mermaid-js/mermaid-cli@11.16.0` installation to render the emitted source. The
Copeland compiler does not depend on `Oblivion.App` or duplicate its process
adapter. A future backend may implement `Diagram -> layout -> SVG` without
changing template authoring or Diagram IR. Native layout and SVG generation are
not part of VIZ-M0.

The checked-in proofs are
`samples/copeland-ts/visualization/artifacts/type-diagram.mmd`,
`enum-diagram.mmd`, and the derived rendered `type-diagram.png`.
