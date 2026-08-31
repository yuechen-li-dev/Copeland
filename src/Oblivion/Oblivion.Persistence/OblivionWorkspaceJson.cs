using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblivion.Persistence;

public static class OblivionWorkspaceJsonReader
{
    public static OblivionWorkspaceJsonReadResult Read(string json, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            OblivionWorkspaceManifestJsonModel? model = JsonSerializer.Deserialize(json, JsonContext.Default.OblivionWorkspaceManifestJsonModel);
            if (model is null)
            {
                return new OblivionWorkspaceJsonReadResult(
                    null,
                    [OblivionWorkspaceValidator.Error("json-deserialize-failed", "Workspace manifest JSON could not be deserialized.", sourcePath)]);
            }

            OblivionWorkspaceManifest manifest = new(
                model.Format,
                model.Kind ?? string.Empty,
                model.WorkspaceId ?? string.Empty,
                model.Title ?? string.Empty,
                model.DefaultPageId,
                model.Sections?
                    .Select(
                        section => new OblivionWorkspaceSectionManifest(
                            section.Id ?? string.Empty,
                            section.Title ?? string.Empty,
                            section.Pages?
                                .Select(
                                    page => new OblivionWorkspacePageManifest(
                                        page.Id ?? string.Empty,
                                        page.Title ?? string.Empty,
                                        page.Asset,
                                        page.Cards?.Where(card => card is not null).Select(card => card!).ToArray() ?? []))
                                .ToArray() ?? []))
                    .ToArray() ?? [],
                model.Pages is null
                    ? null
                    : model.Pages.Where(page => page is not null).Select(page => page!).ToArray());

            return new OblivionWorkspaceJsonReadResult(
                manifest,
                OblivionWorkspaceValidator.ValidateManifest(manifest, sourcePath));
        }
        catch (JsonException ex)
        {
            return new OblivionWorkspaceJsonReadResult(
                null,
                [OblivionWorkspaceValidator.Error("json-parse-failed", ex.Message, sourcePath)]);
        }
    }
}

public static class OblivionWorkspaceJsonWriter
{
    public static string Write(OblivionWorkspaceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var model = new OblivionWorkspaceManifestJsonModel
        {
            Format = manifest.Format,
            Kind = manifest.Kind,
            WorkspaceId = manifest.WorkspaceId,
            Title = manifest.Title,
            DefaultPageId = manifest.DefaultPageId,
            Pages = manifest.PageIds?.ToArray(),
            Sections = manifest.Sections.Count == 0
                ? null
                : manifest.Sections
                .Select(
                    section => new OblivionWorkspaceSectionJsonModel
                    {
                        Id = section.Id,
                        Title = section.Title,
                        Pages = section.Pages
                            .Select(
                                page => new OblivionWorkspacePageJsonModel
                                {
                                    Id = page.Id,
                                    Title = page.Title,
                                    Asset = page.Asset,
                                    Cards = page.Cards.ToArray(),
                                })
                            .ToArray(),
                    })
                .ToArray(),
        };

        return JsonSerializer.Serialize(
            model,
            JsonContext.Default.OblivionWorkspaceManifestJsonModel);
    }
}

internal sealed class OblivionWorkspaceManifestJsonModel
{
    [JsonPropertyName("format")]
    public int Format { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("workspaceId")]
    public string? WorkspaceId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("defaultPageId")]
    public string? DefaultPageId { get; set; }

    [JsonPropertyName("sections")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OblivionWorkspaceSectionJsonModel[]? Sections { get; set; }

    [JsonPropertyName("pages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Pages { get; set; }
}

internal sealed class OblivionWorkspaceSectionJsonModel
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("pages")]
    public OblivionWorkspacePageJsonModel[]? Pages { get; set; }
}

internal sealed class OblivionWorkspacePageJsonModel
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("asset")]
    public string? Asset { get; set; }

    [JsonPropertyName("cards")]
    public string[]? Cards { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(OblivionWorkspaceManifestJsonModel))]
internal sealed partial class JsonContext : JsonSerializerContext
{
}
