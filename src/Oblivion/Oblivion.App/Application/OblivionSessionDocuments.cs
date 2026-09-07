using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionSessionDocument(
    string Schema,
    string WorkspaceRoot,
    string WorkspaceId,
    string ActivePageId,
    OblivionSessionState State)
{
    public const string CurrentSchema = "oblivion.session.v1";

    public static OblivionSessionDocument FromSession(OblivionWorkspaceSession session)
    {
        return new OblivionSessionDocument(
            CurrentSchema,
            Path.GetFullPath(session.Location.RootDirectory),
            session.Workspace.Id.Value,
            session.ActivePage.Id.Value,
            session.State);
    }
}

public sealed record OblivionSessionDocumentResult(
    OblivionSessionDocument? Document,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics)
{
    public bool Succeeded => Document is not null && Diagnostics.All(diagnostic =>
        diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed class OblivionSessionDocumentStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public OblivionSessionDocumentResult Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        try
        {
            if (!File.Exists(fullPath))
            {
                return new(null, []);
            }

            OblivionSessionDocument? document = JsonSerializer.Deserialize<OblivionSessionDocument>(
                File.ReadAllText(fullPath),
                Options);
            if (document is null || document.Schema != OblivionSessionDocument.CurrentSchema)
            {
                return Failure(
                    "OBLIVION-SESSION-SCHEMA-INVALID",
                    "The session document schema is missing or unsupported.",
                    fullPath);
            }

            return new(document, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Failure(
                "OBLIVION-SESSION-READ-FAILED",
                $"The session document could not be read: {exception.Message}",
                fullPath);
        }
    }

    public OblivionSessionDocumentResult Save(string path, OblivionWorkspaceSession session)
    {
        string fullPath = Path.GetFullPath(path);
        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        OblivionSessionDocument document = OblivionSessionDocument.FromSession(session);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, Options) + Environment.NewLine,
                new UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, overwrite: true);
            return new(document, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            return Failure(
                "OBLIVION-SESSION-WRITE-FAILED",
                $"The session document could not be committed: {exception.Message}",
                fullPath);
        }
    }

    private static OblivionSessionDocumentResult Failure(string code, string message, string path)
    {
        return new OblivionSessionDocumentResult(
            null,
            [OblivionWorkspaceValidator.Error(code, message, path)]);
    }
}
