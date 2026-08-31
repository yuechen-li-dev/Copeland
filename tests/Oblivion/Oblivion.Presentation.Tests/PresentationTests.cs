using System.Text.Json;
using Oblivion.Model;
using Oblivion.Presentation;
using Xunit;
using static Oblivion.Presentation.Content;
using static Oblivion.Presentation.Layout;

namespace Oblivion.Presentation.Tests;

public sealed class PresentationTests
{
    [Fact]
    public void TypedIdsSerializeStably()
    {
        PresentationContentId id = new("architecture-flow");

        string first = JsonSerializer.Serialize(id);
        string second = JsonSerializer.Serialize(id);

        Assert.Equal("{\"Value\":\"architecture-flow\"}", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DuplicateContentIdsAreRejectedWithSpecificDiagnostic()
    {
        Presentation presentation = Presentation.Create(
            "duplicate",
            "Duplicate",
            [Summary("same", "One"), Decision("same", "Two")]);

        PresentationValidationException exception = Assert.Throws<PresentationValidationException>(
            () => PresentationMaterializer.Materialize(presentation));

        PresentationDiagnostic diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("OBLIVION-PRESENTATION-DUPLICATE-CONTENT-ID", diagnostic.Code);
        Assert.Equal("same", diagnostic.ContentId);
    }

    [Fact]
    public void EverySupportedContentKindMapsToExistingCardSemantics()
    {
        Presentation presentation = CreateAllKindsPresentation();

        MaterializedPresentation result = PresentationMaterializer.Materialize(presentation);

        Assert.Collection(
            result.Page.Cards,
            card => AssertCard(card, OblivionCardKind.Status, OblivionCardBodyFormat.Plain),
            card => AssertCard(card, OblivionCardKind.Note, OblivionCardBodyFormat.CopelandMarkdown),
            card => AssertCard(card, OblivionCardKind.CodeFact, OblivionCardBodyFormat.Plain),
            card => AssertCard(card, OblivionCardKind.Note, OblivionCardBodyFormat.CopelandMarkdown),
            card =>
            {
                AssertCard(card, OblivionCardKind.Artifact, OblivionCardBodyFormat.Plain);
                Assert.Equal("png", Assert.Single(card.Artifacts).Kind);
            },
            card => AssertCard(card, OblivionCardKind.Note, OblivionCardBodyFormat.Plain),
            card => AssertCard(card, OblivionCardKind.Note, OblivionCardBodyFormat.Plain));
        Assert.Equal(
            ["Summary", "Markdown", "Code", "Diagram", "Artifact", "Decision", "NextActions"],
            result.Content.Select(item => item.ContentKind));
    }

    [Fact]
    public void RepeatedMaterializationProducesSameCardsAndOrder()
    {
        Presentation presentation = CreateAllKindsPresentation();

        MaterializedPresentation first = PresentationMaterializer.Materialize(presentation);
        MaterializedPresentation second = PresentationMaterializer.Materialize(presentation);

        Assert.Equal(
            JsonSerializer.Serialize(first.Page.Cards),
            JsonSerializer.Serialize(second.Page.Cards));
        Assert.Equal(
            JsonSerializer.Serialize(first.Bands),
            JsonSerializer.Serialize(second.Bands));
        Assert.Equal(
            JsonSerializer.Serialize(first.Content),
            JsonSerializer.Serialize(second.Content));
    }

    [Fact]
    public void ContentChangeUpdatesOnlyDeterministicProjection()
    {
        Presentation firstSource = Presentation.Create(
            "change",
            "Change",
            [Summary("summary", "Before")]);
        Presentation secondSource = firstSource with
        {
            Content = [Summary("summary", "After")],
        };

        OblivionCard first = Assert.Single(PresentationMaterializer.Materialize(firstSource).Page.Cards);
        OblivionCard second = Assert.Single(PresentationMaterializer.Materialize(secondSource).Page.Cards);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Before", first.Body.RawText);
        Assert.Equal("After", second.Body.RawText);
    }

    [Fact]
    public void LayoutChangesDoNotChangeContentOrCardIdentity()
    {
        Presentation plain = Presentation.Create(
            "layout-change",
            "Layout",
            [Summary("left", "Left"), Summary("right", "Right")]);
        Presentation compared = plain with
        {
            Layout = [Compare("comparison", "left", "right")],
        };

        MaterializedPresentation plainResult = PresentationMaterializer.Materialize(plain);
        MaterializedPresentation comparedResult = PresentationMaterializer.Materialize(compared);

        Assert.Equal(
            plainResult.Content.Select(item => item.CardId),
            comparedResult.Content.Select(item => item.CardId));
        Assert.Equal(
            plainResult.Source.Content.Select(item => item.Id),
            comparedResult.Source.Content.Select(item => item.Id));
        Assert.All(plainResult.Bands, band => Assert.Equal(PresentationMaterializedBandKind.Stream, band.Kind));
        Assert.Equal(PresentationMaterializedBandKind.Compare, Assert.Single(comparedResult.Bands).Kind);
    }

    [Fact]
    public void LayoutGroupsProduceDeterministicBandsAndUnreferencedFallback()
    {
        Presentation presentation = Presentation.Create(
            "groups",
            "Groups",
            [
                Summary("before", "Before"),
                Summary("after", "After"),
                Decision("one", "One"),
                Decision("two", "Two"),
                Decision("three", "Three"),
                Diagram("focus", new DiagramSource.Mermaid(PresentationSource.Inline("flowchart LR\nA-->B"))),
                NextActions("tail", ["Finish"]),
            ],
            [
                Compare("compare", "before", "after"),
                Columns("columns", "one", "two", "three"),
                Focus("focus-band", "focus"),
            ]);

        MaterializedPresentation result = PresentationMaterializer.Materialize(presentation);

        Assert.Equal(
            [
                PresentationMaterializedBandKind.Compare,
                PresentationMaterializedBandKind.Columns,
                PresentationMaterializedBandKind.Focus,
                PresentationMaterializedBandKind.Stream,
            ],
            result.Bands.Select(band => band.Kind));
        Assert.Equal(7, result.Bands.Sum(band => band.ContentIds.Count));
        Assert.Equal("tail", result.Bands[^1].ContentIds.Single().Value);
    }

    [Theory]
    [InlineData("unknown", "OBLIVION-PRESENTATION-UNKNOWN-CONTENT-ID")]
    [InlineData("membership", "OBLIVION-PRESENTATION-MULTIPLE-LAYOUT-MEMBERSHIP")]
    [InlineData("empty", "OBLIVION-PRESENTATION-INVALID-LAYOUT-GROUP")]
    [InlineData("large", "OBLIVION-PRESENTATION-INVALID-LAYOUT-GROUP")]
    [InlineData("duplicate-member", "OBLIVION-PRESENTATION-DUPLICATE-GROUP-MEMBER")]
    public void InvalidLayoutIsRejected(string caseName, string expectedCode)
    {
        IReadOnlyList<PresentationLayoutGroup> layout = caseName switch
        {
            "unknown" => [Focus("focus", "missing")],
            "membership" => [Focus("first", "a"), Compare("second", "a", "b")],
            "empty" => [Columns("columns")],
            "large" => [Columns("columns", "a", "b", "c", "d")],
            "duplicate-member" => [Compare("compare", "a", "a")],
            _ => throw new InvalidOperationException(caseName),
        };
        Presentation presentation = Presentation.Create(
            "invalid",
            "Invalid",
            [Summary("a", "A"), Summary("b", "B"), Summary("c", "C"), Summary("d", "D")],
            layout);

        IReadOnlyList<PresentationDiagnostic> diagnostics = PresentationMaterializer.Validate(presentation);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }

    [Fact]
    public void ProvenanceMapsPresentationContentSourceAndCards()
    {
        Presentation presentation = Presentation.Create(
            "provenance",
            "Provenance",
            [Markdown("notes", new PresentationSource("# Notes", "docs/notes.md"))]);

        MaterializedPresentation result = PresentationMaterializer.Materialize(presentation);
        PresentationMaterializedContent content = Assert.Single(result.Content);
        OblivionCard card = Assert.Single(result.Page.Cards);

        Assert.Equal("provenance", result.Source.Id.Value);
        Assert.Equal("notes", content.ContentId.Value);
        Assert.Equal("presentation.provenance.notes", content.CardId.Value);
        Assert.Equal("docs/notes.md", content.SourceReference);
        Assert.Equal(content.Provenance, card.Provenance);
        Assert.Contains("presentation=provenance;content=notes", card.Provenance.ProducerActionId);
    }

    [Fact]
    public void MermaidSourceRemainsLinkedForDerivedArtifactProjection()
    {
        Presentation presentation = Presentation.Create(
            "diagram",
            "Diagram",
            [Diagram(
                "architecture",
                new DiagramSource.Mermaid(new PresentationSource("flowchart LR\nA-->B", "architecture.mmd"))) ]);

        OblivionCard card = Assert.Single(PresentationMaterializer.Materialize(presentation).Page.Cards);

        Assert.Equal(OblivionCardBodyFormat.CopelandMarkdown, card.Body.Format);
        Assert.Equal("architecture.mmd", card.Body.SourceReference);
        Assert.Contains("```mermaid", card.Body.RawText);
        Assert.Contains("flowchart LR", card.Body.RawText);
        Assert.Contains("content=architecture", card.Provenance.ProducerActionId);
    }

    private static Presentation CreateAllKindsPresentation()
    {
        return Presentation.Create(
            "all-kinds",
            "All kinds",
            [
                Summary("summary", "Summary"),
                Markdown("markdown", new PresentationSource("# Markdown", "notes.md")),
                Code("code", new PresentationSource("public class C {}", "C.cs"), "csharp"),
                Diagram("diagram", new DiagramSource.Mermaid(PresentationSource.Inline("flowchart LR\nA-->B"))),
                Artifact("artifact", "proof.png", "png", generated: true),
                Decision("decision", "Ship it", ["summary"]),
                NextActions("next", ["Validate", "Report"]),
            ]);
    }

    private static void AssertCard(
        OblivionCard card,
        OblivionCardKind expectedKind,
        OblivionCardBodyFormat expectedFormat)
    {
        Assert.Equal(expectedKind, card.Kind);
        Assert.Equal(expectedFormat, card.Body.Format);
        Assert.StartsWith("presentation.all-kinds.", card.Id.Value, StringComparison.Ordinal);
    }
}
