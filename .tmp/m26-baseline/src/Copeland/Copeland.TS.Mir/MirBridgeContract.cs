namespace Copeland.TS.Mir;

/// <summary>
/// Deterministic identity and route rules shared by bridge emitters. The
/// route is derived only from authored module identity and function name.
/// </summary>
public static class MirBridgeContract
{
    public const int SchemaVersion = 1;
    public const string PostMethod = "POST";
    public const string RoutePrefix = "/__copeland/m0/";

    public static string CreateOperationId(MirModuleId moduleId, string functionName)
        => $"{moduleId.Value}/{functionName}";

    public static string CreateRoute(MirModuleId moduleId, string functionName)
    {
        string modulePath = moduleId.Value.Replace('\\', '/');
        int extensionIndex = modulePath.LastIndexOf('.');
        if (extensionIndex >= 0)
        {
            modulePath = modulePath[..extensionIndex];
        }

        string routePath = string.Join('/', modulePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Slugify));
        string functionPath = Slugify(functionName);
        return RoutePrefix + routePath + "/" + functionPath;
    }

    private static string Slugify(string value)
    {
        var characters = new List<char>(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                characters.Add('-');
            }

            characters.Add(char.ToLowerInvariant(character));
        }

        return new string(characters.ToArray());
    }
}
