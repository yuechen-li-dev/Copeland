using Copeland.TS.Compiler;
using Copeland.TS.Templates;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionDiagramProjectionResult(
    bool Succeeded,
    OblivionDiagramSource Source,
    string? MermaidSource,
    string? SemanticFingerprint,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics);

public sealed record OblivionDiagramCardRealizationResult(
    OblivionDiagramProjectionResult Projection,
    OblivionDiagramRenderResult? Render);

public sealed record OblivionDiagramSemanticProjectionResult(
    bool Succeeded,
    OblivionDiagramSource Source,
    Diagram? Diagram,
    string? SemanticIdentity,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics);

public sealed class OblivionDiagramCardRealizer
{
    public OblivionDiagramProjectionResult Project(
        OblivionCard card,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        OblivionDiagramSemanticProjectionResult semantic = ProjectSemanticDiagram(card, workspaceRoot);
        if (!semantic.Succeeded || semantic.Diagram is null || semantic.SemanticIdentity is null)
        {
            return new OblivionDiagramProjectionResult(
                false,
                semantic.Source,
                null,
                null,
                semantic.Diagnostics);
        }

        string mermaid = MermaidEmitter.Emit(semantic.Diagram);
        string fingerprint = OblivionMermaidHashing.ComputeSourceHash(
            semantic.SemanticIdentity + "\n" + mermaid);
        return new OblivionDiagramProjectionResult(true, semantic.Source, mermaid, fingerprint, []);
    }

    public OblivionDiagramSemanticProjectionResult ProjectSemanticDiagram(
        OblivionCard card,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        if (card.Kind != OblivionCardKind.Diagram || card.Diagram is null)
        {
            OblivionDiagramSource missingSource = card.Diagram ?? new OblivionDiagramSource(
                OblivionDiagramSourceKind.CopelandFlow,
                string.Empty,
                string.Empty,
                OblivionDiagramProjectionKind.State);
            return SemanticFailure(
                missingSource,
                "OBLIVION-DIAGRAM-SOURCE-MISSING",
                $"Card '{card.Id.Value}' has no semantic diagram source.",
                card.Provenance.SourceReference);
        }

        OblivionDiagramSource source = card.Diagram;
        if (source.Kind != OblivionDiagramSourceKind.CopelandFlow ||
            source.Projection != OblivionDiagramProjectionKind.State)
        {
            return SemanticFailure(
                source,
                "OBLIVION-DIAGRAM-PROJECTION-UNSUPPORTED",
                $"Diagram source '{source.Kind}' with projection '{source.Projection}' is not supported.",
                source.Reference);
        }

        string root = Path.GetFullPath(workspaceRoot);
        string path = Path.GetFullPath(Path.Combine(root, source.Reference));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return SemanticFailure(
                source,
                "OBLIVION-DIAGRAM-SOURCE-NOT-FOUND",
                $"Compiler diagram source '{source.Reference}' was not found inside the workspace.",
                source.Reference);
        }

        CopelandCompilation compilation = CopelandCompiler.CompileTemplates(
            File.ReadAllText(path),
            new CopelandCompilationOptions
            {
                SourcePath = path,
                ProjectRoot = root,
            });
        if (!compilation.Success || compilation.BoundCompilation is null)
        {
            return new OblivionDiagramSemanticProjectionResult(
                false,
                source,
                null,
                null,
                compilation.Diagnostics.Select(diagnostic => new OblivionCardDiagnostic(
                    diagnostic.Id,
                    OblivionDiagnosticSeverity.Error,
                    diagnostic.Message,
                    diagnostic.SourcePath ?? source.Reference)).ToArray());
        }

        if (!StateMachineDiagramProjection.TryProject(
                compilation.BoundCompilation.Program,
                source.Symbol,
                out StateMachineSemanticView? semanticView,
                out Diagram? diagram,
                out var diagnostics))
        {
            return new OblivionDiagramSemanticProjectionResult(
                false,
                source,
                null,
                null,
                diagnostics.Select(diagnostic => new OblivionCardDiagnostic(
                    diagnostic.Id,
                    OblivionDiagnosticSeverity.Error,
                    diagnostic.Message,
                    diagnostic.SourcePath ?? source.Reference)).ToArray());
        }

        return new OblivionDiagramSemanticProjectionResult(
            true,
            source,
            diagram,
            semanticView!.Identity,
            []);
    }

    public OblivionDiagramCardRealizationResult Realize(
        OblivionCard card,
        string workspaceRoot,
        IOblivionDiagramRenderer renderer,
        string outputDirectory,
        OblivionResolvedAppearance appearance = OblivionResolvedAppearance.Light)
    {
        OblivionDiagramProjectionResult projection = Project(card, workspaceRoot);
        if (!projection.Succeeded || projection.MermaidSource is null)
        {
            return new OblivionDiagramCardRealizationResult(projection, null);
        }

        OblivionDiagramRenderResult render = renderer.Render(new OblivionDiagramRenderRequest(
            card.Id.Value + ".diagram",
            projection.MermaidSource,
            projection.Source.Reference,
            outputDirectory,
            appearance,
            card.WorkspaceId?.Value,
            card.PageId?.Value,
            card.Id.Value));
        return new OblivionDiagramCardRealizationResult(projection, render);
    }

    private static OblivionDiagramProjectionResult Failure(
        OblivionDiagramSource source,
        string code,
        string message,
        string? sourceReference)
    {
        return new OblivionDiagramProjectionResult(
            false,
            source,
            null,
            null,
            [new OblivionCardDiagnostic(code, OblivionDiagnosticSeverity.Error, message, sourceReference)]);
    }

    private static OblivionDiagramSemanticProjectionResult SemanticFailure(
        OblivionDiagramSource source,
        string code,
        string message,
        string? sourceReference)
    {
        return new OblivionDiagramSemanticProjectionResult(
            false,
            source,
            null,
            null,
            [new OblivionCardDiagnostic(code, OblivionDiagnosticSeverity.Error, message, sourceReference)]);
    }
}
