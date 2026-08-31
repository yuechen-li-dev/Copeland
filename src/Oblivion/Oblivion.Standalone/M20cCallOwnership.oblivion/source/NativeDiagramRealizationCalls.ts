function ProjectSemanticDiagram(): void { return; }
function SelectLayoutPolicy(): void { return; }
function ResolveDiagramGeometry(): void { return; }
function EmitCanonicalSvg(): void { return; }
function ValidateDerivedCache(): void { return; }
function RecordArtifactProvenance(): void { return; }
function HostDiagramCanvas(): void { return; }
function PreserveSemanticFallback(): void { return; }

function RealizeNativeDiagramCard(): void {
    ProjectSemanticDiagram();
    SelectLayoutPolicy();
    ResolveDiagramGeometry();
    EmitCanonicalSvg();
    ValidateDerivedCache();
    ValidateDerivedCache();
    RecordArtifactProvenance();
    HostDiagramCanvas();
    PreserveSemanticFallback();
}

template<> NativeDiagramRealizationCalls: Diagram {
    const calls = reflect callsOf<RealizeNativeDiagramCard>();
    return callGraphDiagram(calls);
}
