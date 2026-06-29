using System.Text;
using Tomlyn;
using Tomlyn.Model;
using static Machina.Presenter.Sample.OblivionCardTomlReaderInternal;
using static Machina.Presenter.Sample.OblivionTomlHelpers;

namespace Machina.Presenter.Sample;

public static class OblivionPageTomlReader
{
    public static OblivionPageTomlReadResult Read(string toml, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        if (!TryParseTomlTable(toml, sourcePath, out TomlTable? table, out IReadOnlyList<OblivionWorkspaceDiagnostic> parseDiagnostics))
        {
            return new OblivionPageTomlReadResult(null, parseDiagnostics);
        }

        TomlTable parsedTable = table!;
        List<OblivionWorkspaceDiagnostic> diagnostics = [];
        int format = ReadRequiredInt(parsedTable, "format", sourcePath, diagnostics);
        string kind = ReadRequiredString(parsedTable, "kind", sourcePath, diagnostics);
        string id = ReadRequiredString(parsedTable, "id", sourcePath, diagnostics);
        string title = ReadRequiredString(parsedTable, "title", sourcePath, diagnostics);
        string? description = ReadOptionalString(parsedTable, "description");
        IReadOnlyList<string> tags = ReadStringArray(parsedTable, "tags", sourcePath, diagnostics);

        if (format != OblivionWorkspaceValidator.SupportedFormat)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unsupported-format", $"Page format '{format}' is not supported. Expected format {OblivionWorkspaceValidator.SupportedFormat}.", sourcePath));
        }

        if (!string.Equals(kind, OblivionWorkspaceValidator.PageKind, StringComparison.Ordinal))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unknown-page-kind", $"Page kind '{kind}' is not supported. Expected '{OblivionWorkspaceValidator.PageKind}'.", sourcePath));
        }

        OblivionPageAssetDocument? document = diagnostics.Any(diagnostic => diagnostic.Severity == OblivionWorkspaceDiagnosticSeverity.Error)
            ? null
            : new OblivionPageAssetDocument(format, kind, id, title, description, tags);

        return new OblivionPageTomlReadResult(document, OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
    }
}

public static class OblivionPageTomlWriter
{
    public static string Write(OblivionPageAssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        StringBuilder builder = new();
        builder.AppendLine($"format = {document.Format}");
        builder.AppendLine($"kind = \"{Escape(document.Kind)}\"");
        builder.AppendLine($"id = \"{Escape(document.Id)}\"");
        builder.AppendLine($"title = \"{Escape(document.Title)}\"");

        if (!string.IsNullOrWhiteSpace(document.Description))
        {
            builder.AppendLine($"description = \"{Escape(document.Description!)}\"");
        }

        AppendStringArray(builder, "tags", document.Tags);
        return builder.ToString();
    }
}

public static class OblivionCardTomlReader
{
    public static OblivionCardTomlReadResult Read(string toml, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        if (!TryParseTomlTable(toml, sourcePath, out TomlTable? table, out IReadOnlyList<OblivionWorkspaceDiagnostic> parseDiagnostics))
        {
            return new OblivionCardTomlReadResult(null, parseDiagnostics);
        }

        TomlTable parsedTable = table!;
        List<OblivionWorkspaceDiagnostic> diagnostics = [];
        int format = ReadRequiredInt(parsedTable, "format", sourcePath, diagnostics);
        string kind = ReadRequiredString(parsedTable, "kind", sourcePath, diagnostics);
        string id = ReadRequiredString(parsedTable, "id", sourcePath, diagnostics);
        string cardKind = ReadRequiredString(parsedTable, "card_kind", sourcePath, diagnostics);
        string status = ReadRequiredString(parsedTable, "status", sourcePath, diagnostics);
        string title = ReadRequiredString(parsedTable, "title", sourcePath, diagnostics);
        string? subtitle = ReadOptionalString(parsedTable, "subtitle");
        IReadOnlyList<string> tags = ReadStringArray(parsedTable, "tags", sourcePath, diagnostics);

        TomlTable? bodyTable = ReadRequiredTable(parsedTable, "body", sourcePath, diagnostics);
        string bodyFormat = bodyTable is null ? string.Empty : ReadRequiredString(bodyTable, "format", sourcePath, diagnostics);
        string? bodyText = bodyTable is null ? null : ReadOptionalString(bodyTable, "text");
        string? bodyPath = bodyTable is null ? null : ReadOptionalString(bodyTable, "path");

        IReadOnlyList<OblivionCardActionDocument> actions = ReadActions(parsedTable, sourcePath, diagnostics);
        IReadOnlyList<OblivionCardArtifactDocument> artifacts = ReadArtifacts(parsedTable, sourcePath, diagnostics);

        if (format != OblivionWorkspaceValidator.SupportedFormat)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unsupported-format", $"Card format '{format}' is not supported. Expected format {OblivionWorkspaceValidator.SupportedFormat}.", sourcePath));
        }

        if (!string.Equals(kind, OblivionWorkspaceValidator.CardKind, StringComparison.Ordinal))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unknown-card-document-kind", $"Card kind '{kind}' is not supported. Expected '{OblivionWorkspaceValidator.CardKind}'.", sourcePath));
        }

        if (!OblivionWorkspaceValidator.TryParseCardKind(cardKind, out _))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unknown-card-kind", $"Card kind '{cardKind}' is not supported.", sourcePath));
        }

        if (!OblivionWorkspaceValidator.TryParseCardStatus(status, out _))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unknown-card-status", $"Card status '{status}' is not supported.", sourcePath));
        }

        bool isPlainBody = string.Equals(bodyFormat, "plain", StringComparison.Ordinal);
        bool isMarkdownBody = string.Equals(bodyFormat, "copeland-markdown", StringComparison.Ordinal);

        if (!isPlainBody && !isMarkdownBody)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unknown-body-format", $"Body format '{bodyFormat}' is not supported. Expected 'plain' or 'copeland-markdown'.", sourcePath));
        }

        if (isPlainBody)
        {
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                diagnostics.Add(OblivionWorkspaceValidator.Error("missing-required-field", "Field 'body.text' is required for plain card bodies.", sourcePath));
            }

            if (!string.IsNullOrWhiteSpace(bodyPath))
            {
                diagnostics.Add(OblivionWorkspaceValidator.Error("plain-body-path-not-supported", "Field 'body.path' is not supported for plain card bodies.", sourcePath));
            }
        }

        if (isMarkdownBody)
        {
            bool hasText = !string.IsNullOrWhiteSpace(bodyText);
            bool hasPath = !string.IsNullOrWhiteSpace(bodyPath);

            if (hasText == hasPath)
            {
                diagnostics.Add(OblivionWorkspaceValidator.Error("invalid-markdown-body-source", "Markdown card bodies must provide exactly one of 'body.text' or 'body.path'.", sourcePath));
            }

            if (!string.IsNullOrWhiteSpace(bodyPath))
            {
                if (Path.IsPathRooted(bodyPath))
                {
                    diagnostics.Add(OblivionWorkspaceValidator.Error("absolute-path-not-allowed", $"The markdown body path '{bodyPath}' must be relative to the workspace root.", sourcePath));
                }

                if (LooksLikePathTraversal(bodyPath))
                {
                    diagnostics.Add(OblivionWorkspaceValidator.Error("path-traversal-not-allowed", $"The markdown body path '{bodyPath}' escapes the workspace root.", sourcePath));
                }
            }
        }

        OblivionCardAssetDocument? document = diagnostics.Any(diagnostic => diagnostic.Severity == OblivionWorkspaceDiagnosticSeverity.Error)
            ? null
            : new OblivionCardAssetDocument(
                format,
                kind,
                id,
                cardKind,
                status,
                title,
                subtitle,
                tags,
                new OblivionCardBodyDocument(bodyFormat, bodyText, bodyPath),
                actions,
                artifacts);

        return new OblivionCardTomlReadResult(document, OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
    }
}

public static class OblivionCardTomlWriter
{
    public static string Write(OblivionCardAssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        StringBuilder builder = new();
        builder.AppendLine($"format = {document.Format}");
        builder.AppendLine($"kind = \"{Escape(document.Kind)}\"");
        builder.AppendLine($"id = \"{Escape(document.Id)}\"");
        builder.AppendLine($"card_kind = \"{Escape(document.CardKind)}\"");
        builder.AppendLine($"status = \"{Escape(document.Status)}\"");
        builder.AppendLine($"title = \"{Escape(document.Title)}\"");

        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            builder.AppendLine($"subtitle = \"{Escape(document.Subtitle!)}\"");
        }

        AppendStringArray(builder, "tags", document.Tags);
        builder.AppendLine();
        builder.AppendLine("[body]");
        builder.AppendLine($"format = \"{Escape(document.Body.Format)}\"");

        if (!string.IsNullOrWhiteSpace(document.Body.Path))
        {
            builder.AppendLine($"path = \"{Escape(document.Body.Path!)}\"");
        }
        else
        {
            builder.AppendLine("text = \"\"\"");
            string normalizedBodyText = NormalizeMultiline(document.Body.Text ?? string.Empty);
            builder.Append(normalizedBodyText);
            if (!normalizedBodyText.EndsWith('\n'))
            {
                builder.AppendLine();
            }
            builder.AppendLine("\"\"\"");
        }

        foreach (OblivionCardActionDocument action in document.Actions)
        {
            builder.AppendLine();
            builder.AppendLine("[[actions]]");
            builder.AppendLine($"id = \"{Escape(action.Id)}\"");
            builder.AppendLine($"label = \"{Escape(action.Label)}\"");
            builder.AppendLine($"enabled = {action.Enabled.ToString().ToLowerInvariant()}");
        }

        foreach (OblivionCardArtifactDocument artifact in document.Artifacts)
        {
            builder.AppendLine();
            builder.AppendLine("[[artifacts]]");
            builder.AppendLine($"id = \"{Escape(artifact.Id)}\"");
            builder.AppendLine($"label = \"{Escape(artifact.Label)}\"");
            builder.AppendLine($"kind = \"{Escape(artifact.Kind)}\"");

            if (!string.IsNullOrWhiteSpace(artifact.Path))
            {
                builder.AppendLine($"path = \"{Escape(artifact.Path!)}\"");
            }

            if (artifact.Generated)
            {
                builder.AppendLine("generated = true");
            }

            if (!string.IsNullOrWhiteSpace(artifact.Asset))
            {
                builder.AppendLine($"asset = \"{Escape(artifact.Asset!)}\"");
            }
        }

        return builder.ToString();
    }
}

public static class OblivionArtifactTomlReader
{
    public static OblivionArtifactTomlReadResult Read(string toml, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        if (!TryParseTomlTable(toml, sourcePath, out TomlTable? table, out IReadOnlyList<OblivionWorkspaceDiagnostic> parseDiagnostics))
        {
            return new OblivionArtifactTomlReadResult(null, parseDiagnostics);
        }

        TomlTable parsedTable = table!;
        List<OblivionWorkspaceDiagnostic> diagnostics = [];
        int format = ReadRequiredInt(parsedTable, "format", sourcePath, diagnostics);
        string kind = ReadRequiredString(parsedTable, "kind", sourcePath, diagnostics);
        string id = ReadRequiredString(parsedTable, "id", sourcePath, diagnostics);
        string label = ReadRequiredString(parsedTable, "label", sourcePath, diagnostics);
        string artifactKind = ReadRequiredString(parsedTable, "artifact_kind", sourcePath, diagnostics);
        string? path = ReadOptionalString(parsedTable, "path");
        bool generated = ReadOptionalBool(parsedTable, "generated");

        if (format != OblivionWorkspaceValidator.SupportedFormat)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unsupported-format", $"Artifact format '{format}' is not supported. Expected format {OblivionWorkspaceValidator.SupportedFormat}.", sourcePath));
        }

        if (!string.Equals(kind, OblivionWorkspaceValidator.ArtifactKind, StringComparison.Ordinal))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("unknown-artifact-kind", $"Artifact kind '{kind}' is not supported. Expected '{OblivionWorkspaceValidator.ArtifactKind}'.", sourcePath));
        }

        OblivionArtifactAssetDocument? document = diagnostics.Any(diagnostic => diagnostic.Severity == OblivionWorkspaceDiagnosticSeverity.Error)
            ? null
            : new OblivionArtifactAssetDocument(format, kind, id, label, artifactKind, path, generated);

        return new OblivionArtifactTomlReadResult(document, OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
    }
}

public static class OblivionArtifactTomlWriter
{
    public static string Write(OblivionArtifactAssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        StringBuilder builder = new();
        builder.AppendLine($"format = {document.Format}");
        builder.AppendLine($"kind = \"{Escape(document.Kind)}\"");
        builder.AppendLine($"id = \"{Escape(document.Id)}\"");
        builder.AppendLine($"label = \"{Escape(document.Label)}\"");
        builder.AppendLine($"artifact_kind = \"{Escape(document.ArtifactKind)}\"");

        if (!string.IsNullOrWhiteSpace(document.Path))
        {
            builder.AppendLine($"path = \"{Escape(document.Path!)}\"");
        }

        builder.AppendLine($"generated = {document.Generated.ToString().ToLowerInvariant()}");
        return builder.ToString();
    }
}

internal static class OblivionTomlHelpers
{
    public static bool TryParseTomlTable(
        string toml,
        string? sourcePath,
        out TomlTable? table,
        out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        try
        {
            table = Tomlyn.TomlSerializer.Deserialize<TomlTable>(toml, Tomlyn.TomlSerializerOptions.Default);
            if (table is null)
            {
                diagnostics = [OblivionWorkspaceValidator.Error("toml-deserialize-failed", "TOML document could not be read as a table.", sourcePath)];
                return false;
            }

            diagnostics = [];
            return true;
        }
        catch (Exception ex)
        {
            table = null;
            diagnostics = [OblivionWorkspaceValidator.Error("toml-parse-failed", ex.Message, sourcePath)];
            return false;
        }
    }

    public static int ReadRequiredInt(
        TomlTable table,
        string key,
        string? sourcePath,
        List<OblivionWorkspaceDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(key, out object? value) || value is null)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("missing-required-field", $"Field '{key}' is required.", sourcePath));
            return 0;
        }

        if (value is long longValue)
        {
            return checked((int)longValue);
        }

        if (value is int intValue)
        {
            return intValue;
        }

        diagnostics.Add(OblivionWorkspaceValidator.Error("invalid-field-type", $"Field '{key}' must be an integer.", sourcePath));
        return 0;
    }

    public static string ReadRequiredString(
        TomlTable table,
        string key,
        string? sourcePath,
        List<OblivionWorkspaceDiagnostic> diagnostics)
    {
        string? value = ReadOptionalString(table, key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        diagnostics.Add(OblivionWorkspaceValidator.Error("missing-required-field", $"Field '{key}' is required.", sourcePath));
        return string.Empty;
    }

    public static string? ReadOptionalString(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out object? value) || value is null)
        {
            return null;
        }

        return value as string;
    }

    public static bool ReadOptionalBool(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out object? value) || value is null)
        {
            return false;
        }

        return value is bool boolValue && boolValue;
    }

    public static IReadOnlyList<string> ReadStringArray(
        TomlTable table,
        string key,
        string? sourcePath,
        List<OblivionWorkspaceDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(key, out object? value) || value is null)
        {
            return [];
        }

        if (value is not TomlArray array)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("invalid-field-type", $"Field '{key}' must be an array of strings.", sourcePath));
            return [];
        }

        List<string> items = [];
        foreach (object? item in array)
        {
            if (item is string text)
            {
                items.Add(text);
                continue;
            }

            diagnostics.Add(OblivionWorkspaceValidator.Error("invalid-field-type", $"Field '{key}' must contain only strings.", sourcePath));
            return [];
        }

        return items;
    }

    public static TomlTable? ReadRequiredTable(
        TomlTable table,
        string key,
        string? sourcePath,
        List<OblivionWorkspaceDiagnostic> diagnostics)
    {
        if (!table.TryGetValue(key, out object? value) || value is null)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("missing-required-field", $"Table '{key}' is required.", sourcePath));
            return null;
        }

        if (value is TomlTable tableValue)
        {
            return tableValue;
        }

        diagnostics.Add(OblivionWorkspaceValidator.Error("invalid-field-type", $"Field '{key}' must be a table.", sourcePath));
        return null;
    }

    public static string Escape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    public static string NormalizeMultiline(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    public static void AppendStringArray(StringBuilder builder, string key, IReadOnlyList<string> values)
    {
        builder.Append(key);
        builder.Append(" = [");
        builder.Append(string.Join(", ", values.Select(value => $"\"{Escape(value)}\"")));
        builder.AppendLine("]");
    }

    public static bool LooksLikePathTraversal(string path)
    {
        string normalized = path.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));
    }
}

internal static class OblivionCardTomlReaderInternal
{
    public static IReadOnlyList<OblivionCardActionDocument> ReadActions(
        TomlTable table,
        string? sourcePath,
        List<OblivionWorkspaceDiagnostic> diagnostics)
    {
        if (!table.TryGetValue("actions", out object? value) || value is null)
        {
            return [];
        }

        if (value is not TomlTableArray array)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("invalid-field-type", "Field 'actions' must be a table array.", sourcePath));
            return [];
        }

        List<OblivionCardActionDocument> actions = [];
        foreach (TomlTable item in array.OfType<TomlTable>())
        {
            string id = OblivionTomlHelpers.ReadRequiredString(item, "id", sourcePath, diagnostics);
            string label = OblivionTomlHelpers.ReadRequiredString(item, "label", sourcePath, diagnostics);
            bool enabled = OblivionTomlHelpers.ReadOptionalBool(item, "enabled");
            actions.Add(new OblivionCardActionDocument(id, label, enabled));
        }

        return actions;
    }

    public static IReadOnlyList<OblivionCardArtifactDocument> ReadArtifacts(
        TomlTable table,
        string? sourcePath,
        List<OblivionWorkspaceDiagnostic> diagnostics)
    {
        if (!table.TryGetValue("artifacts", out object? value) || value is null)
        {
            return [];
        }

        if (value is not TomlTableArray array)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("invalid-field-type", "Field 'artifacts' must be a table array.", sourcePath));
            return [];
        }

        List<OblivionCardArtifactDocument> artifacts = [];
        foreach (TomlTable item in array.OfType<TomlTable>())
        {
            string id = OblivionTomlHelpers.ReadRequiredString(item, "id", sourcePath, diagnostics);
            string label = OblivionTomlHelpers.ReadRequiredString(item, "label", sourcePath, diagnostics);
            string kind = OblivionTomlHelpers.ReadRequiredString(item, "kind", sourcePath, diagnostics);
            string? path = OblivionTomlHelpers.ReadOptionalString(item, "path");
            bool generated = OblivionTomlHelpers.ReadOptionalBool(item, "generated");
            string? asset = OblivionTomlHelpers.ReadOptionalString(item, "asset");
            artifacts.Add(new OblivionCardArtifactDocument(id, label, kind, path, generated, asset));
        }

        return artifacts;
    }
}
