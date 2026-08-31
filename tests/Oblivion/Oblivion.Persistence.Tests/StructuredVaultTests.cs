using Oblivion.Model;
using Oblivion.Persistence;
using Xunit;

namespace Oblivion.Persistence.Tests;

public sealed class StructuredVaultTests
{
    [Fact]
    public void Canonical_paths_map_stable_ids_without_search()
    {
        string root = Path.GetFullPath(Path.Combine("vaults", "notebook.oblivion"));

        Assert.Equal(
            Path.Combine(root, "workspace.json"),
            OblivionStructuredVaultPaths.WorkspaceManifest(root));
        Assert.Equal(
            Path.Combine(root, "pages", "notebook.toml"),
            OblivionStructuredVaultPaths.PageMetadata(root, "notebook"));
        Assert.Equal(
            Path.Combine(root, "cards", "physical-atom.toml"),
            OblivionStructuredVaultPaths.CardMetadata(root, "physical-atom"));
        Assert.Equal(
            Path.Combine(root, "content", "physical-atom.md"),
            OblivionStructuredVaultPaths.MarkdownContent(root, "physical-atom"));
        Assert.False(OblivionStructuredVaultPaths.IsValidId("../physical-atom"));
    }

    [Fact]
    public void Real_fixture_loads_one_page_and_two_markdown_cards_in_declared_order()
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.OpenVault(FixtureRoot);
        OblivionWorkspaceManifest manifest = Assert.IsType<OblivionWorkspaceManifest>(
            OblivionWorkspaceJsonReader.Read(
                File.ReadAllText(Path.Combine(FixtureRoot, "workspace.json"))).Manifest);
        string canonicalJson = OblivionWorkspaceJsonWriter.Write(manifest);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains("\"pages\"", canonicalJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sections\"", canonicalJson, StringComparison.Ordinal);
        Assert.Equal("m19i-notebook", result.Workspace!.Id.Value);
        OblivionWorkspacePage page = Assert.Single(result.Workspace.Pages);
        Assert.Equal("notebook", page.Id.Value);
        Assert.Collection(
            page.Cards,
            card => AssertStructuredMarkdownCard(card, "physical-atom", "content/physical-atom.md"),
            card => AssertStructuredMarkdownCard(card, "notebook-stack", "content/notebook-stack.md"));
    }

    [Theory]
    [InlineData(BrokenVaultCase.MissingManifest, "missing-workspace-manifest")]
    [InlineData(BrokenVaultCase.MissingPage, "missing-page-metadata")]
    [InlineData(BrokenVaultCase.MissingCard, "missing-card-metadata")]
    [InlineData(BrokenVaultCase.MissingMarkdown, "missing-markdown-body-file")]
    [InlineData(BrokenVaultCase.DuplicateCardId, "duplicate-card-id")]
    [InlineData(BrokenVaultCase.UnsafeTraversal, "path-traversal-not-allowed")]
    [InlineData(BrokenVaultCase.UnknownCardKind, "unknown-card-kind")]
    public void Broken_vaults_report_specific_diagnostics(
        BrokenVaultCase brokenCase,
        string expectedCode)
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        vault.Break(brokenCase);

        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.OpenVault(vault.Root);

        Assert.False(result.Succeeded);
        OblivionWorkspaceDiagnostic diagnostic = Assert.Single(
            result.Diagnostics,
            candidate => candidate.Code == expectedCode);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.SourcePath));
    }

    [Fact]
    public void Markdown_and_card_metadata_edits_are_visible_on_explicit_reload()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string markdownPath = Path.Combine(vault.Root, "content", "notebook-stack.md");
        string cardPath = Path.Combine(vault.Root, "cards", "notebook-stack.toml");

        OblivionWorkspaceLoadResult before = OblivionWorkspaceLoader.OpenVault(vault.Root);
        File.AppendAllText(markdownPath, Environment.NewLine + "Reloaded content marker." + Environment.NewLine);
        File.WriteAllText(
            cardPath,
            File.ReadAllText(cardPath).Replace(
                "From one card to a notebook stack",
                "A reloaded notebook stack",
                StringComparison.Ordinal));

        OblivionWorkspaceLoadResult after = OblivionWorkspaceLoader.OpenVault(vault.Root);

        Assert.True(before.Succeeded);
        Assert.True(after.Succeeded, string.Join(Environment.NewLine, after.Diagnostics));
        OblivionCard beforeCard = before.Workspace!.Pages[0].Cards[1];
        OblivionCard afterCard = after.Workspace!.Pages[0].Cards[1];
        Assert.DoesNotContain("Reloaded content marker.", beforeCard.Body.RawText, StringComparison.Ordinal);
        Assert.Contains("Reloaded content marker.", afterCard.Body.RawText, StringComparison.Ordinal);
        Assert.Equal("From one card to a notebook stack", beforeCard.Title);
        Assert.Equal("A reloaded notebook stack", afterCard.Title);
    }

    [Fact]
    public void Push_imports_exact_content_appends_then_pop_restores_and_can_repeat()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string externalRoot = Path.Combine(Path.GetTempPath(), "oblivion-m19k-source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalRoot);
        string source = Path.Combine(externalRoot, "Architecture Notes.md");
        const string originalText = "# Architecture notes\r\n\r\nCaptured outside the vault.\r\n";
        File.WriteAllText(source, originalText);
        try
        {
            OblivionStackMutationResult pushed = Assert.IsType<OblivionStackMutationResult>(
                OblivionStackMutation.PushMarkdown(
                    vault.Root,
                    source,
                    null,
                    null,
                    null,
                    null,
                    out IReadOnlyList<OblivionWorkspaceDiagnostic> pushDiagnostics));

            Assert.DoesNotContain(
                pushDiagnostics,
                diagnostic => diagnostic.Severity == OblivionDiagnosticSeverity.Error);
            Assert.Equal((2, 3), (pushed.OldCount, pushed.NewCount));
            Assert.Equal(originalText, File.ReadAllText(Path.Combine(vault.Root, pushed.ContentPath)));
            File.WriteAllText(source, "# Changed external source\n");
            OblivionWorkspaceLoadResult afterExternalEdit = OblivionWorkspaceLoader.OpenVault(vault.Root);
            OblivionCard imported = afterExternalEdit.Workspace!.Pages[0].Cards[^1];
            Assert.Equal("architecture-notes", imported.Id.Value);
            Assert.Equal("Architecture notes", imported.Title);
            Assert.Equal(originalText, imported.Body.RawText);
            Assert.Equal(OblivionProvenanceSourceKind.ImportedMarkdown, imported.Provenance.SourceKind);
            Assert.Equal("oblivion.card.push", imported.Provenance.ProducerActionId);

            OblivionStackMutationResult popped = Assert.IsType<OblivionStackMutationResult>(
                OblivionStackMutation.Pop(vault.Root, null, out _));
            Assert.True(popped.ContentDeleted);
            Assert.Equal((3, 2), (popped.OldCount, popped.NewCount));
            Assert.False(File.Exists(Path.Combine(vault.Root, popped.MetadataPath)));
            Assert.False(File.Exists(Path.Combine(vault.Root, popped.ContentPath)));

            File.WriteAllText(source, originalText);
            OblivionStackMutationResult repeated = Assert.IsType<OblivionStackMutationResult>(
                OblivionStackMutation.PushMarkdown(
                    vault.Root,
                    source,
                    null,
                    null,
                    null,
                    null,
                    out _));
            Assert.Equal("architecture-notes", repeated.CardId);
            Assert.Equal((2, 3), (repeated.OldCount, repeated.NewCount));
        }
        finally
        {
            Directory.Delete(externalRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("\n", true)]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    [InlineData("\r\n", false)]
    public void Preserve_push_then_pop_restores_exact_page_bytes(string newline, bool trailingNewline)
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string pagePath = Path.Combine(vault.Root, "pages", "notebook.toml");
        string canonical = File.ReadAllText(pagePath)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n');
        string authored = canonical.Replace("\n", newline, StringComparison.Ordinal) +
            (trailingNewline ? newline : string.Empty);
        File.WriteAllText(pagePath, authored);
        byte[] original = File.ReadAllBytes(pagePath);
        string source = Path.Combine(Path.GetDirectoryName(vault.Root)!, $"newline-{Guid.NewGuid():N}.md");
        File.WriteAllText(source, "# Newline proof\n");
        try
        {
            Assert.NotNull(OblivionStackMutation.PushMarkdown(
                vault.Root,
                source,
                null,
                "newline-proof",
                null,
                null,
                OblivionVaultNewlinePolicy.Preserve,
                out _));
            Assert.NotNull(OblivionStackMutation.Pop(
                vault.Root,
                null,
                OblivionVaultNewlinePolicy.Preserve,
                out _));

            Assert.Equal(original, File.ReadAllBytes(pagePath));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public void Preserve_push_then_pop_restores_mixed_existing_line_endings_exactly()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string pagePath = Path.Combine(vault.Root, "pages", "notebook.toml");
        string canonical = File.ReadAllText(pagePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        string mixed = canonical.Replace("\n", "\r\n", StringComparison.Ordinal)
            .Replace("cards = ", "cards = ", StringComparison.Ordinal);
        int cardsStart = mixed.IndexOf("cards = ", StringComparison.Ordinal);
        mixed = mixed[..cardsStart].Replace("\r\n", "\n", StringComparison.Ordinal) + mixed[cardsStart..];
        File.WriteAllText(pagePath, mixed);
        byte[] original = File.ReadAllBytes(pagePath);
        string source = Path.Combine(Path.GetDirectoryName(vault.Root)!, $"mixed-{Guid.NewGuid():N}.md");
        File.WriteAllText(source, "# Mixed proof\n");
        try
        {
            Assert.NotNull(OblivionStackMutation.PushMarkdown(
                vault.Root,
                source,
                null,
                "mixed-proof",
                null,
                null,
                OblivionVaultNewlinePolicy.Preserve,
                out _));
            Assert.NotNull(OblivionStackMutation.Pop(
                vault.Root,
                null,
                OblivionVaultNewlinePolicy.Preserve,
                out _));
            Assert.Equal(original, File.ReadAllBytes(pagePath));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Theory]
    [InlineData(OblivionVaultNewlinePolicy.Lf, "\n")]
    [InlineData(OblivionVaultNewlinePolicy.Crlf, "\r\n")]
    public void Explicit_newline_policy_applies_to_rewritten_page_and_new_card_metadata(
        OblivionVaultNewlinePolicy policy,
        string expectedNewline)
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string source = Path.Combine(Path.GetDirectoryName(vault.Root)!, $"policy-{Guid.NewGuid():N}.md");
        const string importedBytes = "# Source\r\n\r\nKeep imported bytes.\r\n";
        File.WriteAllText(source, importedBytes);
        try
        {
            OblivionStackMutationResult mutation = Assert.IsType<OblivionStackMutationResult>(
                OblivionStackMutation.PushMarkdown(
                    vault.Root,
                    source,
                    null,
                    "policy-proof",
                    null,
                    null,
                    policy,
                    out _));

            AssertOnlyNewline(File.ReadAllText(Path.Combine(vault.Root, "pages", "notebook.toml")), expectedNewline);
            AssertOnlyNewline(File.ReadAllText(Path.Combine(vault.Root, mutation.MetadataPath)), expectedNewline);
            Assert.Equal(importedBytes, File.ReadAllText(Path.Combine(vault.Root, mutation.ContentPath)));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public void Pop_removes_metadata_but_retains_shared_content()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        vault.ReplaceInFile(
            Path.Combine(vault.Root, "cards", "notebook-stack.toml"),
            "content/notebook-stack.md",
            "content/physical-atom.md");

        OblivionStackMutationResult result = Assert.IsType<OblivionStackMutationResult>(
            OblivionStackMutation.Pop(vault.Root, "notebook", out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics));

        Assert.False(result.ContentDeleted);
        Assert.False(File.Exists(Path.Combine(vault.Root, "cards", "notebook-stack.toml")));
        Assert.True(File.Exists(Path.Combine(vault.Root, "content", "physical-atom.md")));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "OBLIVION-CARD-CONTENT-RETAINED");
        OblivionWorkspaceLoadResult loaded = OblivionWorkspaceLoader.OpenVault(vault.Root);
        Assert.True(loaded.Succeeded, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.Equal("physical-atom", Assert.Single(loaded.Workspace!.Pages[0].Cards).Id.Value);
    }

    [Fact]
    public void Duplicate_push_and_unsafe_pop_leave_the_vault_byte_for_byte_unchanged()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string source = Path.Combine(vault.Root, "duplicate.md");
        File.WriteAllText(source, "# Duplicate\n");
        IReadOnlyDictionary<string, byte[]> beforeDuplicate = vault.Snapshot();

        Assert.Null(OblivionStackMutation.PushMarkdown(
            vault.Root,
            source,
            null,
            "physical-atom",
            null,
            null,
            out IReadOnlyList<OblivionWorkspaceDiagnostic> duplicateDiagnostics));
        Assert.Contains(duplicateDiagnostics, diagnostic => diagnostic.Code == "OBLIVION-CARD-ID-ALREADY-EXISTS");
        vault.AssertSnapshot(beforeDuplicate);

        vault.ReplaceInFile(
            Path.Combine(vault.Root, "cards", "notebook-stack.toml"),
            "content/notebook-stack.md",
            "../outside.md");
        IReadOnlyDictionary<string, byte[]> beforeUnsafePop = vault.Snapshot();
        Assert.Null(OblivionStackMutation.Pop(vault.Root, null, out IReadOnlyList<OblivionWorkspaceDiagnostic> popDiagnostics));
        Assert.Contains(popDiagnostics, diagnostic => diagnostic.Code == "path-traversal-not-allowed");
        vault.AssertSnapshot(beforeUnsafePop);
    }

    private static string FixtureRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "M19iNotebook.oblivion");

    private static void AssertStructuredMarkdownCard(
        OblivionCard card,
        string expectedId,
        string expectedReference)
    {
        Assert.Equal(expectedId, card.Id.Value);
        Assert.Equal(OblivionCardBodyFormat.CopelandMarkdown, card.Body.Format);
        Assert.Equal(expectedReference, card.Body.SourceReference);
        Assert.Equal(OblivionProvenanceSourceKind.WorkspaceAsset, card.Provenance.SourceKind);
        Assert.Equal($"cards/{expectedId}.toml", card.Provenance.SourceReference);
    }

    private static void AssertOnlyNewline(string text, string expectedNewline)
    {
        if (expectedNewline == "\r\n")
        {
            Assert.Contains("\r\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            return;
        }

        Assert.Contains("\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
    }

    public enum BrokenVaultCase
    {
        MissingManifest,
        MissingPage,
        MissingCard,
        MissingMarkdown,
        DuplicateCardId,
        UnsafeTraversal,
        UnknownCardKind,
    }

    private sealed class TemporaryVault : IDisposable
    {
        private TemporaryVault(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporaryVault CopyFixture()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "oblivion-m19i-tests",
                Guid.NewGuid().ToString("N"));
            foreach (string sourcePath in Directory.GetFiles(FixtureRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(FixtureRoot, sourcePath);
                string destinationPath = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }

            return new TemporaryVault(root);
        }

        public void Break(BrokenVaultCase brokenCase)
        {
            switch (brokenCase)
            {
                case BrokenVaultCase.MissingManifest:
                    File.Delete(Path.Combine(Root, "workspace.json"));
                    break;
                case BrokenVaultCase.MissingPage:
                    File.Delete(Path.Combine(Root, "pages", "notebook.toml"));
                    break;
                case BrokenVaultCase.MissingCard:
                    File.Delete(Path.Combine(Root, "cards", "physical-atom.toml"));
                    break;
                case BrokenVaultCase.MissingMarkdown:
                    File.Delete(Path.Combine(Root, "content", "physical-atom.md"));
                    break;
                case BrokenVaultCase.DuplicateCardId:
                    ReplaceInFile(
                        Path.Combine(Root, "pages", "notebook.toml"),
                        "[\"physical-atom\", \"notebook-stack\"]",
                        "[\"physical-atom\", \"physical-atom\"]");
                    break;
                case BrokenVaultCase.UnsafeTraversal:
                    ReplaceInFile(
                        Path.Combine(Root, "cards", "physical-atom.toml"),
                        "content/physical-atom.md",
                        "../physical-atom.md");
                    break;
                case BrokenVaultCase.UnknownCardKind:
                    ReplaceInFile(
                        Path.Combine(Root, "cards", "physical-atom.toml"),
                        "card_kind = \"note\"",
                        "card_kind = \"mystery\"");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(brokenCase), brokenCase, null);
            }
        }

        public void ReplaceInFile(string path, string oldValue, string newValue)
        {
            ReplaceInFileCore(path, oldValue, newValue);
        }

        public IReadOnlyDictionary<string, byte[]> Snapshot()
        {
            return Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(Root, path),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);
        }

        public void AssertSnapshot(IReadOnlyDictionary<string, byte[]> expected)
        {
            IReadOnlyDictionary<string, byte[]> actual = Snapshot();
            Assert.Equal(expected.Keys.OrderBy(key => key), actual.Keys.OrderBy(key => key));
            foreach ((string path, byte[] content) in expected)
            {
                Assert.Equal(content, actual[path]);
            }
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }

        private static void ReplaceInFileCore(string path, string oldValue, string newValue)
        {
            string source = File.ReadAllText(path);
            Assert.Contains(oldValue, source, StringComparison.Ordinal);
            File.WriteAllText(path, source.Replace(oldValue, newValue, StringComparison.Ordinal));
        }
    }
}
