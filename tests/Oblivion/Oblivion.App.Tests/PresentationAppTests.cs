using Oblivion.Presentation;
using Xunit;

namespace Oblivion.App.Tests;

public sealed class PresentationAppTests
{
    [Fact]
    public void DogfoodUsesOnlySemanticAuthoringAndMaterializesSixCards()
    {
        MaterializedPresentation result = M19PresentationDogfood.Materialize(FindRepositoryRoot());

        Assert.Equal(6, result.Source.Content.Count);
        Assert.Equal(6, result.Page.Cards.Count);
        Assert.All(result.Page.Cards, card =>
            Assert.StartsWith("presentation.m19-architecture.", card.Id.Value, StringComparison.Ordinal));
        Assert.Contains(result.Bands, band => band.Kind == PresentationMaterializedBandKind.Compare);
        Assert.Contains(result.Bands, band => band.Kind == PresentationMaterializedBandKind.Focus);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void HeadlessInspectionExplainsContentCardsSourcesLayoutAndPresenters()
    {
        OblivionPresentationSnapshot snapshot = OblivionPresentationInspection.Inspect(
            M19PresentationDogfood.Materialize(FindRepositoryRoot()));

        Assert.Equal("m19-architecture", snapshot.PresentationId);
        Assert.Equal(snapshot.ContentCount, snapshot.CardCount);
        Assert.Contains(snapshot.Content, item =>
            item.Kind == "Markdown" && item.Presenter == "AvaloniaReadOnlyDocument");
        Assert.Contains(snapshot.Content, item =>
            item.Kind == "Code" && item.Presenter == "AvaloniaReadOnlyCode");
        Assert.Contains(snapshot.Content, item =>
            item.Kind == "Diagram" && item.Presenter.Contains("ExternalMermaidRenderer", StringComparison.Ordinal));
        Assert.All(snapshot.Content, item => Assert.Contains("content=", item.Producer));
        Assert.Empty(snapshot.Diagnostics);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Oblivion.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for presentation tests.");
    }
}
