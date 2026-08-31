using Copeland.TS.Templates;
using System.Diagnostics;
using System.Text.Json;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;
using Xunit;
using Xunit.Abstractions;

namespace Oblivion.App.Tests;

public sealed class NativeDiagramBackendTests
{
    private readonly ITestOutputHelper _output;

    public NativeDiagramBackendTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Graphs_a_and_b_preserve_semantics_and_use_distinct_layout_strategies()
    {
        (Diagram graphA, string fingerprintA) = Project(M20aRoot);
        (Diagram graphB, string fingerprintB) = Project(M20cRoot);

        Assert.Equal(16, graphA.Nodes.Count);
        Assert.Equal(31, graphA.Edges.Count);
        Assert.Equal(24, graphA.Edges.Count(edge => edge.Label!.Contains('[', StringComparison.Ordinal)));
        Assert.Equal(
            "a7c7e007bdc4faedb589cd23f5b3f6269cdee589ee635af3c426b4338c68b5d1",
            fingerprintA);
        Assert.Equal(9, graphB.Nodes.Count);
        Assert.Equal(8, graphB.Edges.Count);
        Assert.Contains(graphB.Edges, edge => edge.Label == "×2");
        Assert.NotEqual(fingerprintA, fingerprintB);

        OblivionResolvedDiagram resolvedA = OblivionNativeDiagramLayout.Resolve(graphA);
        OblivionResolvedDiagram resolvedB = OblivionNativeDiagramLayout.Resolve(graphB);
        Assert.Equal(OblivionNativeDiagramPolicies.PhaseLanesV1, resolvedA.LayoutPolicyIdentity);
        Assert.Equal(OblivionNativeDiagramPolicies.BranchingCallsV1, resolvedB.LayoutPolicyIdentity);
        Assert.Equal(graphA.Nodes.Select(node => node.Id), resolvedA.Nodes.Select(node => node.Id));
        Assert.Equal(graphB.Nodes.Select(node => node.Id), resolvedB.Nodes.Select(node => node.Id));
    }

    [Theory]
    [InlineData("M20aDenseDiagram.oblivion")]
    [InlineData("M20cCallOwnership.oblivion")]
    [InlineData("M20dLayeredArchitecture.oblivion")]
    public void Geometry_and_svg_are_deterministic_accessible_inert_and_source_correlated(
        string fixture)
    {
        (Diagram diagram, _) = Project(Path.Combine(FixturesRoot, fixture));

        Stopwatch layoutTimer = Stopwatch.StartNew();
        OblivionResolvedDiagram first = OblivionNativeDiagramLayout.Resolve(diagram);
        layoutTimer.Stop();
        OblivionResolvedDiagram second = OblivionNativeDiagramLayout.Resolve(diagram);
        Stopwatch emissionTimer = Stopwatch.StartNew();
        string firstSvg = OblivionNativeDiagramSvgEmitter.Emit(
            first,
            OblivionResolvedAppearance.Dark,
            "Qualified diagram");
        emissionTimer.Stop();
        string secondSvg = OblivionNativeDiagramSvgEmitter.Emit(
            second,
            OblivionResolvedAppearance.Dark,
            "Qualified diagram");

        _output.WriteLine(
            "{0}: layout={1:F3}ms; svg={2:F3}ms; bytes={3}",
            fixture,
            layoutTimer.Elapsed.TotalMilliseconds,
            emissionTimer.Elapsed.TotalMilliseconds,
            System.Text.Encoding.UTF8.GetByteCount(firstSvg));

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(firstSvg, secondSvg);
        Assert.Contains("<title id=\"diagram-title\">Qualified diagram</title>", firstSvg);
        Assert.Contains("<desc id=\"diagram-description\">", firstSvg);
        Assert.DoesNotContain("<script", firstSvg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreignObject", firstSvg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", firstSvg, StringComparison.OrdinalIgnoreCase);
        Assert.All(diagram.Nodes, node => Assert.Contains(
            $"data-node-id=\"{node.Id}\"",
            firstSvg,
            StringComparison.Ordinal));
        Assert.All(first.Edges, edge => Assert.Contains(
            $"data-edge-id=\"{edge.Id}\"",
            firstSvg,
            StringComparison.Ordinal));
        Assert.All(
            diagram.Edges.Where(edge => !string.IsNullOrWhiteSpace(edge.Label)),
            edge => Assert.Contains($"<title>{edge.Label}</title>", firstSvg, StringComparison.Ordinal));
    }

    [Fact]
    public void Graph_c_is_real_non_star_reconvergent_and_uses_automatic_layering_without_hints()
    {
        (Diagram graphC, string fingerprint) = Project(M20dRoot);

        Assert.Equal(14, graphC.Nodes.Count);
        Assert.Equal(19, graphC.Edges.Count);
        Assert.Equal(DiagramDirection.LeftRight, graphC.Direction);
        Assert.Null(graphC.Provenance.ReflectedType);
        Assert.False(string.IsNullOrWhiteSpace(fingerprint));
        Assert.Equal(5, graphC.Nodes.Count(node => graphC.Edges.Count(edge => edge.To == node.Id) > 1));
        Assert.Equal(3, graphC.Nodes.Max(node => graphC.Edges.Count(edge => edge.To == node.Id)));
        Assert.Equal(3, graphC.Nodes.Max(node => graphC.Edges.Count(edge => edge.From == node.Id)));
        Assert.Equal(33, graphC.Nodes.Count + graphC.Edges.Count(edge => edge.Label is not null));
        Assert.Equal(19, graphC.Nodes.Max(node => node.Label.Length));
        Assert.Contains(graphC.Edges, edge => edge.From == "diagram-canvas" && edge.To == "viewport-state");
        Assert.Contains(graphC.Edges, edge => edge.From == "viewport-state" && edge.To == "diagram-canvas");

        OblivionResolvedDiagram resolved = OblivionNativeDiagramLayout.Resolve(graphC);

        Assert.Equal(OblivionNativeDiagramPolicies.AutomaticLayeredV1, resolved.LayoutPolicyIdentity);
        Assert.Equal(graphC.Nodes.Count, resolved.Nodes.Count);
        Assert.Equal(graphC.Edges.Count, resolved.Edges.Count);
        Assert.NotNull(resolved.Metrics);
        Assert.Equal(1, resolved.Metrics.BackEdgeCount);
        Assert.Equal(1, resolved.Metrics.ComponentCount);
        Assert.Contains(resolved.Edges, edge => edge.RouteKind == "back-edge");
        Assert.Contains(
            resolved.Diagnostics!,
            diagnostic => diagnostic.Code == "OBLIVION-NATIVE-CYCLE-NORMALIZED");
        Assert.Single(resolved.Nodes, node => node.Id == "content-realization");
        Assert.Equal(3, resolved.Edges.Count(edge => edge.To == "content-realization"));
    }

    [Fact]
    public void Automatic_layering_is_deterministic_for_all_three_real_graphs()
    {
        foreach (string root in new[] { M20aRoot, M20cRoot, M20dRoot })
        {
            (Diagram diagram, _) = Project(root);
            OblivionNativeLayoutPolicy policy = new(
                OblivionNativeDiagramPolicies.AutomaticLayeredV1,
                "automatic-layered");

            OblivionResolvedDiagram first = OblivionNativeDiagramLayout.Resolve(diagram, policy);
            OblivionResolvedDiagram second = OblivionNativeDiagramLayout.Resolve(diagram, policy);
            string firstJson = JsonSerializer.Serialize(first);
            string secondJson = JsonSerializer.Serialize(second);
            string firstSvg = OblivionNativeDiagramSvgEmitter.Emit(
                first,
                OblivionResolvedAppearance.Dark,
                "Automatic layout determinism");
            string secondSvg = OblivionNativeDiagramSvgEmitter.Emit(
                second,
                OblivionResolvedAppearance.Dark,
                "Automatic layout determinism");

            _output.WriteLine(
                "{0}: automatic={1}x{2}; layers={3}; crossings={4}; svg={5} bytes",
                Path.GetFileName(root),
                first.Width,
                first.Height,
                first.Metrics!.LayerCount,
                first.Metrics.CrossingEstimate,
                System.Text.Encoding.UTF8.GetByteCount(firstSvg));
            Assert.Equal(firstJson, secondJson);
            Assert.Equal(firstSvg, secondSvg);
            Assert.Equal(diagram.Nodes.Count, first.Nodes.Count);
            Assert.Equal(diagram.Edges.Count, first.Edges.Count);
        }
    }

    [Fact]
    public void Automatic_layering_handles_bounded_failure_topologies_explicitly()
    {
        Diagram single = CreateDiagram([new DiagramNode("only", "Only")], []);
        Diagram disconnected = CreateDiagram(
            [
                new DiagramNode("a", "A"),
                new DiagramNode("b", "B"),
                new DiagramNode("c", "C"),
            ],
            [new DiagramEdge("a", "b", "connected")]);
        Diagram cycle = CreateDiagram(
            [new DiagramNode("a", "A"), new DiagramNode("b", "B")],
            [
                new DiagramEdge("a", "b", "forward"),
                new DiagramEdge("b", "a", "return"),
                new DiagramEdge("a", "a", "self"),
                new DiagramEdge("a", "b", "parallel"),
            ]);

        OblivionResolvedDiagram singleResolved = OblivionNativeDiagramLayout.Resolve(single);
        OblivionResolvedDiagram disconnectedResolved = OblivionNativeDiagramLayout.Resolve(disconnected);
        OblivionResolvedDiagram cycleResolved = OblivionNativeDiagramLayout.Resolve(cycle);

        Assert.Single(singleResolved.Nodes);
        Assert.Equal(2, disconnectedResolved.Metrics!.ComponentCount);
        Assert.Equal(4, cycleResolved.Edges.Count);
        Assert.Contains(cycleResolved.Edges, edge => edge.RouteKind == "self-loop");
        Assert.Contains(cycleResolved.Edges, edge => edge.RouteKind == "back-edge");
        Assert.Equal(2, cycleResolved.Edges.Count(edge => edge.From == "a" && edge.To == "b"));
        Assert.Throws<InvalidOperationException>(() => OblivionNativeDiagramLayout.Resolve(
            CreateDiagram([], [])));

        DiagramNode[] tooManyNodes = Enumerable.Range(0, OblivionNativeDiagramLayout.MaximumNodes + 1)
            .Select(index => new DiagramNode("n" + index, "Node " + index))
            .ToArray();
        Assert.Throws<InvalidOperationException>(() => OblivionNativeDiagramLayout.Resolve(
            CreateDiagram(tooManyNodes, [])));
    }

    [Fact]
    public void Automatic_layering_honors_top_down_orientation_and_emits_directional_arrows()
    {
        Diagram diagram = CreateDiagram(
            [
                new DiagramNode("root", "Root"),
                new DiagramNode("left", "Left"),
                new DiagramNode("right", "Right"),
                new DiagramNode("join", "Join"),
            ],
            [
                new DiagramEdge("root", "left", "left"),
                new DiagramEdge("root", "right", "right"),
                new DiagramEdge("left", "join", "joins"),
                new DiagramEdge("right", "join", "joins"),
            ],
            DiagramDirection.TopDown);

        OblivionResolvedDiagram resolved = OblivionNativeDiagramLayout.Resolve(diagram);
        string svg = OblivionNativeDiagramSvgEmitter.Emit(
            resolved,
            OblivionResolvedAppearance.Light,
            "Top-down diamond");
        OblivionResolvedDiagramNode root = resolved.Nodes.Single(node => node.Id == "root");
        OblivionResolvedDiagramNode join = resolved.Nodes.Single(node => node.Id == "join");

        Assert.True(join.Y > root.Y);
        Assert.Equal(4, resolved.Edges.Count);
        Assert.Contains("<marker id=\"diagram-arrow\"", svg, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(svg, "marker-end=\"url(#diagram-arrow)\""));
        Assert.All(diagram.Edges, edge => Assert.Contains($"<title>{edge.Label}</title>", svg));
    }

    [Fact]
    public void Graph_c_layout_emission_and_cache_remain_comfortably_interactive()
    {
        (Diagram graphC, string fingerprint) = Project(M20dRoot);
        Stopwatch layoutTimer = Stopwatch.StartNew();
        OblivionResolvedDiagram resolved = OblivionNativeDiagramLayout.Resolve(graphC);
        layoutTimer.Stop();
        Stopwatch emissionTimer = Stopwatch.StartNew();
        string svg = OblivionNativeDiagramSvgEmitter.Emit(
            resolved,
            OblivionResolvedAppearance.Dark,
            "Graph C automatic layout");
        emissionTimer.Stop();
        string output = CreateTemporaryDirectory();
        try
        {
            OblivionNativeSvgRenderer renderer = new(graphC, fingerprint);
            Stopwatch coldTimer = Stopwatch.StartNew();
            OblivionDiagramRenderResult cold = renderer.Render(
                Request(output, OblivionResolvedAppearance.Dark));
            coldTimer.Stop();
            Stopwatch hitTimer = Stopwatch.StartNew();
            OblivionDiagramRenderResult hit = renderer.Render(
                Request(output, OblivionResolvedAppearance.Dark));
            hitTimer.Stop();

            _output.WriteLine(
                "Graph C: layout={0:F3}ms; emit={1:F3}ms; cold={2:F3}ms; hit={3:F3}ms; svg={4} bytes",
                layoutTimer.Elapsed.TotalMilliseconds,
                emissionTimer.Elapsed.TotalMilliseconds,
                coldTimer.Elapsed.TotalMilliseconds,
                hitTimer.Elapsed.TotalMilliseconds,
                System.Text.Encoding.UTF8.GetByteCount(svg));
            Assert.True(cold.Succeeded);
            Assert.False(cold.CacheHit);
            Assert.True(hit.Succeeded);
            Assert.True(hit.CacheHit);
            Assert.True(layoutTimer.Elapsed < TimeSpan.FromSeconds(1));
            Assert.True(emissionTimer.Elapsed < TimeSpan.FromSeconds(1));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Cache_identity_covers_backend_layout_appearance_and_provenance()
    {
        (Diagram diagram, string fingerprint) = Project(M20cRoot);
        string output = CreateTemporaryDirectory();
        try
        {
            OblivionDiagramRenderRequest light = Request(output, OblivionResolvedAppearance.Light);
            OblivionDiagramRenderRequest dark = Request(output, OblivionResolvedAppearance.Dark);
            OblivionNativeSvgRenderer renderer = new(diagram, fingerprint);

            Stopwatch coldTimer = Stopwatch.StartNew();
            OblivionDiagramRenderResult lightCold = renderer.Render(light);
            coldTimer.Stop();
            Stopwatch hitTimer = Stopwatch.StartNew();
            OblivionDiagramRenderResult lightHit = renderer.Render(light);
            hitTimer.Stop();
            OblivionDiagramRenderResult darkCold = renderer.Render(dark);
            OblivionNativeLayoutPolicy changedPolicy = new(
                "branching-calls-v2-test",
                "branching-call-ownership");
            OblivionDiagramRenderResult changedLayout = new OblivionNativeSvgRenderer(
                diagram,
                fingerprint,
                changedPolicy).Render(light);

            _output.WriteLine(
                "Graph B native realization: cold={0:F3}ms; hit={1:F3}ms",
                coldTimer.Elapsed.TotalMilliseconds,
                hitTimer.Elapsed.TotalMilliseconds);

            Assert.True(lightCold.Succeeded);
            Assert.False(lightCold.CacheHit);
            Assert.True(lightHit.CacheHit);
            Assert.False(darkCold.CacheHit);
            Assert.NotEqual(lightCold.CacheKey, darkCold.CacheKey);
            Assert.NotEqual(lightCold.CacheKey, changedLayout.CacheKey);
            Assert.Equal(OblivionDiagramRendererKind.NativeSvg, lightCold.RendererKind);
            Assert.Equal("NativeSvg", lightCold.Provenance!.SourceKind);
            Assert.Equal(OblivionNativeSvgRenderer.RendererId, lightCold.Provenance.RendererId);
            Assert.Equal(OblivionNativeDiagramPolicies.BranchingCallsV1, lightCold.LayoutPolicyIdentity);
            Assert.Equal(OblivionResolvedAppearance.Light, lightCold.Provenance.ResolvedAppearance);
            Assert.True(lightCold.Provenance.Derived);
            Assert.True(File.Exists(lightCold.RenderedPath));
            Assert.True(File.Exists(Path.ChangeExtension(lightCold.RenderedPath, ".json")));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_cache_is_diagnosed_and_rebuilt()
    {
        (Diagram diagram, string fingerprint) = Project(M20cRoot);
        string output = CreateTemporaryDirectory();
        try
        {
            OblivionNativeSvgRenderer renderer = new(diagram, fingerprint);
            OblivionDiagramRenderRequest request = Request(output, OblivionResolvedAppearance.Light);
            OblivionDiagramRenderResult first = renderer.Render(request);
            File.WriteAllText(first.RenderedPath!, "not svg");

            OblivionDiagramRenderResult rebuilt = renderer.Render(request);

            Assert.True(rebuilt.Succeeded);
            Assert.False(rebuilt.CacheHit);
            Assert.Contains(
                rebuilt.Diagnostics,
                diagnostic => diagnostic.Code == "OBLIVION-NATIVE-CACHE-INVALID");
            Assert.StartsWith("<svg ", File.ReadAllText(rebuilt.RenderedPath!));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Unsupported_topology_is_visible_and_mermaid_fallback_reports_actual_backend()
    {
        (Diagram diagram, _) = Project(M20cRoot);
        string output = CreateTemporaryDirectory();
        try
        {
            IOblivionDiagramRenderer renderer = new OblivionFallbackDiagramRenderer(
                new AlwaysFailRenderer(),
                new SuccessfulMermaidRenderer());

            OblivionDiagramRenderResult result = renderer.Render(
                Request(output, OblivionResolvedAppearance.Light));

            Assert.True(result.Succeeded);
            Assert.Equal(OblivionDiagramRendererKind.Mermaid, result.RendererKind);
            Assert.Equal("Mermaid", result.Provenance!.SourceKind);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code == "OBLIVION-NATIVE-FALLBACK-MERMAID");
            Assert.Null(result.ResolvedDiagram);
            Assert.NotNull(diagram);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static (Diagram Diagram, string Fingerprint) Project(string root)
    {
        OblivionWorkspaceLoadResult load = OblivionApplication.LoadVault(root);
        Assert.True(load.Succeeded, string.Join(Environment.NewLine, load.Diagnostics));
        OblivionCard card = Assert.Single(
            load.Workspace!.Pages.Single().Cards,
            candidate => candidate.Kind == OblivionCardKind.Diagram);
        OblivionDiagramCardRealizer realizer = new();
        OblivionDiagramSemanticProjectionResult semantic = realizer.ProjectSemanticDiagram(card, root);
        OblivionDiagramProjectionResult projection = realizer.Project(card, root);
        Assert.True(semantic.Succeeded, string.Join(Environment.NewLine, semantic.Diagnostics));
        Assert.True(projection.Succeeded, string.Join(Environment.NewLine, projection.Diagnostics));
        return (semantic.Diagram!, projection.SemanticFingerprint!);
    }

    private static OblivionDiagramRenderRequest Request(
        string output,
        OblivionResolvedAppearance appearance)
    {
        return new OblivionDiagramRenderRequest(
            "m20c.graph-b",
            "flowchart LR\n",
            "source/NativeDiagramRealizationCalls.ts",
            output,
            appearance,
            "m20c-call-ownership",
            "ownership-review",
            "realization-call-ownership");
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "oblivion-m20c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static string M20aRoot => Path.Combine(FixturesRoot, "M20aDenseDiagram.oblivion");
    private static string M20cRoot => Path.Combine(FixturesRoot, "M20cCallOwnership.oblivion");
    private static string M20dRoot => Path.Combine(FixturesRoot, "M20dLayeredArchitecture.oblivion");

    private static Diagram CreateDiagram(
        IEnumerable<DiagramNode> nodes,
        IEnumerable<DiagramEdge> edges,
        DiagramDirection direction = DiagramDirection.LeftRight)
    {
        Assert.True(Diagram.TryCreate(
            nodes,
            edges,
            direction,
            new DiagramProvenance("m20d-test", null),
            out Diagram? diagram,
            out IReadOnlyList<Copeland.TS.Diagnostics.Diagnostic> diagnostics),
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)));
        return diagram!;
    }

    private static int CountOccurrences(string value, string pattern)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(pattern, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += pattern.Length;
        }
        return count;
    }

    private sealed class AlwaysFailRenderer : IOblivionDiagramRenderer
    {
        public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
        {
            return new OblivionDiagramRenderResult(
                false,
                OblivionNativeSvgRenderer.RendererId,
                OblivionNativeSvgRenderer.RendererVersion,
                "semantic-hash",
                null,
                null,
                [new OblivionCardDiagnostic(
                    "OBLIVION-NATIVE-UNSUPPORTED-TOPOLOGY",
                    OblivionDiagnosticSeverity.Warning,
                    "Topology is outside the qualified native bound.",
                    request.SourceReference)],
                RendererKind: OblivionDiagramRendererKind.NativeSvg);
        }
    }

    private sealed class SuccessfulMermaidRenderer : IOblivionDiagramRenderer
    {
        public OblivionDiagramRenderResult Render(OblivionDiagramRenderRequest request)
        {
            string path = Path.Combine(request.OutputDirectory, "fallback.png");
            return new OblivionDiagramRenderResult(
                true,
                OblivionMermaidRendererOptions.RendererId,
                OblivionMermaidRendererOptions.PinnedVersion,
                "mermaid-hash",
                path,
                "image/png",
                [],
                "mermaid-cache-key",
                false,
                new OblivionDiagramProvenance(
                    "Mermaid",
                    "mermaid-hash",
                    OblivionMermaidRendererOptions.RendererId,
                    OblivionMermaidRendererOptions.PinnedVersion,
                    "render-mermaid-png",
                    "png",
                    request.Appearance,
                    "test",
                    "test",
                    request.WorkspaceId,
                    request.PageId,
                    request.CardId,
                    request.ContentId,
                    request.SourceReference,
                    true));
        }
    }
}
