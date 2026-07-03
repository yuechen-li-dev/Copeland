using System.Globalization;
using Tomlyn.Model;

namespace Machina.Presenter.Sample.Playback;

public static class PresenterPlaybackTomlParser
{
    private static readonly HashSet<string> ForbiddenProgrammingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "if",
        "then",
        "else",
        "loop",
        "while",
        "until",
        "for",
        "repeat",
        "script",
        "eval",
        "expr",
        "condition",
        "callback",
    };

    public static PresenterPlaybackScenario LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string toml = File.ReadAllText(fullPath);
        return LoadString(toml, fullPath);
    }

    public static PresenterPlaybackScenario LoadString(string toml, string sourcePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(toml);

        TomlTable model;
        try
        {
            model = Tomlyn.TomlSerializer.Deserialize<TomlTable>(toml, Tomlyn.TomlSerializerOptions.Default)
                ?? throw new PresenterPlaybackScenarioParseException(sourcePath, "Scenario root must be a TOML table.");
        }
        catch (Exception ex) when (ex is not PresenterPlaybackScenarioParseException)
        {
            throw new PresenterPlaybackScenarioParseException(sourcePath, ex.Message);
        }

        ValidateProgrammingBoundary(model, sourcePath, "<root>");

        TomlTable scenarioTable = GetRequiredTable(model, "scenario", sourcePath);
        TomlTable? outputTable = GetOptionalTable(model, "output");
        TomlTableArray stepTables = GetRequiredTableArray(model, "steps", sourcePath);
        TomlTableArray assertionTables = GetRequiredTableArray(model, "assertions", sourcePath);

        PresenterPlaybackViewport viewport = ParseViewport(GetRequiredTable(scenarioTable, "viewport", sourcePath), sourcePath);
        string id = GetRequiredString(scenarioTable, "id", sourcePath);
        string name = GetRequiredString(scenarioTable, "name", sourcePath);
        string section = GetRequiredString(scenarioTable, "section", sourcePath);
        string tab = GetRequiredString(scenarioTable, "tab", sourcePath);

        string? selectedCard = GetOptionalString(scenarioTable, "selectedCard");
        string? expandedCard = GetOptionalString(scenarioTable, "expandedCard");
        double? expandedCardBodyScroll = GetOptionalDouble(scenarioTable, "expandedCardBodyScroll");
        double? inspectorScroll = GetOptionalDouble(scenarioTable, "inspectorScroll");
        double? inspectorRawSourceScroll = GetOptionalDouble(scenarioTable, "inspectorRawSourceScroll");
        double? mainStackScroll = GetOptionalDouble(scenarioTable, "mainStackScroll");

        PresenterPlaybackOutputOptions output = new(
            CaptureFinalPng: GetOptionalBool(outputTable, "captureFinalPng") ?? true,
            CaptureTraceJson: GetOptionalBool(outputTable, "captureTraceJson") ?? true,
            CaptureManifest: GetOptionalBool(outputTable, "captureManifest") ?? true);

        IReadOnlyList<PresenterPlaybackStep> steps = stepTables
            .Select((table, index) => ParseStep(table, sourcePath, index))
            .ToArray();

        IReadOnlyList<PresenterPlaybackAssertion> assertions = assertionTables
            .Select((table, index) => ParseAssertion(table, sourcePath, index))
            .ToArray();

        return new PresenterPlaybackScenario(
            sourcePath,
            id,
            name,
            viewport,
            section,
            tab,
            selectedCard,
            expandedCard,
            expandedCardBodyScroll,
            inspectorScroll,
            inspectorRawSourceScroll,
            mainStackScroll,
            output,
            steps,
            assertions);
    }

    private static PresenterPlaybackViewport ParseViewport(TomlTable table, string sourcePath)
    {
        return new PresenterPlaybackViewport(
            Width: GetRequiredInt(table, "width", sourcePath),
            Height: GetRequiredInt(table, "height", sourcePath));
    }

    private static PresenterPlaybackStep ParseStep(TomlTable table, string sourcePath, int index)
    {
        string type = GetRequiredString(table, "type", sourcePath);

        return type switch
        {
            "wait" => new PresenterPlaybackWaitStep(GetRequiredInt(table, "ms", sourcePath)),
            "click" => new PresenterPlaybackClickStep(
                GetOptionalString(table, "target"),
                GetOptionalString(table, "card"),
                ParseOptionalPoint(table, "point", sourcePath) ?? ParseOptionalPointFromXY(table)),
            "wheel" => new PresenterPlaybackWheelStep(
                GetRequiredString(table, "target", sourcePath),
                GetOptionalString(table, "card"),
                GetRequiredDouble(table, "deltaY", sourcePath)),
            "key" => new PresenterPlaybackKeyStep(ParseKey(GetRequiredString(table, "key", sourcePath), sourcePath)),
            "drag" => new PresenterPlaybackDragStep(
                GetRequiredString(table, "target", sourcePath),
                GetOptionalString(table, "card"),
                GetOptionalDouble(table, "from"),
                GetOptionalDouble(table, "to"),
                ParseOptionalPoint(table, "from", sourcePath),
                ParseOptionalPoint(table, "to", sourcePath)),
            _ => throw new PresenterPlaybackScenarioParseException(
                sourcePath,
                $"Unknown playback step type '{type}' at steps[{index}]."),
        };
    }

    private static PresenterPlaybackAssertion ParseAssertion(TomlTable table, string sourcePath, int index)
    {
        string type = GetRequiredString(table, "type", sourcePath);
        string reason = GetRequiredAssertionReason(table, sourcePath, index);

        return type switch
        {
            "selected-card" => new PresenterPlaybackSelectedCardAssertion(
                GetRequiredString(table, "value", sourcePath),
                reason),
            "card-expanded" => new PresenterPlaybackCardExpandedAssertion(
                GetRequiredString(table, "card", sourcePath),
                GetRequiredBool(table, "value", sourcePath),
                reason),
            "scroll-offset-changed" => new PresenterPlaybackScrollOffsetChangedAssertion(
                GetRequiredString(table, "target", sourcePath),
                GetOptionalString(table, "card"),
                reason),
            "scroll-offset-greater-than" => new PresenterPlaybackScrollOffsetGreaterThanAssertion(
                GetRequiredString(table, "target", sourcePath),
                GetOptionalString(table, "card"),
                GetRequiredDouble(table, "value", sourcePath),
                reason),
            "scroll-offset-equals" => new PresenterPlaybackScrollOffsetEqualsAssertion(
                GetRequiredString(table, "target", sourcePath),
                GetOptionalString(table, "card"),
                GetRequiredDouble(table, "value", sourcePath),
                reason),
            "shell-mode" => new PresenterPlaybackShellModeAssertion(
                ParseShellMode(GetRequiredString(table, "value", sourcePath), sourcePath),
                reason),
            "region-exists" => new PresenterPlaybackRegionExistsAssertion(
                GetRequiredString(table, "target", sourcePath),
                GetOptionalString(table, "card"),
                reason),
            _ => throw new PresenterPlaybackScenarioParseException(
                sourcePath,
                $"Unknown playback assertion type '{type}' at assertions[{index}]."),
        };
    }

    private static PresenterKey ParseKey(string key, string sourcePath)
    {
        if (Enum.TryParse<PresenterKey>(key, ignoreCase: true, out PresenterKey value))
        {
            return value;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Unsupported presenter key '{key}'.");
    }

    private static PresenterShellMode ParseShellMode(string shellMode, string sourcePath)
    {
        if (Enum.TryParse<PresenterShellMode>(shellMode, ignoreCase: true, out PresenterShellMode value))
        {
            return value;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Unsupported shell mode '{shellMode}'.");
    }

    private static PresenterPlaybackPoint? ParseOptionalPoint(TomlTable table, string key, string sourcePath)
    {
        if (!table.TryGetValue(key, out object? rawValue) || rawValue is null)
        {
            return null;
        }

        if (rawValue is not TomlTable pointTable)
        {
            return null;
        }

        return new PresenterPlaybackPoint(
            GetRequiredDouble(pointTable, "x", sourcePath),
            GetRequiredDouble(pointTable, "y", sourcePath));
    }

    private static PresenterPlaybackPoint? ParseOptionalPointFromXY(TomlTable table)
    {
        double? x = GetOptionalDouble(table, "x");
        double? y = GetOptionalDouble(table, "y");
        if (x is null || y is null)
        {
            return null;
        }

        return new PresenterPlaybackPoint(x.Value, y.Value);
    }

    private static string GetRequiredAssertionReason(TomlTable table, string sourcePath, int index)
    {
        if (!table.TryGetValue("reason", out object? value))
        {
            throw new PresenterPlaybackScenarioParseException(
                sourcePath,
                $"Assertion at assertions[{index}] is invalid because every assertion must include a non-empty reason.");
        }

        string reason = value?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new PresenterPlaybackScenarioParseException(
                sourcePath,
                $"Assertion at assertions[{index}] is invalid because every assertion must include a non-empty reason.");
        }

        return reason;
    }

    private static TomlTable GetRequiredTable(TomlTable table, string key, string sourcePath)
    {
        if (table.TryGetValue(key, out object? value) &&
            value is TomlTable child)
        {
            return child;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Missing required TOML table '{key}'.");
    }

    private static TomlTable? GetOptionalTable(TomlTable table, string key)
    {
        return table.TryGetValue(key, out object? value) && value is TomlTable child
            ? child
            : null;
    }

    private static TomlTableArray GetRequiredTableArray(TomlTable table, string key, string sourcePath)
    {
        if (table.TryGetValue(key, out object? value) &&
            value is TomlTableArray array)
        {
            return array;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Missing required TOML table array '{key}'.");
    }

    private static string GetRequiredString(TomlTable table, string key, string sourcePath)
    {
        string? value = GetOptionalString(table, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PresenterPlaybackScenarioParseException(sourcePath, $"Missing required string '{key}'.");
        }

        return value;
    }

    private static string? GetOptionalString(TomlTable? table, string key)
    {
        if (table is null ||
            !table.TryGetValue(key, out object? value) ||
            value is null)
        {
            return null;
        }

        return value.ToString();
    }

    private static int GetRequiredInt(TomlTable table, string key, string sourcePath)
    {
        object? value = GetRequiredValue(table, key, sourcePath);
        if (value is long longValue)
        {
            return checked((int)longValue);
        }

        if (value is int intValue)
        {
            return intValue;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Expected integer '{key}'.");
    }

    private static bool GetRequiredBool(TomlTable table, string key, string sourcePath)
    {
        object? value = GetRequiredValue(table, key, sourcePath);
        if (value is bool boolValue)
        {
            return boolValue;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Expected boolean '{key}'.");
    }

    private static bool? GetOptionalBool(TomlTable? table, string key)
    {
        if (table is null ||
            !table.TryGetValue(key, out object? value) ||
            value is null)
        {
            return null;
        }

        return value is bool boolValue
            ? boolValue
            : null;
    }

    private static double GetRequiredDouble(TomlTable table, string key, string sourcePath)
    {
        double? value = GetOptionalDouble(table, key);
        if (value is null)
        {
            throw new PresenterPlaybackScenarioParseException(sourcePath, $"Expected numeric value '{key}'.");
        }

        return value.Value;
    }

    private static double? GetOptionalDouble(TomlTable? table, string key)
    {
        if (table is null ||
            !table.TryGetValue(key, out object? value) ||
            value is null)
        {
            return null;
        }

        return value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => null,
        };
    }

    private static object GetRequiredValue(TomlTable table, string key, string sourcePath)
    {
        if (table.TryGetValue(key, out object? value) &&
            value is not null)
        {
            return value;
        }

        throw new PresenterPlaybackScenarioParseException(sourcePath, $"Missing required value '{key}'.");
    }

    private static void ValidateProgrammingBoundary(object? value, string sourcePath, string path)
    {
        if (value is TomlTable table)
        {
            foreach ((string key, object? child) in table)
            {
                if (ForbiddenProgrammingKeys.Contains(key))
                {
                    throw new PresenterPlaybackScenarioParseException(
                        sourcePath,
                        $"Playback TOML must remain linear data, not a scripting language. Field '{key}' is not allowed at {path}.");
                }

                ValidateProgrammingBoundary(child, sourcePath, $"{path}.{key}");
            }

            return;
        }

        if (value is TomlTableArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                ValidateProgrammingBoundary(array[index], sourcePath, $"{path}[{index}]");
            }
        }
    }
}

public sealed class PresenterPlaybackScenarioParseException : Exception
{
    public PresenterPlaybackScenarioParseException(string sourcePath, string message)
        : base($"{sourcePath}: {message}")
    {
    }
}
