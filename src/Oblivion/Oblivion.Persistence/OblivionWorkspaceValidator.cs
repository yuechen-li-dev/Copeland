using Oblivion.Model;

namespace Oblivion.Persistence;

public static class OblivionWorkspaceValidator
{
    public const int SupportedFormat = 1;
    public const string WorkspaceKind = "oblivion-workspace";
    public const string PageKind = "page";
    public const string CardKind = "card";
    public const string ArtifactKind = "artifact";

    public static IReadOnlyList<OblivionWorkspaceDiagnostic> ValidateManifest(
        OblivionWorkspaceManifest manifest,
        string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        List<OblivionWorkspaceDiagnostic> diagnostics = [];

        if (manifest.Format != SupportedFormat)
        {
            diagnostics.Add(Error("unsupported-format", $"Workspace format '{manifest.Format}' is not supported. Expected format {SupportedFormat}.", sourcePath));
        }

        if (!string.Equals(manifest.Kind, WorkspaceKind, StringComparison.Ordinal))
        {
            diagnostics.Add(Error("unknown-workspace-kind", $"Workspace kind '{manifest.Kind}' is not supported. Expected '{WorkspaceKind}'.", sourcePath));
        }

        if (string.IsNullOrWhiteSpace(manifest.WorkspaceId))
        {
            diagnostics.Add(Error("missing-workspace-id", "Workspace id is required.", sourcePath));
        }

        if (string.IsNullOrWhiteSpace(manifest.Title))
        {
            diagnostics.Add(Error("missing-workspace-title", "Workspace title is required.", sourcePath));
        }

        var sectionIds = new HashSet<string>(StringComparer.Ordinal);
        var pageIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (OblivionWorkspaceSectionManifest section in manifest.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Id))
            {
                diagnostics.Add(Error("missing-section-id", "Section id is required.", sourcePath));
            }
            else if (!sectionIds.Add(section.Id))
            {
                diagnostics.Add(Error("duplicate-section-id", $"Section id '{section.Id}' appears more than once.", sourcePath));
            }

            if (string.IsNullOrWhiteSpace(section.Title))
            {
                diagnostics.Add(Error("missing-section-title", $"Section '{section.Id}' is missing a title.", sourcePath));
            }

            foreach (OblivionWorkspacePageManifest page in section.Pages)
            {
                if (string.IsNullOrWhiteSpace(page.Id))
                {
                    diagnostics.Add(Error("missing-page-id", $"Section '{section.Id}' contains a page without an id.", sourcePath));
                    continue;
                }

                if (!pageIds.Add(page.Id))
                {
                    diagnostics.Add(Error("duplicate-page-id", $"Page id '{page.Id}' appears more than once.", sourcePath));
                }

                if (string.IsNullOrWhiteSpace(page.Title))
                {
                    diagnostics.Add(Error("missing-page-title", $"Page '{page.Id}' is missing a title.", sourcePath));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.DefaultPageId) &&
            !pageIds.Contains(manifest.DefaultPageId))
        {
            diagnostics.Add(Error("unknown-default-page-id", $"Default page id '{manifest.DefaultPageId}' was not found in the manifest.", sourcePath));
        }

        return OrderDiagnostics(diagnostics);
    }

    public static bool TryParseCardKind(
        string value,
        out OblivionCardKind cardKind)
    {
        return CardKindsByValue.TryGetValue(value, out cardKind);
    }

    public static bool TryParseCardStatus(
        string value,
        out OblivionCardStatus status)
    {
        return StatusByValue.TryGetValue(value, out status);
    }

    public static string GetCardKindValue(OblivionCardKind kind)
    {
        return kind switch
        {
            OblivionCardKind.Note => "note",
            OblivionCardKind.Status => "status",
            OblivionCardKind.UiPreview => "ui-preview",
            OblivionCardKind.Artifact => "artifact",
            OblivionCardKind.CodeFact => "code-fact",
            OblivionCardKind.CodeTheory => "code-theory",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Oblivion card kind."),
        };
    }

    public static string GetCardStatusValue(OblivionCardStatus status)
    {
        return status switch
        {
            OblivionCardStatus.Idle => "idle",
            OblivionCardStatus.Passing => "passing",
            OblivionCardStatus.Failing => "failing",
            OblivionCardStatus.Warning => "warning",
            OblivionCardStatus.Deferred => "deferred",
            OblivionCardStatus.Placeholder => "placeholder",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown Oblivion card status."),
        };
    }

    public static IReadOnlyList<OblivionWorkspaceDiagnostic> OrderDiagnostics(IEnumerable<OblivionWorkspaceDiagnostic> diagnostics)
    {
        return diagnostics
            .OrderBy(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.SourcePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
    }

    public static OblivionWorkspaceDiagnostic Error(string code, string message, string? sourcePath)
    {
        return new OblivionWorkspaceDiagnostic(
            OblivionWorkspaceDiagnosticSeverity.Error,
            code,
            message,
            sourcePath);
    }

    private static readonly IReadOnlyDictionary<string, OblivionCardKind> CardKindsByValue =
        new Dictionary<string, OblivionCardKind>(StringComparer.Ordinal)
        {
            ["note"] = OblivionCardKind.Note,
            ["status"] = OblivionCardKind.Status,
            ["ui-preview"] = OblivionCardKind.UiPreview,
            ["artifact"] = OblivionCardKind.Artifact,
            ["code-fact"] = OblivionCardKind.CodeFact,
            ["code-theory"] = OblivionCardKind.CodeTheory,
        };

    private static readonly IReadOnlyDictionary<string, OblivionCardStatus> StatusByValue =
        new Dictionary<string, OblivionCardStatus>(StringComparer.Ordinal)
        {
            ["idle"] = OblivionCardStatus.Idle,
            ["passing"] = OblivionCardStatus.Passing,
            ["failing"] = OblivionCardStatus.Failing,
            ["warning"] = OblivionCardStatus.Warning,
            ["deferred"] = OblivionCardStatus.Deferred,
            ["placeholder"] = OblivionCardStatus.Placeholder,
        };
}
