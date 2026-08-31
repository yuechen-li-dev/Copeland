using Copeland.TS.Compiler;
using Copeland.TS.Diagnostics;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;
using Xunit;
using SemanticBinder = Copeland.TS.Semantics.Binder;

namespace Copeland.TS.Tests;

public sealed class VisualizationM0Tests
{
    [Fact]
    public void Parses_and_binds_explicit_reflection_queries()
    {
        const string source = """
record Model { id: int; }
template<type T = Model> Describe: ProjectTree {
    const name = reflect nameOf<T>();
    const fields = reflect fieldsOf<T>();
    return project([textFile(`${name}.txt`, `${fields}`)]);
}
""";

        SyntaxTree tree = SyntaxTree.Parse(source);
        Assert.Empty(tree.Diagnostics);
        TemplateDeclarationSyntax template = Assert.Single(tree.Root.Members.OfType<TemplateDeclarationSyntax>());
        VariableDeclarationStatementSyntax first = Assert.IsType<VariableDeclarationStatementSyntax>(template.Body.Statements[0]);
        Assert.IsType<ReflectExpressionSyntax>(first.Initializer);

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(source);
        BoundTemplateDeclaration bound = Assert.Single(compilation.BoundCompilation!.Program.Templates);
        BoundTemplateLocal local = Assert.IsType<BoundTemplateLocal>(bound.Plan!.Statements[0]);
        BoundTemplateReflection reflection = Assert.IsType<BoundTemplateReflection>(local.Initializer);
        Assert.Equal(BoundSemanticReflectionQuery.NameOf, reflection.Query);
    }

    [Fact]
    public void Rejects_reflection_in_runtime_code()
    {
        BoundCompilation compilation = SemanticBinder.Bind(SyntaxTree.Parse("""
record Model { id: int; }
function runtimeThing(): void {
    const fields = reflect fieldsOf<Model>();
}
"""));

        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0001");
    }

    [Fact]
    public void Diagnoses_plain_reflection_calls_with_migration_guidance()
    {
        TemplateEvaluationResult result = TemplateCompiler.Evaluate("""
record Model { id: int; }
template<> Old: ProjectTree {
    const fields = fieldsOf<Model>();
    return project([textFile("old.txt", `${fields}`)]);
}
""", "Old");

        Diagnostic diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0004");
        Assert.Contains("explicit 'reflect' marker", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnoses_unsupported_queries_and_invalid_targets()
    {
        TemplateEvaluationResult unsupported = TemplateCompiler.Evaluate("""
record Model { id: int; }
template<> Invalid: ProjectTree {
    const syntax = reflect syntaxOf<Model>();
    return project([textFile("invalid.txt", `${syntax}`)]);
}
""", "Invalid");
        Assert.Contains(unsupported.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0002");

        TemplateEvaluationResult fields = TemplateCompiler.Evaluate("""
enum State { Ready, }
template<> Invalid: ProjectTree {
    const metadata = reflect fieldsOf<State>();
    return project([textFile("invalid.txt", `${metadata}`)]);
}
""", "Invalid");
        Assert.Contains(fields.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0005");

        TemplateEvaluationResult cases = TemplateCompiler.Evaluate("""
record Model { id: int; }
template<> Invalid: ProjectTree {
    const metadata = reflect enumCasesOf<Model>();
    return project([textFile("invalid.txt", `${metadata}`)]);
}
""", "Invalid");
        Assert.Contains(cases.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0006");
    }

    [Fact]
    public void Materializes_record_reflection_as_a_typed_diagram()
    {
        TemplateEvaluationResult result = TemplateCompiler.Evaluate("""
record CopelandWorkspace { root: string; project: string; strict: boolean; }
template<type T = CopelandWorkspace> TypeStructure: Diagram {
    const typeName = reflect nameOf<T>();
    const fields = reflect fieldsOf<T>();
    return recordDiagram(typeName, fields, "TopDown");
}
""", "TypeStructure");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Diagram diagram = Assert.IsType<Diagram>(result.Diagram);
        Assert.Equal(DiagramDirection.TopDown, diagram.Direction);
        Assert.Equal(4, diagram.Nodes.Count);
        Assert.Equal(3, diagram.Edges.Count);
        Assert.Equal("CopelandWorkspace", diagram.Provenance.ReflectedType);
        Assert.Contains(diagram.Nodes, node => node.Id == "field:root" && node.Label == "root : string");
    }

    [Fact]
    public void Materializes_enum_reflection_as_a_typed_diagram()
    {
        TemplateEvaluationResult result = TemplateCompiler.Evaluate("""
enum BuildState { Pending, Running(worker: string), Complete, }
template<type T = BuildState> EnumStructure: Diagram {
    const typeName = reflect nameOf<T>();
    const cases = reflect enumCasesOf<T>();
    return enumDiagram(typeName, cases, "LeftRight");
}
""", "EnumStructure");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Diagram diagram = Assert.IsType<Diagram>(result.Diagram);
        Assert.Equal(DiagramDirection.LeftRight, diagram.Direction);
        Assert.Equal(4, diagram.Nodes.Count);
        Assert.Equal(3, diagram.Edges.Count);
        Assert.Contains(diagram.Nodes, node => node.Id == "case:Running" && node.Label == "Running(string)");
    }

    [Fact]
    public void Diagram_validation_rejects_duplicate_ids_and_unknown_edges()
    {
        bool duplicateSucceeded = Diagram.TryCreate(
            [new DiagramNode("same", "One"), new DiagramNode("same", "Two")],
            [],
            DiagramDirection.TopDown,
            new DiagramProvenance("Test", null),
            out _,
            out IReadOnlyList<Diagnostic> duplicateDiagnostics);
        Assert.False(duplicateSucceeded);
        Assert.Contains(duplicateDiagnostics, diagnostic => diagnostic.Id == "COPE-DIAGRAM-0001");

        bool edgeSucceeded = Diagram.TryCreate(
            [new DiagramNode("known", "Known")],
            [new DiagramEdge("known", "missing")],
            DiagramDirection.TopDown,
            new DiagramProvenance("Test", null),
            out _,
            out IReadOnlyList<Diagnostic> edgeDiagnostics);
        Assert.False(edgeSucceeded);
        Assert.Contains(edgeDiagnostics, diagnostic => diagnostic.Id == "COPE-DIAGRAM-0002");

        TemplateEvaluationResult template = TemplateCompiler.Evaluate("""
template<> Invalid: Diagram {
    return diagram(
        [diagramNode("same", "One"), diagramNode("same", "Two")],
        [diagramEdge("same", "missing", "")],
        "TopDown");
}
""", "Invalid");
        Assert.Contains(template.Diagnostics, diagnostic => diagnostic.Id == "COPE-DIAGRAM-0001");
        Assert.Contains(template.Diagnostics, diagnostic => diagnostic.Id == "COPE-DIAGRAM-0002");
    }

    [Fact]
    public void Mermaid_emission_is_escaped_directional_and_deterministic()
    {
        Assert.True(Diagram.TryCreate(
            [
                new DiagramNode("root", "Root [quoted] \"name\"\nnext"),
                new DiagramNode("child", "Child {value}"),
            ],
            [new DiagramEdge("root", "child", "has [edge]")],
            DiagramDirection.LeftRight,
            new DiagramProvenance("Test", null),
            out Diagram? diagram,
            out _));

        string first = MermaidEmitter.Emit(diagram!);
        string second = MermaidEmitter.Emit(diagram!);

        Assert.Equal(first, second);
        Assert.StartsWith("flowchart LR\n", first, StringComparison.Ordinal);
        Assert.Contains("Root &#91;quoted&#93; &quot;name&quot;<br/>next", first, StringComparison.Ordinal);
        Assert.Contains("has &#91;edge&#93;", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Root [quoted]", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Direct_diagram_intrinsics_preserve_typed_nodes_and_edges()
    {
        TemplateEvaluationResult result = TemplateCompiler.Evaluate("""
template<> Direct: Diagram {
    return diagram(
        [diagramNode("root", "Root"), diagramNode("child", "Child")],
        [diagramEdge("root", "child", "contains")],
        "TopDown");
}
""", "Direct");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(["child", "root"], result.Diagram!.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void Diagram_pipeline_has_no_runtime_reflection_dependency()
    {
        string[] referencedAssemblies = typeof(Diagram).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("System.Reflection", StringComparison.Ordinal));
    }
}
