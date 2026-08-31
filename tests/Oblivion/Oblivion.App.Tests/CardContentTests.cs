using Oblivion.Model;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class CardContentTests
{
    [Fact]
    public void Full_markdown_content_resolves_through_the_App_surface()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        string contentPath = Path.Combine(vault.Root, "content", "physical-atom.md");
        string content = "# Full UTF-8 source λ\r\n\r\n" + new string('z', 650) + "\r\n";
        File.WriteAllText(contentPath, content);

        OblivionControlResult<OblivionCardContentResult> result =
            new OblivionWorkspaceControl().GetCardContent(
                vault.Root,
                "physical-atom",
                "notebook");

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("m19i-notebook", result.Value!.WorkspaceId);
        Assert.Equal("notebook", result.Value.PageId);
        Assert.Equal("physical-atom", result.Value.CardId);
        Assert.Equal("markdown", result.Value.ContentKind);
        Assert.Equal("content/physical-atom.md", result.Value.Source);
        Assert.Equal(content, result.Value.Content);
        Assert.True(result.Value.Content.Length > 400);
    }

    [Fact]
    public void Missing_markdown_source_returns_the_existing_persistence_diagnostic()
    {
        using TemporaryVault vault = TemporaryVault.CopyFixture();
        File.Delete(Path.Combine(vault.Root, "content", "physical-atom.md"));

        OblivionControlResult<OblivionCardContentResult> result =
            new OblivionWorkspaceControl().GetCardContent(vault.Root, "physical-atom");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == "missing-markdown-body-file");
    }

    [Fact]
    public void Unsupported_body_kind_returns_an_explicit_content_diagnostic()
    {
        OblivionCard card = new(
            new OblivionCardId("plain-card"),
            OblivionCardKind.Note,
            OblivionCardStatus.Idle,
            "Plain Card",
            null,
            [],
            new OblivionCardBody(
                OblivionCardBodyFormat.Plain,
                new OblivionPlainTextContent("plain text")),
            [],
            [],
            OblivionProvenance.Unknown);
        OblivionWorkspace workspace = new(
            new OblivionWorkspaceId("content-test"),
            "Content test",
            new OblivionPageId("notes"),
            [new OblivionWorkspaceSection(
                "main",
                "Main",
                [new OblivionWorkspacePage(
                    new OblivionPageId("notes"),
                    "Notes",
                    null,
                    [],
                    [card])])]);

        OblivionControlResult<OblivionCardContentResult> result =
            OblivionWorkspaceControl.ResolveCardContent(
                workspace,
                "workspace.json",
                "plain-card",
                null);

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        OblivionControlDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OBLIVION-CARD-CONTENT-NOT-TEXT", diagnostic.Code);
        Assert.Equal("notes", diagnostic.PageId);
        Assert.Equal("plain-card", diagnostic.CardId);
    }

    private static string FixtureRoot => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "M19iNotebook.oblivion");

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
                "oblivion-m19l-app-tests",
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

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
