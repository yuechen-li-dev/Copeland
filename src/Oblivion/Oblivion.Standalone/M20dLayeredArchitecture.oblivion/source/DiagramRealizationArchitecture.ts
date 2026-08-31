template<> DiagramRealizationArchitecture: Diagram {
    return diagram(
        [
            diagramNode("workspace-loader", "Workspace loader"),
            diagramNode("card-model", "Card model"),
            diagramNode("semantic-projector", "Semantic projector"),
            diagramNode("diagram-ir", "Diagram IR"),
            diagramNode("mermaid-emitter", "Mermaid emitter"),
            diagramNode("mermaid-renderer", "Mermaid renderer"),
            diagramNode("native-layout", "Native layout"),
            diagramNode("native-svg", "Native SVG emitter"),
            diagramNode("derived-cache", "Derived cache"),
            diagramNode("provenance", "Provenance"),
            diagramNode("source-correlation", "Source correlation"),
            diagramNode("content-realization", "Content realization"),
            diagramNode("diagram-canvas", "Diagram Canvas"),
            diagramNode("viewport-state", "Viewport state")
        ],
        [
            diagramEdge("workspace-loader", "card-model", "loads"),
            diagramEdge("card-model", "semantic-projector", "configures"),
            diagramEdge("semantic-projector", "diagram-ir", "projects"),
            diagramEdge("diagram-ir", "mermaid-emitter", "emits"),
            diagramEdge("mermaid-emitter", "mermaid-renderer", "renders"),
            diagramEdge("diagram-ir", "native-layout", "resolves"),
            diagramEdge("native-layout", "native-svg", "emits"),
            diagramEdge("mermaid-renderer", "derived-cache", "PNG"),
            diagramEdge("native-svg", "derived-cache", "SVG"),
            diagramEdge("mermaid-renderer", "provenance", "records"),
            diagramEdge("native-svg", "provenance", "records"),
            diagramEdge("diagram-ir", "source-correlation", "identifies"),
            diagramEdge("provenance", "source-correlation", "retains"),
            diagramEdge("derived-cache", "content-realization", "artifact"),
            diagramEdge("provenance", "content-realization", "owner"),
            diagramEdge("source-correlation", "content-realization", "source"),
            diagramEdge("content-realization", "diagram-canvas", "hosts"),
            diagramEdge("viewport-state", "diagram-canvas", "applies"),
            diagramEdge("diagram-canvas", "viewport-state", "updates")
        ],
        "LeftRight");
}
