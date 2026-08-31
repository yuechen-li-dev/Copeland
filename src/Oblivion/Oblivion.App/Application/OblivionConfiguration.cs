using System.Text;

namespace Oblivion.App;

public enum OblivionAppearance
{
    System,
    Light,
    Dark,
}

public enum OblivionNewlinePolicy
{
    Preserve,
    Lf,
    Crlf,
}

public enum OblivionStyleProfile
{
    Default,
}

public enum OblivionConfigKey
{
    Appearance,
    Newline,
    Style,
}

public sealed record OblivionConfig(
    OblivionAppearance Appearance,
    OblivionNewlinePolicy NewlinePolicy,
    OblivionStyleProfile Style)
{
    public static OblivionConfig Default { get; } = new(
        OblivionAppearance.System,
        OblivionNewlinePolicy.Preserve,
        OblivionStyleProfile.Default);
}

public sealed record OblivionConfigDiagnostic(
    string Code,
    string Severity,
    string Message,
    string Path);

public sealed record OblivionConfigResult(
    OblivionConfig? Config,
    OblivionConfigKey? Key,
    string? Value,
    bool Persisted,
    IReadOnlyList<OblivionConfigDiagnostic> Diagnostics)
{
    public bool Succeeded => Config is not null && Diagnostics.Count == 0;
}

public sealed class OblivionConfigStore
{
    private readonly string _path;

    public OblivionConfigStore(string? path = null)
    {
        _path = System.IO.Path.GetFullPath(path ?? DefaultPath);
    }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Oblivion",
        "config.toml");

    public string Path => _path;

    public OblivionConfigResult Show()
    {
        return Load();
    }

    public OblivionConfigResult Get(string externalKey)
    {
        if (!TryParseKey(externalKey, out OblivionConfigKey key))
        {
            return Failure(
                "OBLIVION-CONFIG-KEY-UNKNOWN",
                $"Unknown config key '{externalKey}'. Expected appearance, newline, or style.");
        }

        OblivionConfigResult load = Load();
        return load.Config is null
            ? load
            : load with { Key = key, Value = FormatValue(load.Config, key) };
    }

    public OblivionConfigResult Set(string externalKey, string externalValue)
    {
        if (!TryParseKey(externalKey, out OblivionConfigKey key))
        {
            return Failure(
                "OBLIVION-CONFIG-KEY-UNKNOWN",
                $"Unknown config key '{externalKey}'. Expected appearance, newline, or style.");
        }

        OblivionConfigResult load = Load();
        if (load.Config is null)
        {
            return load;
        }

        if (!TryApply(load.Config, key, externalValue, out OblivionConfig next, out string expected))
        {
            return Failure(
                "OBLIVION-CONFIG-VALUE-INVALID",
                $"Invalid value '{externalValue}' for '{FormatKey(key)}'. Expected {expected}.");
        }

        string directory = System.IO.Path.GetDirectoryName(_path)!;
        string temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporaryPath, Serialize(next), new UTF8Encoding(false));
            OblivionConfigResult validation = Parse(File.ReadAllText(temporaryPath), temporaryPath);
            if (validation.Config != next)
            {
                return Failure(
                    "OBLIVION-CONFIG-WRITE-VALIDATION-FAILED",
                    "The temporary config file did not validate to the requested typed value.");
            }

            File.Move(temporaryPath, _path, overwrite: true);
            return new(next, key, FormatValue(next, key), Persisted: true, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "OBLIVION-CONFIG-WRITE-FAILED",
                $"Config could not be written atomically: {exception.Message}");
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The validated destination outcome is authoritative. A denied best-effort
                // cleanup must not replace it with an unrelated exception.
            }
        }
    }

    public OblivionConfigResult Load()
    {
        if (!File.Exists(_path))
        {
            return new(OblivionConfig.Default, null, null, Persisted: false, []);
        }

        try
        {
            return Parse(File.ReadAllText(_path), _path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "OBLIVION-CONFIG-READ-FAILED",
                $"Config could not be read: {exception.Message}");
        }
    }

    public static string FormatKey(OblivionConfigKey key)
    {
        return key switch
        {
            OblivionConfigKey.Appearance => "appearance",
            OblivionConfigKey.Newline => "newline",
            OblivionConfigKey.Style => "style",
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    public static string FormatValue(OblivionConfig config, OblivionConfigKey key)
    {
        return key switch
        {
            OblivionConfigKey.Appearance => config.Appearance.ToString().ToLowerInvariant(),
            OblivionConfigKey.Newline => config.NewlinePolicy.ToString().ToLowerInvariant(),
            OblivionConfigKey.Style => config.Style.ToString().ToLowerInvariant(),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
    }

    private OblivionConfigResult Parse(string source, string sourcePath)
    {
        string? appearanceValue = null;
        string? newlineValue = null;
        string? styleValue = null;
        foreach (string rawLine in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                return Failure("OBLIVION-CONFIG-TOML-INVALID", $"Invalid config assignment '{line}'.", sourcePath);
            }

            string key = line[..separator].Trim();
            string encoded = line[(separator + 1)..].Trim();
            if (encoded.Length < 2 || encoded[0] != '"' || encoded[^1] != '"')
            {
                return Failure("OBLIVION-CONFIG-TOML-INVALID", $"Config value for '{key}' must be a quoted string.", sourcePath);
            }

            if (!TryParseKey(key, out OblivionConfigKey parsedKey))
            {
                return Failure("OBLIVION-CONFIG-TOML-INVALID", $"Unknown config key '{key}'.", sourcePath);
            }

            string decoded = encoded[1..^1];
            bool duplicate = parsedKey switch
            {
                OblivionConfigKey.Appearance when appearanceValue is null => Assign(ref appearanceValue, decoded),
                OblivionConfigKey.Newline when newlineValue is null => Assign(ref newlineValue, decoded),
                OblivionConfigKey.Style when styleValue is null => Assign(ref styleValue, decoded),
                _ => true,
            };
            if (duplicate)
            {
                return Failure("OBLIVION-CONFIG-TOML-INVALID", $"Duplicate config key '{key}'.", sourcePath);
            }
        }

        OblivionConfig config = OblivionConfig.Default;
        (OblivionConfigKey Key, string? Value)[] assignments =
        [
            (OblivionConfigKey.Appearance, appearanceValue),
            (OblivionConfigKey.Newline, newlineValue),
            (OblivionConfigKey.Style, styleValue),
        ];
        foreach ((OblivionConfigKey key, string? value) in assignments)
        {
            if (value is null)
            {
                continue;
            }

            if (!TryApply(config, key, value, out config, out string expected))
            {
                return Failure(
                    "OBLIVION-CONFIG-VALUE-INVALID",
                    $"Invalid value '{value}' for '{FormatKey(key)}'. Expected {expected}.",
                    sourcePath);
            }
        }

        return new(config, null, null, Persisted: true, []);
    }

    private static bool Assign(ref string? target, string value)
    {
        target = value;
        return false;
    }

    private OblivionConfigResult Failure(string code, string message, string? path = null)
    {
        return new(null, null, null, Persisted: false, [new(code, "error", message, path ?? _path)]);
    }

    private static bool TryParseKey(string value, out OblivionConfigKey key)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "appearance":
                key = OblivionConfigKey.Appearance;
                return true;
            case "newline":
                key = OblivionConfigKey.Newline;
                return true;
            case "style":
                key = OblivionConfigKey.Style;
                return true;
            default:
                key = default;
                return false;
        }
    }

    private static bool TryApply(
        OblivionConfig current,
        OblivionConfigKey key,
        string value,
        out OblivionConfig next,
        out string expected)
    {
        string normalized = value.Trim().ToLowerInvariant();
        next = current;
        switch (key)
        {
            case OblivionConfigKey.Appearance:
                expected = "system, light, or dark";
                if (!TryAppearance(normalized, out OblivionAppearance appearance))
                {
                    return false;
                }
                next = current with { Appearance = appearance };
                return true;
            case OblivionConfigKey.Newline:
                expected = "preserve, lf, or crlf";
                if (!TryNewline(normalized, out OblivionNewlinePolicy newline))
                {
                    return false;
                }
                next = current with { NewlinePolicy = newline };
                return true;
            case OblivionConfigKey.Style:
                expected = "default";
                if (normalized != "default")
                {
                    return false;
                }
                next = current with { Style = OblivionStyleProfile.Default };
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }
    }

    private static bool TryAppearance(string value, out OblivionAppearance appearance)
    {
        appearance = value switch
        {
            "system" => OblivionAppearance.System,
            "light" => OblivionAppearance.Light,
            "dark" => OblivionAppearance.Dark,
            _ => default,
        };
        return value is "system" or "light" or "dark";
    }

    private static bool TryNewline(string value, out OblivionNewlinePolicy newline)
    {
        newline = value switch
        {
            "preserve" => OblivionNewlinePolicy.Preserve,
            "lf" => OblivionNewlinePolicy.Lf,
            "crlf" => OblivionNewlinePolicy.Crlf,
            _ => default,
        };
        return value is "preserve" or "lf" or "crlf";
    }

    private static string Serialize(OblivionConfig config)
    {
        return
            $"appearance = \"{FormatValue(config, OblivionConfigKey.Appearance)}\"\n" +
            $"newline = \"{FormatValue(config, OblivionConfigKey.Newline)}\"\n" +
            $"style = \"{FormatValue(config, OblivionConfigKey.Style)}\"\n";
    }
}
