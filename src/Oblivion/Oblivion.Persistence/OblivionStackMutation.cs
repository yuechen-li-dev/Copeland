namespace Oblivion.Persistence;

public enum OblivionVaultNewlinePolicy
{
    Preserve,
    Lf,
    Crlf,
}

public sealed record OblivionStackMutationResult(
    string Operation,
    string WorkspaceId,
    string PageId,
    string CardId,
    int OldCount,
    int NewCount,
    string MetadataPath,
    string ContentPath,
    bool ContentDeleted,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics,
    OblivionWorkspaceLoadResult Workspace);

public static class OblivionStackMutation
{
    private const string ImportedMarkdownProducer = "oblivion.card.push";

    public static OblivionStackMutationResult? PushMarkdown(
        string vaultRoot,
        string sourcePath,
        string? requestedPageId,
        string? requestedCardId,
        string? requestedTitle,
        string? subtitle,
        out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        return PushMarkdown(
            vaultRoot,
            sourcePath,
            requestedPageId,
            requestedCardId,
            requestedTitle,
            subtitle,
            OblivionVaultNewlinePolicy.Preserve,
            out diagnostics);
    }

    public static OblivionStackMutationResult? PushMarkdown(
        string vaultRoot,
        string sourcePath,
        string? requestedPageId,
        string? requestedCardId,
        string? requestedTitle,
        string? subtitle,
        OblivionVaultNewlinePolicy newlinePolicy,
        out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        string root = Path.GetFullPath(vaultRoot);
        OblivionWorkspaceLoadResult original = OblivionWorkspaceLoader.OpenVault(root);
        if (!original.Succeeded || original.Workspace is null)
        {
            diagnostics = original.Diagnostics;
            return null;
        }

        if (!TryResolvePage(original, requestedPageId, out string pageId, out diagnostics))
        {
            return null;
        }

        string fullSourcePath = Path.GetFullPath(sourcePath);
        if (Directory.Exists(fullSourcePath))
        {
            diagnostics = [Error(
                "OBLIVION-CARD-IMPORT-SOURCE-INVALID",
                $"Markdown import source '{fullSourcePath}' is a directory, not a .md file.",
                fullSourcePath)];
            return null;
        }

        if (!File.Exists(fullSourcePath))
        {
            diagnostics = [Error(
                "OBLIVION-CARD-IMPORT-SOURCE-MISSING",
                $"Markdown import source '{fullSourcePath}' was not found.",
                fullSourcePath)];
            return null;
        }

        if (!string.Equals(Path.GetExtension(fullSourcePath), ".md", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics = [Error(
                "OBLIVION-CARD-IMPORT-SOURCE-INVALID",
                $"Markdown import source '{fullSourcePath}' must be a readable .md file.",
                fullSourcePath)];
            return null;
        }

        string cardId = requestedCardId is null
            ? DeriveCardId(Path.GetFileNameWithoutExtension(fullSourcePath))
            : requestedCardId;
        if (!OblivionStructuredVaultPaths.IsValidId(cardId) ||
            !string.Equals(cardId, cardId.ToLowerInvariant(), StringComparison.Ordinal))
        {
            diagnostics = [Error(
                "OBLIVION-CARD-IMPORT-SOURCE-INVALID",
                $"Card id '{cardId}' must be lowercase and contain only letters, digits, '.', '_', or '-'.",
                fullSourcePath)];
            return null;
        }

        if (original.Workspace.Pages.SelectMany(page => page.Cards)
            .Any(card => string.Equals(card.Id.Value, cardId, StringComparison.Ordinal)))
        {
            diagnostics = [Error(
                "OBLIVION-CARD-ID-ALREADY-EXISTS",
                $"Card id '{cardId}' already exists in workspace '{original.Workspace.Id.Value}'. Use --id with a different explicit id.",
                OblivionStructuredVaultPaths.CardMetadata(root, cardId))];
            return null;
        }

        string metadataPath = OblivionStructuredVaultPaths.CardMetadata(root, cardId);
        string contentPath = OblivionStructuredVaultPaths.MarkdownContent(root, cardId);
        if (File.Exists(metadataPath) || File.Exists(contentPath))
        {
            diagnostics = [Error(
                "OBLIVION-CARD-IMPORT-DESTINATION-CONFLICT",
                $"Import destination for card '{cardId}' already exists.",
                File.Exists(metadataPath) ? metadataPath : contentPath)];
            return null;
        }

        string markdown;
        try
        {
            markdown = File.ReadAllText(fullSourcePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics = [Error(
                "OBLIVION-CARD-IMPORT-SOURCE-INVALID",
                $"Markdown import source '{fullSourcePath}' could not be read: {exception.Message}",
                fullSourcePath)];
            return null;
        }

        string title = string.IsNullOrWhiteSpace(requestedTitle)
            ? DeriveTitle(markdown, Path.GetFileNameWithoutExtension(fullSourcePath))
            : requestedTitle.Trim();
        string stageRoot = CreateStage(root);
        try
        {
            CopyVault(root, stageRoot);
            string stagedPagePath = OblivionStructuredVaultPaths.PageMetadata(stageRoot, pageId);
            OblivionPageAssetDocument pageDocument = ReadPage(stagedPagePath);
            OblivionPageAssetDocument nextPage = pageDocument with
            {
                CardIds = [.. pageDocument.StructuredCardIds, cardId],
            };

            Directory.CreateDirectory(Path.Combine(stageRoot, OblivionStructuredVaultPaths.ContentDirectoryName));
            Directory.CreateDirectory(Path.Combine(stageRoot, OblivionStructuredVaultPaths.CardsDirectoryName));
            File.Copy(
                fullSourcePath,
                OblivionStructuredVaultPaths.MarkdownContent(stageRoot, cardId));
            OblivionCardAssetDocument cardDocument = new(
                OblivionWorkspaceValidator.SupportedFormat,
                OblivionWorkspaceValidator.CardKind,
                cardId,
                "note",
                "idle",
                title,
                string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim(),
                ["imported-markdown"],
                new OblivionCardBodyDocument(
                    "copeland-markdown",
                    null,
                    $"content/{cardId}.md"),
                [],
                [],
                new OblivionCardProvenanceDocument(
                    "imported-markdown",
                    fullSourcePath,
                    ImportedMarkdownProducer));
            string cardToml = ApplyNewlinePolicy(
                OblivionCardTomlWriter.Write(cardDocument),
                newlinePolicy,
                existingText: null);
            File.WriteAllText(
                OblivionStructuredVaultPaths.CardMetadata(stageRoot, cardId),
                cardToml);
            WritePageMutation(stagedPagePath, nextPage, newlinePolicy);

            OblivionWorkspaceLoadResult candidate = OblivionWorkspaceLoader.OpenVault(stageRoot);
            if (!candidate.Succeeded)
            {
                diagnostics = candidate.Diagnostics;
                return null;
            }

            CommitPush(root, stageRoot, pageId, cardId);
            OblivionWorkspaceLoadResult committed = OblivionWorkspaceLoader.OpenVault(root);
            diagnostics = committed.Diagnostics;
            return new OblivionStackMutationResult(
                "push",
                original.Workspace.Id.Value,
                pageId,
                cardId,
                pageDocument.StructuredCardIds.Count,
                nextPage.StructuredCardIds.Count,
                Relative(root, metadataPath),
                Relative(root, contentPath),
                ContentDeleted: false,
                diagnostics,
                committed);
        }
        finally
        {
            DeleteStage(stageRoot);
        }
    }

    public static OblivionStackMutationResult? Pop(
        string vaultRoot,
        string? requestedPageId,
        out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        return Pop(
            vaultRoot,
            requestedPageId,
            OblivionVaultNewlinePolicy.Preserve,
            out diagnostics);
    }

    public static OblivionStackMutationResult? Pop(
        string vaultRoot,
        string? requestedPageId,
        OblivionVaultNewlinePolicy newlinePolicy,
        out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        string root = Path.GetFullPath(vaultRoot);
        OblivionWorkspaceLoadResult original = OblivionWorkspaceLoader.OpenVault(root);
        if (!original.Succeeded || original.Workspace is null)
        {
            diagnostics = original.Diagnostics;
            return null;
        }

        if (!TryResolvePage(original, requestedPageId, out string pageId, out diagnostics))
        {
            return null;
        }

        Oblivion.Model.OblivionWorkspacePage page = original.Workspace.Pages.Single(
            candidate => candidate.Id.Value == pageId);
        if (page.Cards.Count == 0)
        {
            diagnostics = [Error(
                "OBLIVION-STACK-EMPTY",
                $"Page '{pageId}' has no top Card to pop.",
                OblivionStructuredVaultPaths.PageMetadata(root, pageId))];
            return null;
        }

        Oblivion.Model.OblivionCard top = page.Cards[^1];
        string cardId = top.Id.Value;
        string metadataPath = OblivionStructuredVaultPaths.CardMetadata(root, cardId);
        OblivionCardAssetDocument cardDocument = ReadCard(metadataPath);
        string contentReference = cardDocument.Body.Path!;
        if (!TryResolveOwnedContent(root, cardId, contentReference, out string contentPath))
        {
            diagnostics = [Error(
                "OBLIVION-CARD-POP-OWNERSHIP-AMBIGUOUS",
                $"Card '{cardId}' body path '{contentReference}' is not a safe canonical vault-owned content path.",
                metadataPath)];
            return null;
        }

        int referenceCount = CountContentReferences(root, original.Workspace, contentReference);
        bool deleteContent = referenceCount == 1;
        List<OblivionWorkspaceDiagnostic> resultDiagnostics = [];
        if (!deleteContent)
        {
            resultDiagnostics.Add(new OblivionWorkspaceDiagnostic(
                Oblivion.Model.OblivionDiagnosticSeverity.Info,
                "OBLIVION-CARD-CONTENT-RETAINED",
                $"Content '{contentReference}' was retained because it is referenced by another Card.",
                contentPath));
        }

        string stageRoot = CreateStage(root);
        try
        {
            CopyVault(root, stageRoot);
            string stagedPagePath = OblivionStructuredVaultPaths.PageMetadata(stageRoot, pageId);
            OblivionPageAssetDocument pageDocument = ReadPage(stagedPagePath);
            OblivionPageAssetDocument nextPage = pageDocument with
            {
                CardIds = pageDocument.StructuredCardIds.Take(pageDocument.StructuredCardIds.Count - 1).ToArray(),
            };
            WritePageMutation(stagedPagePath, nextPage, newlinePolicy);
            File.Delete(OblivionStructuredVaultPaths.CardMetadata(stageRoot, cardId));
            if (deleteContent)
            {
                File.Delete(Path.Combine(stageRoot, contentReference.Replace('/', Path.DirectorySeparatorChar)));
            }

            OblivionWorkspaceLoadResult candidate = OblivionWorkspaceLoader.OpenVault(stageRoot);
            if (!candidate.Succeeded)
            {
                diagnostics = candidate.Diagnostics;
                return null;
            }

            CommitPop(root, stageRoot, pageId, cardId, contentPath, deleteContent);
            OblivionWorkspaceLoadResult committed = OblivionWorkspaceLoader.OpenVault(root);
            resultDiagnostics.AddRange(committed.Diagnostics);
            diagnostics = OblivionWorkspaceValidator.OrderDiagnostics(resultDiagnostics);
            return new OblivionStackMutationResult(
                "pop",
                original.Workspace.Id.Value,
                pageId,
                cardId,
                pageDocument.StructuredCardIds.Count,
                nextPage.StructuredCardIds.Count,
                Relative(root, metadataPath),
                contentReference,
                deleteContent,
                diagnostics,
                committed);
        }
        finally
        {
            DeleteStage(stageRoot);
        }
    }

    public static string DeriveCardId(string fileStem)
    {
        string normalized = string.Concat(fileStem.Trim().ToLowerInvariant().Select(character =>
            char.IsAsciiLetterOrDigit(character) ? character : '-'));
        string collapsed = string.Join(
            '-',
            normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(collapsed) ? "card" : collapsed;
    }

    private static string DeriveTitle(string markdown, string fileStem)
    {
        string? heading = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal));
        if (heading is not null)
        {
            return heading[2..].Trim();
        }

        string[] words = fileStem.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(word =>
            char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static bool TryResolvePage(
        OblivionWorkspaceLoadResult load,
        string? requestedPageId,
        out string pageId,
        out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        string? target = requestedPageId ?? load.Workspace!.DefaultPageId?.Value;
        if (target is null)
        {
            pageId = string.Empty;
            diagnostics = [Error(
                "OBLIVION-PAGE-TARGET-REQUIRED",
                $"Workspace '{load.Workspace!.Id.Value}' has no default Page; provide --page.",
                load.Location?.ManifestPath)];
            return false;
        }

        if (!load.Workspace!.Pages.Any(page => page.Id.Value == target))
        {
            pageId = string.Empty;
            diagnostics = [Error(
                "unknown-page",
                $"Page '{target}' was not found in workspace '{load.Workspace.Id.Value}'.",
                load.Location?.ManifestPath)];
            return false;
        }

        pageId = target;
        diagnostics = [];
        return true;
    }

    private static bool TryResolveOwnedContent(
        string root,
        string cardId,
        string reference,
        out string contentPath)
    {
        contentPath = Path.GetFullPath(Path.Combine(root, reference.Replace('/', Path.DirectorySeparatorChar)));
        string contentRoot = Path.GetFullPath(Path.Combine(
            root,
            OblivionStructuredVaultPaths.ContentDirectoryName)) + Path.DirectorySeparatorChar;
        return contentPath.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(contentPath);
    }

    private static int CountContentReferences(
        string root,
        Oblivion.Model.OblivionWorkspace workspace,
        string reference)
    {
        int count = 0;
        foreach (Oblivion.Model.OblivionCard card in workspace.Pages.SelectMany(page => page.Cards))
        {
            string path = OblivionStructuredVaultPaths.CardMetadata(root, card.Id.Value);
            OblivionCardAssetDocument document = ReadCard(path);
            if (string.Equals(document.Body.Path, reference, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static void CommitPush(string root, string stageRoot, string pageId, string cardId)
    {
        string livePage = OblivionStructuredVaultPaths.PageMetadata(root, pageId);
        byte[] originalPage = File.ReadAllBytes(livePage);
        string liveCard = OblivionStructuredVaultPaths.CardMetadata(root, cardId);
        string liveContent = OblivionStructuredVaultPaths.MarkdownContent(root, cardId);
        try
        {
            File.Copy(OblivionStructuredVaultPaths.MarkdownContent(stageRoot, cardId), liveContent, overwrite: false);
            File.Copy(OblivionStructuredVaultPaths.CardMetadata(stageRoot, cardId), liveCard, overwrite: false);
            ReplaceFile(OblivionStructuredVaultPaths.PageMetadata(stageRoot, pageId), livePage);
        }
        catch
        {
            RestoreFile(livePage, originalPage);
            File.Delete(liveCard);
            File.Delete(liveContent);
            throw;
        }
    }

    private static void CommitPop(
        string root,
        string stageRoot,
        string pageId,
        string cardId,
        string contentPath,
        bool deleteContent)
    {
        string livePage = OblivionStructuredVaultPaths.PageMetadata(root, pageId);
        string liveCard = OblivionStructuredVaultPaths.CardMetadata(root, cardId);
        byte[] originalPage = File.ReadAllBytes(livePage);
        byte[] originalCard = File.ReadAllBytes(liveCard);
        byte[]? originalContent = deleteContent ? File.ReadAllBytes(contentPath) : null;
        try
        {
            ReplaceFile(OblivionStructuredVaultPaths.PageMetadata(stageRoot, pageId), livePage);
            File.Delete(liveCard);
            if (deleteContent)
            {
                File.Delete(contentPath);
            }
        }
        catch
        {
            RestoreFile(livePage, originalPage);
            RestoreFile(liveCard, originalCard);
            if (originalContent is not null)
            {
                RestoreFile(contentPath, originalContent);
            }

            throw;
        }
    }

    private static void ReplaceFile(string stagedPath, string livePath)
    {
        string temporaryPath = livePath + ".m19k-" + Guid.NewGuid().ToString("N") + ".tmp";
        File.Copy(stagedPath, temporaryPath);
        try
        {
            File.Move(temporaryPath, livePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static void RestoreFile(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static string CreateStage(string root)
    {
        string parent = Path.GetDirectoryName(root) ?? Path.GetTempPath();
        string stage = Path.Combine(parent, ".oblivion-m19k-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        return stage;
    }

    private static void CopyVault(string root, string stageRoot)
    {
        foreach (string sourcePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, sourcePath);
            string destinationPath = Path.Combine(stageRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath);
        }
    }

    private static void DeleteStage(string stageRoot)
    {
        if (Directory.Exists(stageRoot))
        {
            Directory.Delete(stageRoot, recursive: true);
        }
    }

    private static OblivionPageAssetDocument ReadPage(string path)
    {
        return OblivionPageTomlReader.Read(File.ReadAllText(path), path).Document ??
            throw new InvalidDataException($"Page metadata '{path}' could not be read after workspace validation.");
    }

    private static void WritePageMutation(
        string path,
        OblivionPageAssetDocument nextPage,
        OblivionVaultNewlinePolicy policy)
    {
        string original = File.ReadAllText(path);
        string replacement = FormatStringArray("cards", nextPage.StructuredCardIds);
        if (!TryReplaceCardsAssignment(original, replacement, out string mutated))
        {
            throw new InvalidDataException($"Page metadata '{path}' has no writable cards assignment.");
        }

        mutated = ApplyNewlinePolicy(mutated, policy, original);
        File.WriteAllText(path, mutated);
    }

    private static bool TryReplaceCardsAssignment(
        string original,
        string replacement,
        out string mutated)
    {
        int lineStart = 0;
        while (lineStart <= original.Length)
        {
            int lineEnd = lineStart;
            while (lineEnd < original.Length && original[lineEnd] is not ('\r' or '\n'))
            {
                lineEnd++;
            }

            string line = original[lineStart..lineEnd];
            string trimmed = line.TrimStart();
            int separator = trimmed.IndexOf('=');
            if (separator > 0 && string.Equals(trimmed[..separator].Trim(), "cards", StringComparison.Ordinal))
            {
                string indentation = line[..(line.Length - trimmed.Length)];
                mutated = original[..lineStart] + indentation + replacement + original[lineEnd..];
                return true;
            }

            if (lineEnd == original.Length)
            {
                break;
            }

            lineStart = original[lineEnd] == '\r' &&
                lineEnd + 1 < original.Length &&
                original[lineEnd + 1] == '\n'
                    ? lineEnd + 2
                    : lineEnd + 1;
        }

        mutated = original;
        return false;
    }

    private static string FormatStringArray(string key, IReadOnlyList<string> values)
    {
        string encoded = string.Join(", ", values.Select(value => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""));
        return $"{key} = [{encoded}]";
    }

    private static string ApplyNewlinePolicy(
        string text,
        OblivionVaultNewlinePolicy policy,
        string? existingText)
    {
        string newline = policy switch
        {
            OblivionVaultNewlinePolicy.Lf => "\n",
            OblivionVaultNewlinePolicy.Crlf => "\r\n",
            OblivionVaultNewlinePolicy.Preserve when existingText?.Contains("\r\n", StringComparison.Ordinal) == true => "\r\n",
            OblivionVaultNewlinePolicy.Preserve when existingText is not null => "\n",
            OblivionVaultNewlinePolicy.Preserve => Environment.NewLine,
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };
        if (policy == OblivionVaultNewlinePolicy.Preserve && existingText is not null)
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newline, StringComparison.Ordinal);
    }

    private static OblivionCardAssetDocument ReadCard(string path)
    {
        return OblivionCardTomlReader.Read(File.ReadAllText(path), path).Document ??
            throw new InvalidDataException($"Card metadata '{path}' could not be read after workspace validation.");
    }

    private static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static OblivionWorkspaceDiagnostic Error(string code, string message, string? source)
    {
        return OblivionWorkspaceValidator.Error(code, message, source);
    }
}
