using Copeland.TS.Compiler;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Templates;
using Xunit;
using SemanticBinder = Copeland.TS.Semantics.Binder;

namespace Copeland.TS.Tests;

public sealed class VisualizationM1Tests
{
    private const string CallGraphSource = """
function ParseSources(): void { return; }
function BindModules(): void { ValidateImports(); }
function ValidateImports(): void { return; }
function LowerProgram(): void { return; }
function EmitBackend(): void { return; }

function CompileWorkspace(): void {
    ParseSources();
    BindModules();
    BindModules();
    LowerProgram();
    EmitBackend();
}

template<> CompilerCalls: Diagram {
    const calls = reflect callsOf<CompileWorkspace>();
    return callGraphDiagram(calls);
}
""";

    [Fact]
    public void Reflects_direct_calls_in_source_order_with_typed_identity_and_correlation()
    {
        BoundCompilation compilation = SemanticBinder.Bind(Syntax.SyntaxTree.Parse(CallGraphSource));
        Assert.Empty(compilation.Diagnostics);

        BoundSemanticCallSite[] calls = compilation.Program.SemanticCallSites
            .Where(call => call.Caller.Name == "CompileWorkspace")
            .ToArray();

        Assert.Equal(5, calls.Length);
        Assert.Equal(
            ["ParseSources", "BindModules", "BindModules", "LowerProgram", "EmitBackend"],
            calls.Select(call => call.Callee!.Name));
        Assert.All(calls, call => Assert.Equal(ReflectedCallKind.Direct, call.Kind));
        Assert.All(calls, call => Assert.StartsWith("function:", call.Caller.Id, StringComparison.Ordinal));
        Assert.Equal([8, 9, 10, 11, 12], calls.Select(call => call.Source.StartLine));
        Assert.All(calls, call => Assert.True(call.Source.StartColumn > 1));

        BoundSemanticCallSite nested = Assert.Single(
            compilation.Program.SemanticCallSites,
            call => call.Caller.Name == "BindModules");
        Assert.Equal("ValidateImports", nested.Callee!.Name);
        Assert.DoesNotContain(calls, call => call.Callee!.Name == "ValidateImports");
    }

    [Fact]
    public void Projects_call_sites_to_aggregated_deterministic_diagram_and_mermaid()
    {
        TemplateEvaluationResult first = TemplateCompiler.Evaluate(CallGraphSource, "CompilerCalls");
        TemplateEvaluationResult second = TemplateCompiler.Evaluate(CallGraphSource, "CompilerCalls");

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(second.Success, string.Join(Environment.NewLine, second.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Diagram diagram = Assert.IsType<Diagram>(first.Diagram);
        Assert.Equal(DiagramDirection.LeftRight, diagram.Direction);
        Assert.Equal(5, diagram.Nodes.Count);
        Assert.Equal(4, diagram.Edges.Count);
        Assert.Contains(diagram.Nodes, node => node.Id.Contains("CompileWorkspace", StringComparison.Ordinal));
        Assert.Contains(diagram.Edges, edge => edge.Label == "×2");

        string firstMermaid = MermaidEmitter.Emit(diagram);
        string secondMermaid = MermaidEmitter.Emit(second.Diagram!);
        Assert.Equal(firstMermaid, secondMermaid);
        Assert.StartsWith("flowchart LR\n", firstMermaid, StringComparison.Ordinal);
        Assert.Contains("CompileWorkspace", firstMermaid, StringComparison.Ordinal);
        Assert.Contains("BindModules", firstMermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnoses_runtime_plain_and_invalid_call_reflection()
    {
        BoundCompilation runtime = SemanticBinder.Bind(Syntax.SyntaxTree.Parse("""
function Target(): void { return; }
function Runtime(): void {
    const calls = reflect callsOf<Target>();
}
"""));
        Assert.Contains(runtime.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0001");

        TemplateEvaluationResult plain = TemplateCompiler.Evaluate("""
function Target(): void { return; }
template<> Invalid: Diagram {
    const calls = callsOf<Target>();
    return callGraphDiagram(calls);
}
""", "Invalid");
        Assert.Contains(plain.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0004");

        TemplateEvaluationResult invalid = TemplateCompiler.Evaluate("""
record Target { value: int; }
template<> Invalid: Diagram {
    const calls = reflect callsOf<Target>();
    return callGraphDiagram(calls);
}
""", "Invalid");
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0007");
    }

    [Fact]
    public void Enforces_direct_call_site_query_bound()
    {
        string calls = string.Join(Environment.NewLine, Enumerable.Repeat("    Leaf();", 257));
        string source = $$"""
function Leaf(): void { return; }
function TooLarge(): void {
{{calls}}
}
template<> Calls: Diagram {
    const calls = reflect callsOf<TooLarge>();
    return callGraphDiagram(calls);
}
""";

        TemplateEvaluationResult result = TemplateCompiler.Evaluate(source, "Calls");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "COPE-REFLECT-0008");
    }

    [Fact]
    public void Represents_dynamic_callable_invocation_without_inventing_a_target()
    {
        BoundCompilation compilation = SemanticBinder.Bind(Syntax.SyntaxTree.Parse("""
type Operation = () => void;
function Invoke(operation: Operation): void {
    operation();
}
"""));

        Assert.Empty(compilation.Diagnostics);
        BoundSemanticCallSite call = Assert.Single(compilation.Program.SemanticCallSites);
        Assert.Equal(ReflectedCallKind.Dynamic, call.Kind);
        Assert.Null(call.Callee);
        Assert.Equal("callable invocation", call.UnresolvedDisplayName);
    }

    [Fact]
    public void Call_reflection_introduces_no_runtime_reflection_dependency()
    {
        string[] referencedAssemblies = typeof(BoundSemanticCallSite).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("System.Reflection", StringComparison.Ordinal));
    }
}
