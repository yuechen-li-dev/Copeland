using Tomlyn.Model;

namespace Machina.Presenter.Sample.Playback;

public static class PresenterPlaybackSuiteManifestTomlParser
{
    public static PresenterPlaybackSuiteDefinition LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string toml = File.ReadAllText(fullPath);
        return LoadString(toml, fullPath);
    }

    public static PresenterPlaybackSuiteDefinition LoadString(string toml, string sourcePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(toml);

        TomlTable model = Tomlyn.TomlSerializer.Deserialize<TomlTable>(toml, Tomlyn.TomlSerializerOptions.Default)
            ?? throw new PresenterPlaybackScenarioParseException(sourcePath, "Suite manifest root must be a TOML table.");

        TomlTable suiteTable = GetRequiredTable(model, "suite", sourcePath);
        TomlTableArray scenarioTables = GetRequiredTableArray(model, "scenario", sourcePath);

        string id = GetRequiredString(suiteTable, "id", sourcePath);
        string name = GetRequiredString(suiteTable, "name", sourcePath);
        string baseDirectory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();

        string[] scenarioPaths = scenarioTables
            .Select((table, index) =>
            {
                string value = GetRequiredString(table, "path", sourcePath);
                return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
            })
            .ToArray();

        return new PresenterPlaybackSuiteDefinition(sourcePath, id, name, scenarioPaths);
    }

    private static TomlTable GetRequiredTable(TomlTable table, string key, string sourcePath)
    {
        if (table.TryGetValue(key, out object? value) && value is TomlTable child)
        {
            return child;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Missing required TOML table '{key}'.");
    }

    private static TomlTableArray GetRequiredTableArray(TomlTable table, string key, string sourcePath)
    {
        if (table.TryGetValue(key, out object? value) && value is TomlTableArray array)
        {
            return array;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Missing required TOML table array '{key}'.");
    }

    private static string GetRequiredString(TomlTable table, string key, string sourcePath)
    {
        if (table.TryGetValue(key, out object? value) &&
            value?.ToString() is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Missing required string '{key}'.");
    }
}
