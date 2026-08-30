using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.App;

public sealed class OblivionCardEffectRouter
{
    private readonly OblivionHostCapabilities _capabilities;

    public OblivionCardEffectRouter(OblivionHostCapabilities? capabilities = null)
    {
        _capabilities = capabilities ?? OblivionHostCapabilities.None;
    }

    public OblivionEffectResult Route(OblivionEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_capabilities.TryExecute(request, out OblivionEffectResult? result))
        {
            return result!;
        }

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

    private static OblivionEffectResult CreateDeferredResult(
        OblivionEffectRequest request,
        string message,
        string diagnosticCode)
    {
        List<OblivionCardDiagnostic> diagnostics =
        [
            new OblivionCardDiagnostic(
                diagnosticCode,
                OblivionDiagnosticSeverity.Info,
                message,
                request.Context.SourcePath),
        ];
        if (IsHostCapabilityRequest(request))
        {
            diagnostics.Add(
                new OblivionCardDiagnostic(
                    "OBLIVION-HOST-CAPABILITY-UNAVAILABLE",
                    OblivionDiagnosticSeverity.Info,
                    $"Host capability for '{request.GetType().Name}' is unavailable.",
                    request.Context.SourcePath));
        }

        return new DeferredEffectResult(
            request.RequestId,
            request.CardId,
            request.Kind,
            message,
            diagnostics,
            []);
    }

    private static bool IsHostCapabilityRequest(OblivionEffectRequest request)
    {
        return request is RefreshContentEffectRequest or
            OpenSourceEffectRequest or
            CopySourcePathEffectRequest or
            OpenArtifactEffectRequest or
            ExportCardEffectRequest or
            RenderPreviewEffectRequest;
    }

    private static OblivionEffectResult CreateRejectedResult(
        OblivionEffectRequest request,
        string message,
        string diagnosticCode)
    {
        return new RejectedEffectResult(
            request.RequestId,
            request.CardId,
            request.Kind,
            message,
            [
                new OblivionCardDiagnostic(
                    diagnosticCode,
                    OblivionDiagnosticSeverity.Warning,
                    message,
                    request.Context.SourcePath),
            ],
            []);
    }
}
