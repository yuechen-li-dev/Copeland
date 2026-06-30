namespace Machina.Presenter.Sample;

public sealed class OblivionCardEffectRouter
{
    public OblivionCardEffectResult Route(OblivionCardEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind switch
        {
            OblivionCardEffectKind.RefreshMarkdown => CreateDeferredResult(
                request,
                "RefreshMarkdown deferred in M12f. Effect routing skeleton only.",
                "M12F-DEFERRED-REFRESH"),
            OblivionCardEffectKind.OpenSource => CreateDeferredResult(
                request,
                "OpenSource deferred in M12f. No file open occurs.",
                "M12F-DEFERRED-OPEN-SOURCE"),
            OblivionCardEffectKind.CopySourcePath => CreateDeferredResult(
                request,
                "CopySourcePath deferred in M12f. Clipboard interaction is not executed.",
                "M12F-DEFERRED-COPY-SOURCE"),
            OblivionCardEffectKind.OpenArtifact => CreateDeferredResult(
                request,
                "OpenArtifact deferred in M12f. No artifact open occurs.",
                "M12F-DEFERRED-OPEN-ARTIFACT"),
            OblivionCardEffectKind.RunCodeFact => CreateDeferredResult(
                request,
                "Code execution deferred to M13+. No Roslyn or xUnit execution occurred.",
                "M12F-DEFERRED-RUN-CODEFACT"),
            OblivionCardEffectKind.RunCodeTheory => CreateDeferredResult(
                request,
                "Theory execution deferred to M13+. No Roslyn or xUnit execution occurred.",
                "M12F-DEFERRED-RUN-CODETHEORY"),
            OblivionCardEffectKind.ExportCard => CreateDeferredResult(
                request,
                "ExportCard deferred in M12f. No artifact generation occurs.",
                "M12F-DEFERRED-EXPORT"),
            OblivionCardEffectKind.RenderPreview => CreateDeferredResult(
                request,
                "RenderPreview deferred in M12f. No preview renderer execution occurs.",
                "M12F-DEFERRED-RENDER-PREVIEW"),
            OblivionCardEffectKind.None => CreateDeferredResult(
                request,
                "No-op effect remains deferred in M12f.",
                "M12F-DEFERRED-NONE"),
            _ => CreateRejectedResult(
                request,
                "Unknown/custom effect kind rejected in M12f.",
                "M12F-REJECTED-UNKNOWN-EFFECT"),
        };
    }

    private static OblivionCardEffectResult CreateDeferredResult(
        OblivionCardEffectRequest request,
        string message,
        string diagnosticCode)
    {
        return new OblivionCardEffectResult(
            request.RequestId,
            request.CardId,
            request.Kind,
            OblivionCardEffectStatus.Deferred,
            message,
            [
                new OblivionCardDiagnostic(
                    diagnosticCode,
                    OblivionCardDiagnosticSeverity.Info,
                    message,
                    request.Properties.TryGetValue("sourcePath", out string? sourcePath) ? sourcePath : null),
            ],
            []);
    }

    private static OblivionCardEffectResult CreateRejectedResult(
        OblivionCardEffectRequest request,
        string message,
        string diagnosticCode)
    {
        return new OblivionCardEffectResult(
            request.RequestId,
            request.CardId,
            request.Kind,
            OblivionCardEffectStatus.Rejected,
            message,
            [
                new OblivionCardDiagnostic(
                    diagnosticCode,
                    OblivionCardDiagnosticSeverity.Warning,
                    message,
                    request.Properties.TryGetValue("sourcePath", out string? sourcePath) ? sourcePath : null),
            ],
            []);
    }
}
