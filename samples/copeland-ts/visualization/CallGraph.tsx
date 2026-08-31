// Maintained VIZ-M1 dogfood: this is ordinary executable Copeland source.
// The template names only CompileWorkspace; every edge comes from bound calls.

function ParseSources(): void {
    return;
}

function BindModules(): void {
    ValidateImports();
}

function ValidateImports(): void {
    return;
}

function LowerProgram(): void {
    return;
}

function EmitBackends(): void {
    return;
}

function WriteArtifacts(): void {
    return;
}

function CompileWorkspace(): void {
    ParseSources();
    BindModules();
    BindModules();
    LowerProgram();
    EmitBackends();
    WriteArtifacts();
}

template<> CompilerCallGraph: Diagram {
    const calls = reflect callsOf<CompileWorkspace>();
    return callGraphDiagram(calls);
}
