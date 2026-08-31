// VIZ-M0 maintained semantic-visualization proofs.

record CopelandWorkspaceDescriptor {
    root: string;
    project: string;
    strictOwnership: boolean;
}

enum CompilationState {
    Discovered,
    Binding(module: string),
    Materialized(artifact: string),
}

template<type T = CopelandWorkspaceDescriptor> TypeDiagram: Diagram {
    const typeName = reflect nameOf<T>();
    const fields = reflect fieldsOf<T>();
    return recordDiagram(typeName, fields, "TopDown");
}

template<type T = CompilationState> EnumDiagram: Diagram {
    const typeName = reflect nameOf<T>();
    const cases = reflect enumCasesOf<T>();
    return enumDiagram(typeName, cases, "LeftRight");
}
