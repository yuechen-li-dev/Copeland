using Copeland.TS.Compiler;
using Copeland.TS.Semantics;
using Copeland.TS.Semantics.Bound;
using Copeland.TS.Syntax;
using Copeland.TS.Templates;
using Xunit;
using SemanticBinder = Copeland.TS.Semantics.Binder;

namespace Copeland.TS.Tests;

public sealed class VisualizationM2Tests
{
    private const string VehicleFlowSource = """
flow VehicleFlow -> number {
    board { speed: number = 0; impact: boolean = false; }
    event Start(speed: number);
    event Stop(speed: number);
    event Brake(engaged: boolean);
    event Impact(detected: boolean);
    event Wait();
    state Still initial {
        on Start(speed) when speed > 0 -> Moving { board.speed = speed; };
        on Wait() when board.speed == 0 -> Still;
    }
    state Moving {
        on Stop(speed) when speed == 0 -> Still { board.speed = speed; };
        on Brake(engaged) when engaged == true -> Still;
        on Impact(detected) when detected == true -> Crash { board.impact = detected; };
    }
    state Crash { finish board.speed; }
}
""";

    [Fact]
    public void Extracts_ordered_syntax_free_state_machine_semantics()
    {
        CopelandCompilation compilerResult = CopelandCompiler.CompileTemplates(
            VehicleFlowSource,
            new CopelandCompilationOptions { SourcePath = "VehicleFlow.ts" });
        BoundCompilation compilation = compilerResult.BoundCompilation!;

        Assert.Empty(compilation.Diagnostics);
        BoundFlowDefinition flow = Assert.Single(compilation.Program.Flows);
        Assert.True(StateMachineDiagramProjection.TryCreateSemanticView(
            flow,
            out StateMachineSemanticView? view,
            out var diagnostics),
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)));

        Assert.Equal("flow:VehicleFlow", view!.Identity);
        Assert.Equal(
            ["flow:VehicleFlow.state:Still", "flow:VehicleFlow.state:Moving", "flow:VehicleFlow.state:Crash"],
            view.States.Select(state => state.Identity));
        Assert.Equal("flow:VehicleFlow.state:Still", view.InitialStateIdentity);
        Assert.Equal(["flow:VehicleFlow.state:Crash"], view.FinalStateIdentities);
        Assert.Equal(5, view.Transitions.Count);
        Assert.Equal(Enumerable.Range(0, 5), view.Transitions.Select(transition => transition.Order));
        Assert.Equal(
            [
                "flow:VehicleFlow.state:Still.transition:0",
                "flow:VehicleFlow.state:Still.transition:1",
                "flow:VehicleFlow.state:Moving.transition:0",
                "flow:VehicleFlow.state:Moving.transition:1",
                "flow:VehicleFlow.state:Moving.transition:2",
            ],
            view.Transitions.Select(transition => transition.Identity));
        Assert.All(view.Transitions, transition => Assert.NotNull(transition.Guard));
        Assert.All(view.Transitions, transition => Assert.Equal("VehicleFlow.ts", transition.Source.Path));
        Assert.All(view.Transitions, transition => Assert.NotNull(transition.GuardSource));
        Assert.True(view.Source.StartLine > 0);
    }

    [Fact]
    public void Projects_guards_self_bidirectional_and_parallel_transitions_without_collapsing()
    {
        BoundCompilation compilation = SemanticBinder.Bind(SyntaxTree.Parse(VehicleFlowSource));

        Assert.True(StateMachineDiagramProjection.TryProject(
            compilation.Program,
            "VehicleFlow",
            out _,
            out Diagram? diagram,
            out var diagnostics),
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)));

        Assert.Equal(DiagramBackendKind.State, diagram!.BackendKind);
        Assert.Equal("flow:VehicleFlow.state:Still", diagram.InitialNodeId);
        Assert.Equal(["flow:VehicleFlow.state:Crash"], diagram.FinalNodeIds);
        Assert.Equal(5, diagram.Edges.Count);
        Assert.Contains(diagram.Edges, edge => edge.From == edge.To && edge.Label == "Wait [board.speed == 0]");
        Assert.Contains(diagram.Edges, edge => edge.From.EndsWith(":Still", StringComparison.Ordinal) && edge.To.EndsWith(":Moving", StringComparison.Ordinal));
        Assert.Equal(
            2,
            diagram.Edges.Count(edge => edge.From.EndsWith(":Moving", StringComparison.Ordinal)
                && edge.To.EndsWith(":Still", StringComparison.Ordinal)));
        Assert.Equal(
            ["Start [speed > 0]", "Wait [board.speed == 0]", "Stop [speed == 0]", "Brake [engaged == true]", "Impact [detected == true]"],
            diagram.Edges.Select(edge => edge.Label));
    }

    [Fact]
    public void Emits_state_diagram_v2_initial_final_guards_and_deterministic_backend_ids()
    {
        BoundCompilation firstCompilation = SemanticBinder.Bind(SyntaxTree.Parse(VehicleFlowSource));
        BoundCompilation secondCompilation = SemanticBinder.Bind(SyntaxTree.Parse(VehicleFlowSource));
        Assert.True(StateMachineDiagramProjection.TryProject(firstCompilation.Program, "VehicleFlow", out _, out Diagram? first, out _));
        Assert.True(StateMachineDiagramProjection.TryProject(secondCompilation.Program, "VehicleFlow", out _, out Diagram? second, out _));

        string firstMermaid = MermaidEmitter.Emit(first!);
        string secondMermaid = MermaidEmitter.Emit(second!);

        Assert.Equal(firstMermaid, secondMermaid);
        Assert.StartsWith("stateDiagram-v2\n", firstMermaid, StringComparison.Ordinal);
        Assert.Contains("    [*] --> s2\n", firstMermaid, StringComparison.Ordinal);
        Assert.Contains("s2 --> s1: Start [speed > 0]", firstMermaid, StringComparison.Ordinal);
        Assert.Contains("s2 --> s2: Wait [board.speed == 0]", firstMermaid, StringComparison.Ordinal);
        Assert.Contains("    s0 --> [*]\n", firstMermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void State_backend_escapes_labels_and_transition_text()
    {
        Assert.True(Diagram.TryCreate(
            [
                new DiagramNode("state:a", "Still \"quoted\" [ready]\n下一步"),
                new DiagramNode("state:b", "Crash: {terminal}"),
            ],
            [
                new DiagramEdge("state:a", "state:b", "Impact [value: \"x\" < 1]", "transition:0", 0),
                new DiagramEdge("state:b", "state:a", "Reset", "transition:1", 1),
            ],
            DiagramDirection.TopDown,
            new DiagramProvenance("Test", "flow:Escaping"),
            out Diagram? diagram,
            out _,
            DiagramBackendKind.State,
            "state:a",
            ["state:b"]));

        string mermaid = MermaidEmitter.Emit(diagram!);

        Assert.Contains("Still &quot;quoted&quot; [ready]<br/>下一步", mermaid, StringComparison.Ordinal);
        Assert.Contains("Crash: {terminal}", mermaid, StringComparison.Ordinal);
        Assert.Contains("Impact [value: \"x\" < 1]", mermaid, StringComparison.Ordinal);
        Assert.Contains("s1 --> s0: Reset", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void Projection_reports_unknown_target_invalid_identity_and_bounds()
    {
        BoundCompilation compilation = SemanticBinder.Bind(SyntaxTree.Parse(VehicleFlowSource));
        Assert.False(StateMachineDiagramProjection.TryProject(
            compilation.Program,
            "Missing",
            out _,
            out _,
            out var unknownDiagnostics));
        Assert.Contains(unknownDiagnostics, diagnostic => diagnostic.Id == "COPE-STATE-DIAGRAM-0001");

        FlowSourceCorrelation source = Source();
        var missingIdentity = new StateMachineSemanticView(
            "flow:Broken",
            "Broken",
            [new StateMachineState(string.Empty, "Missing", source)],
            [],
            string.Empty,
            [],
            source);
        Assert.False(StateMachineDiagramProjection.TryProject(missingIdentity, out _, out var identityDiagnostics));
        Assert.Contains(identityDiagnostics, diagnostic => diagnostic.Id == "COPE-STATE-DIAGRAM-0007");

        StateMachineState[] tooManyStates = Enumerable.Range(0, StateMachineDiagramLimits.MaximumStates + 1)
            .Select(index => new StateMachineState("state:" + index, "State " + index, source))
            .ToArray();
        var oversized = new StateMachineSemanticView(
            "flow:Large",
            "Large",
            tooManyStates,
            [],
            tooManyStates[0].Identity,
            [],
            source);
        Assert.False(StateMachineDiagramProjection.TryProject(oversized, out _, out var sizeDiagnostics));
        Assert.Contains(sizeDiagnostics, diagnostic => diagnostic.Id == "COPE-STATE-DIAGRAM-0005");
    }

    [Fact]
    public void Projection_reports_missing_state_duplicate_ids_and_unrepresentable_guard()
    {
        FlowSourceCorrelation source = Source();
        BoundExpression hugeGuard = new BoundLiteralExpression(
            new string('x', StateMachineDiagramLimits.MaximumGuardSemanticBytes + 1),
            PrimitiveTypeSymbol.Boolean);
        var view = new StateMachineSemanticView(
            "flow:Broken",
            "Broken",
            [
                new StateMachineState("state:same", "One", source),
                new StateMachineState("state:same", "Two", source),
            ],
            [new StateMachineTransition(
                "transition:0",
                0,
                "state:same",
                "state:missing",
                "Go",
                hugeGuard,
                source,
                source)],
            "state:same",
            [],
            source);

        Assert.False(StateMachineDiagramProjection.TryProject(view, out _, out var diagnostics));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "COPE-STATE-DIAGRAM-0008");

        StateMachineSemanticView structuralView = view with
        {
            Transitions =
            [
                view.Transitions[0] with
                {
                    Guard = null,
                },
            ],
        };
        Assert.False(StateMachineDiagramProjection.TryProject(structuralView, out _, out diagnostics));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "COPE-DIAGRAM-0001");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "COPE-DIAGRAM-0002");

        StateMachineSemanticView missingTransitionIdentity = structuralView with
        {
            States = [new StateMachineState("state:only", "Only", source)],
            InitialStateIdentity = "state:only",
            Transitions =
            [
                new StateMachineTransition(
                    string.Empty,
                    0,
                    "state:only",
                    "state:only",
                    "Wait",
                    null,
                    source,
                    null),
            ],
        };
        Assert.False(StateMachineDiagramProjection.TryProject(missingTransitionIdentity, out _, out diagnostics));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "COPE-STATE-DIAGRAM-0010");
    }

    [Fact]
    public void Guard_display_is_deterministically_shortened_without_losing_semantic_guard()
    {
        string longName = new('g', StateMachineDiagramLimits.MaximumGuardDisplayBytes + 20);
        var variable = new VariableSymbol(longName, PrimitiveTypeSymbol.Boolean, true);
        BoundExpression guard = new BoundVariableExpression(variable);
        FlowSourceCorrelation source = Source();
        var view = new StateMachineSemanticView(
            "flow:LongGuard",
            "LongGuard",
            [new StateMachineState("state:only", "Only", source)],
            [new StateMachineTransition("transition:0", 0, "state:only", "state:only", "Wait", guard, source, source)],
            "state:only",
            [],
            source);

        Assert.True(StateMachineDiagramProjection.TryProject(view, out Diagram? diagram, out _));
        Assert.Same(guard, view.Transitions[0].Guard);
        Assert.EndsWith("...]", Assert.Single(diagram!.Edges).Label, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(
            Assert.Single(diagram.Edges).Label!.Split('[', 2)[1].TrimEnd(']'))
            <= StateMachineDiagramLimits.MaximumGuardDisplayBytes);
    }

    private static FlowSourceCorrelation Source()
        => new("test.ts", 1, 1, 1, 2);
}
