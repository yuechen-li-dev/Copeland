using Oblivion.Presentation;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionPresentationContentSnapshot(
    string ContentId,
    string Kind,
    string CardId,
    string? SourceReference,
    string? LayoutGroupId,
    string Presenter,
    string ProvenanceSource,
    string? Producer,
    IReadOnlyList<string> Diagnostics);

public sealed record OblivionPresentationLayoutSnapshot(
    string Id,
    string Kind,
    IReadOnlyList<string> ContentIds,
    IReadOnlyList<string> CardIds);

public sealed record OblivionPresentationSnapshot(
    string SchemaVersion,
    string PresentationId,
    string Title,
    string PageId,
    int ContentCount,
    int CardCount,
    IReadOnlyList<OblivionPresentationContentSnapshot> Content,
    IReadOnlyList<OblivionPresentationLayoutSnapshot> Layout,
    IReadOnlyList<string> Diagnostics);

public static class OblivionPresentationInspection
{
    public const string SchemaVersion = "oblivion.presentation.v1";

    public static OblivionPresentationSnapshot Inspect(MaterializedPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        Dictionary<string, string> presenterByCardId = presentation.Page.Cards.ToDictionary(
            card => card.Id.Value,
            card => string.Join(
                "+",
                OblivionContentPresenterSelector.Select(
                        card,
                        OblivionCardViewState.Collapsed,
                        ResolveAbsoluteArtifacts(card))
                    .Items
                    .Select(item => item.PresenterKind.ToString())
                    .Distinct(StringComparer.Ordinal)),
            StringComparer.Ordinal);

        OblivionPresentationContentSnapshot[] content = presentation.Content
            .Select(item => new OblivionPresentationContentSnapshot(
                item.ContentId.Value,
                item.ContentKind,
                item.CardId.Value,
                item.SourceReference,
                item.LayoutGroupId,
                presenterByCardId[item.CardId.Value],
                item.Provenance.SourceKind.ToString(),
                item.Provenance.ProducerActionId,
                presentation.Diagnostics
                    .Where(diagnostic => diagnostic.ContentId == item.ContentId.Value)
                    .Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Message}")
                    .ToArray()))
            .ToArray();
        OblivionPresentationLayoutSnapshot[] layout = presentation.Bands
            .Select(band => new OblivionPresentationLayoutSnapshot(
                band.Id,
                band.Kind.ToString(),
                band.ContentIds.Select(id => id.Value).ToArray(),
                band.CardIds.Select(id => id.Value).ToArray()))
            .ToArray();

        return new OblivionPresentationSnapshot(
            SchemaVersion,
            presentation.Source.Id.Value,
            presentation.Source.Title,
            presentation.Page.Id.Value,
            content.Length,
            presentation.Page.Cards.Count,
            content,
            layout,
            presentation.Diagnostics
                .Select(diagnostic => $"{diagnostic.Code}:{diagnostic.Message}")
                .ToArray());
    }

    private static IReadOnlyList<OblivionResolvedContentArtifact> ResolveAbsoluteArtifacts(
        Oblivion.Model.OblivionCard card)
    {
        return card.Artifacts
            .Select(artifact =>
            {
                string? path = artifact.Reference is not null && Path.IsPathFullyQualified(artifact.Reference)
                    ? Path.GetFullPath(artifact.Reference)
                    : null;
                bool exists = path is not null && File.Exists(path);
                string? mediaType = string.Equals(artifact.Kind, "png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : null;
                return new OblivionResolvedContentArtifact(
                    artifact.Id,
                    artifact.Label,
                    artifact.Kind,
                    artifact.Reference,
                    path,
                    exists,
                    mediaType,
                    artifact.Generated,
                    artifact.SourceReference);
            })
            .ToArray();
    }
}
