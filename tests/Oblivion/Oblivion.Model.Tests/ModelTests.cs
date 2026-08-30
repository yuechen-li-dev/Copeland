using Oblivion.Model;
using Xunit;

namespace Oblivion.Model.Tests;

public sealed class ModelTests
{
    [Fact]
    public void Workspace_preserves_page_and_card_order()
    {
        OblivionCard first = CreateCard("first");
        OblivionCard second = CreateCard("second");
        var page = new OblivionWorkspacePage(new OblivionPageId("page"), "Page", null, [], [first, second]);
        var workspace = new OblivionWorkspace(
            new OblivionWorkspaceId("workspace"),
            "Workspace",
            page.Id,
            [new OblivionWorkspaceSection("section", "Section", [page])]);

        Assert.Equal(["first", "second"], workspace.Pages.Single().Cards.Select(card => card.Id.Value));
    }

    [Fact]
    public void Markdown_content_contains_source_but_no_projection()
    {
        var body = new OblivionCardBody(
            OblivionCardBodyFormat.CopelandMarkdown,
            new OblivionMarkdownReferenceContent("# Source", "body/source.md"));

        Assert.Equal("# Source", body.RawText);
        Assert.Equal("body/source.md", body.SourceReference);
        Assert.DoesNotContain(body.GetType().GetProperties(), property => property.Name.Contains("Mir", StringComparison.Ordinal));
    }

    [Fact]
    public void Assembly_has_no_forbidden_dependencies()
    {
        string[] references = typeof(OblivionWorkspace).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();
        Assert.DoesNotContain(references, name => name.StartsWith("Machina.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Aurelian.", StringComparison.Ordinal));
        Assert.DoesNotContain("Copeland.Markdown", references);
        Assert.DoesNotContain("Machina.Presenter.Sample", references);
    }

    private static OblivionCard CreateCard(string id)
    {
        return new OblivionCard(
            new OblivionCardId(id),
            OblivionCardKind.Note,
            OblivionCardStatus.Idle,
            id,
            null,
            [],
            new OblivionCardBody(OblivionCardBodyFormat.Plain, new OblivionPlainTextContent(id)),
            [],
            [],
            OblivionProvenance.Unknown);
    }
}
