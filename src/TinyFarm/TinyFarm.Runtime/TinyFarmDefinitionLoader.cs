using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Copeland.TS.Tson;

namespace TinyFarm.Core;

public static class TinyFarmDefinitionLoader
{
    private const string ProductFileName = "tiny-farm-definitions.obj.ts";
    private const string ScheduleFileName = "tiny-farm-npc-schedules.obj.ts";
    private const string ScheduleCandidateFileName = "tiny-farm-npc-schedule-candidates.obj.ts";
    private static readonly IReadOnlySet<ActorId> ScheduledActors = new HashSet<ActorId>
    {
        TinyFarmIds.Elias,
        TinyFarmIds.Mara,
        TinyFarmIds.Sela
    };
    private static readonly string[] SceneFileNames =
    [
        "tiny-farm-scene-anchors.obj.ts",
        "tiny-farm-scene-layout.obj.ts",
        "tiny-farm-scene-objects.obj.ts",
        "tiny-farm-scene-routes.obj.ts",
        "tiny-farm-scenes.obj.ts"
    ];

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Content", ProductFileName);

    public static TinyFarmDefinitions Load(string? path = null)
    {
        return LoadCore(path, null, isM12: false);
    }

    public static TinyFarmDefinitions LoadM12(string? path = null)
    {
        string productPath = Path.GetFullPath(path ?? DefaultPath);
        string contentDirectory = Path.Combine(Path.GetDirectoryName(productPath)!, "M12");
        return LoadCore(productPath, contentDirectory, isM12: true);
    }

    private static TinyFarmDefinitions LoadCore(string? path, string? contentOverride, bool isM12)
    {
        string productPath = Path.GetFullPath(path ?? DefaultPath);
        string contentDirectory = contentOverride ?? Path.GetDirectoryName(productPath)!;
        string source = File.ReadAllText(productPath);
        TsonTable products = ReadTable(
            source,
            productPath,
            "Products",
            [
                ("id", TsonTypeKind.String),
                ("name", TsonTypeKind.String),
                ("buyPrice", TsonTypeKind.Number),
                ("sellPrice", TsonTypeKind.Number),
                ("cropId", TsonTypeKind.String),
                ("seedItemId", TsonTypeKind.String),
                ("harvestItemId", TsonTypeKind.String),
                ("growthDays", TsonTypeKind.Number),
                ("waterRequirement", TsonTypeKind.Number),
                ("yieldCount", TsonTypeKind.Number)
            ]);

        string identity = "tiny-farm-content-m2-sha256:"
            + Hash(Encoding.UTF8.GetBytes(source));
        var items = new List<ItemDefinition>();
        var crops = new List<CropDefinition>();
        for (int row = 0; row < products.RowCount; row++)
        {
            ProductId productId = new(Text(products, "id", row));
            items.Add(new ItemDefinition(
                productId,
                Text(products, "name", row),
                Integer(products, "buyPrice", row),
                Integer(products, "sellPrice", row)));
            string cropId = Text(products, "cropId", row);
            if (cropId.Length > 0)
            {
                crops.Add(new CropDefinition(
                    new CropId(cropId),
                    new ProductId(Text(products, "seedItemId", row)),
                    new ProductId(Text(products, "harvestItemId", row)),
                    Integer(products, "growthDays", row),
                    Integer(products, "waterRequirement", row),
                    Integer(products, "yieldCount", row)));
            }
        }

        (TinyFarmSceneCatalog scenes, SceneContentProvenance sceneProvenance) = LoadSceneCatalog(contentDirectory);
        (TinyFarmScheduleCatalog schedules, ScheduleContentProvenance scheduleProvenance) =
            LoadScheduleCatalog(Path.Combine(contentDirectory, ScheduleFileName), scenes);
        if (isM12)
        {
            identity = $"{identity};m12-scenes:{sceneProvenance.AggregateSha256};m12-schedules:{scheduleProvenance.AggregateSha256}";
        }
        return new TinyFarmDefinitions(
            identity,
            items,
            crops,
            scenes,
            sceneProvenance,
            schedules,
            scheduleProvenance);
    }

    public static (TinyFarmScheduleCatalog Catalog, ScheduleContentProvenance Provenance) LoadScheduleCatalog(
        string path,
        TinyFarmSceneCatalog scenes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(scenes);
        string fullPath = Path.GetFullPath(path);
        string fileName = Path.GetFileName(fullPath);

        var readWatch = Stopwatch.StartNew();
        string source = File.ReadAllText(fullPath);
        readWatch.Stop();

        var parseWatch = Stopwatch.StartNew();
        TsonTable table = ReadTable(
            source,
            fileName,
            "NpcSchedules",
            [
                ("windowId", TsonTypeKind.String),
                ("actorId", TsonTypeKind.String),
                ("day", TsonTypeKind.Enum),
                ("startMinute", TsonTypeKind.Number),
                ("endMinuteExclusive", TsonTypeKind.Number),
                ("regime", TsonTypeKind.Enum),
                ("requiredAnchorId", TsonTypeKind.String),
                ("priority", TsonTypeKind.Number),
                ("reason", TsonTypeKind.String)
            ]);
        string candidatePath = Path.Combine(Path.GetDirectoryName(fullPath)!, ScheduleCandidateFileName);
        TsonTable? candidateTable = null;
        if (File.Exists(candidatePath))
        {
            candidateTable = ReadTable(
                File.ReadAllText(candidatePath),
                ScheduleCandidateFileName,
                "UtilityCandidates",
                [
                    ("windowId", TsonTypeKind.String),
                    ("anchorId", TsonTypeKind.String),
                    ("considerationKind", TsonTypeKind.String),
                    ("baseScore", TsonTypeKind.Number),
                    ("currentLocationBonus", TsonTypeKind.Number)
                ]);
        }
        parseWatch.Stop();

        var materializeWatch = Stopwatch.StartNew();
        var windows = new List<TinyFarmScheduleWindow>(table.RowCount);
        for (int row = 0; row < table.RowCount; row++)
        {
            windows.Add(new TinyFarmScheduleWindow(
                Text(table, "windowId", row),
                new ActorId(Text(table, "actorId", row)),
                ScheduleDay(table, "day", row),
                Integer(table, "startMinute", row),
                Integer(table, "endMinuteExclusive", row),
                ScheduleRegime(table, "regime", row),
                OptionalAnchor(Text(table, "requiredAnchorId", row)),
                Integer(table, "priority", row),
                Text(table, "reason", row)));
        }
        var candidates = new List<TinyFarmUtilityCandidate>(candidateTable?.RowCount ?? 0);
        if (candidateTable is not null)
        {
            for (int row = 0; row < candidateTable.RowCount; row++)
            {
                candidates.Add(new TinyFarmUtilityCandidate(
                    Text(candidateTable, "windowId", row),
                    new SceneAnchorId(Text(candidateTable, "anchorId", row)),
                    Text(candidateTable, "considerationKind", row),
                    Number(candidateTable, "baseScore", row) / 100d,
                    Number(candidateTable, "currentLocationBonus", row) / 100d));
            }
        }
        materializeWatch.Stop();

        var validationWatch = Stopwatch.StartNew();
        TinyFarmScheduleCatalog.Validate(windows, candidates, ScheduledActors, scenes);
        validationWatch.Stop();

        var indexWatch = Stopwatch.StartNew();
        var catalog = new TinyFarmScheduleCatalog(windows, candidates);
        indexWatch.Stop();

        var provenance = new ScheduleContentProvenance(
            "object-typescript-record-table-v1",
            fileName,
            Hash(Encoding.UTF8.GetBytes(source)),
            Encoding.UTF8.GetByteCount(source),
            ScheduleSemanticHash(catalog.Windows, catalog.Candidates),
            readWatch.Elapsed.TotalMilliseconds,
            parseWatch.Elapsed.TotalMilliseconds,
            materializeWatch.Elapsed.TotalMilliseconds,
            validationWatch.Elapsed.TotalMilliseconds,
            indexWatch.Elapsed.TotalMilliseconds);
        return (catalog, provenance);
    }

    public static (TinyFarmSceneCatalog Catalog, SceneContentProvenance Provenance) LoadSceneCatalog(
        string contentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        var readWatch = Stopwatch.StartNew();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string fileName in SceneFileNames)
        {
            string path = Path.GetFullPath(Path.Combine(contentDirectory, fileName));
            sources.Add(fileName, File.ReadAllText(path));
        }
        readWatch.Stop();

        var parseWatch = Stopwatch.StartNew();
        TsonTable scenesTable = ReadTable(sources["tiny-farm-scenes.obj.ts"], "tiny-farm-scenes.obj.ts", "Scenes",
            [("id", TsonTypeKind.String), ("label", TsonTypeKind.String), ("width", TsonTypeKind.Number), ("height", TsonTypeKind.Number)]);
        TsonTable objectsTable = ReadTable(sources["tiny-farm-scene-objects.obj.ts"], "tiny-farm-scene-objects.obj.ts", "SceneObjects",
            [("sceneId", TsonTypeKind.String), ("objectId", TsonTypeKind.String), ("kind", TsonTypeKind.String), ("label", TsonTypeKind.String), ("blocksMovement", TsonTypeKind.Boolean), ("semanticReference", TsonTypeKind.Enum)]);
        TsonTable layoutTable = ReadTable(sources["tiny-farm-scene-layout.obj.ts"], "tiny-farm-scene-layout.obj.ts", "SceneLayout",
            [("sceneId", TsonTypeKind.String), ("objectId", TsonTypeKind.String), ("x", TsonTypeKind.Number), ("y", TsonTypeKind.Number), ("width", TsonTypeKind.Number), ("height", TsonTypeKind.Number), ("layer", TsonTypeKind.Number)]);
        TsonTable anchorsTable = ReadTable(sources["tiny-farm-scene-anchors.obj.ts"], "tiny-farm-scene-anchors.obj.ts", "SceneAnchors",
            [("anchorId", TsonTypeKind.String), ("sceneId", TsonTypeKind.String), ("x", TsonTypeKind.Number), ("y", TsonTypeKind.Number), ("kind", TsonTypeKind.String), ("semanticLocation", TsonTypeKind.Enum), ("semanticObject", TsonTypeKind.Enum), ("facing", TsonTypeKind.Enum), ("arrivalRadiusUnits", TsonTypeKind.Number)]);
        TsonTable routesTable = ReadTable(sources["tiny-farm-scene-routes.obj.ts"], "tiny-farm-scene-routes.obj.ts", "SceneRoutes",
            [("routeId", TsonTypeKind.String), ("sourceScene", TsonTypeKind.String), ("triggerObject", TsonTypeKind.String), ("targetScene", TsonTypeKind.String), ("targetAnchor", TsonTypeKind.String), ("interactionLabel", TsonTypeKind.String)]);
        parseWatch.Stop();

        var materializeWatch = Stopwatch.StartNew();
        List<SceneObjectRow> objects = ReadObjects(objectsTable);
        List<SceneLayoutInput> layout = ReadLayout(layoutTable);
        List<SceneAnchorDefinition> anchors = ReadAnchors(anchorsTable);
        List<SceneRoute> routes = ReadRoutes(routesTable);
        var definitions = new List<SceneDefinition>();
        for (int row = 0; row < scenesTable.RowCount; row++)
        {
            SceneId sceneId = new(Text(scenesTable, "id", row));
            definitions.Add(new SceneDefinition(
                sceneId,
                Text(scenesTable, "label", row),
                Integer(scenesTable, "width", row),
                Integer(scenesTable, "height", row),
                objects.Where(item => item.Scene == sceneId).Select(item => item.Definition),
                layout.Where(item => item.Scene == sceneId).Select(item => item.Row),
                anchors.Where(item => item.Scene == sceneId),
                routes.Where(item => item.SourceScene == sceneId)));
        }

        RejectUnknownSceneRows(definitions, objects, layout, anchors, routes);
        var catalog = new TinyFarmSceneCatalog(definitions);
        materializeWatch.Stop();

        SceneContentSource[] provenanceSources = SceneFileNames
            .Select(fileName => new SceneContentSource(
                fileName,
                Hash(Encoding.UTF8.GetBytes(sources[fileName])),
                Encoding.UTF8.GetByteCount(sources[fileName])))
            .ToArray();
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string fileName in SceneFileNames)
        {
            aggregate.AppendData(Encoding.UTF8.GetBytes(fileName));
            aggregate.AppendData([0]);
            aggregate.AppendData(Encoding.UTF8.GetBytes(sources[fileName]));
        }
        var provenance = new SceneContentProvenance(
            "object-typescript-record-table-v1",
            Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant(),
            provenanceSources,
            readWatch.Elapsed.TotalMilliseconds,
            parseWatch.Elapsed.TotalMilliseconds,
            materializeWatch.Elapsed.TotalMilliseconds);
        return (catalog, provenance);
    }

    private static List<SceneObjectRow> ReadObjects(TsonTable table)
    {
        var rows = new List<SceneObjectRow>();
        for (int row = 0; row < table.RowCount; row++)
        {
            SceneId scene = new(Text(table, "sceneId", row));
            SceneObjectKind kind = ParseEnum<SceneObjectKind>(Text(table, "kind", row), "object kind");
            string? semanticReference = OptionalText(table, "semanticReference", row);
            ValidateSemanticReference(kind, semanticReference);
            rows.Add(new SceneObjectRow(
                scene,
                new SceneObjectDefinition(
                    new SceneObjectId(Text(table, "objectId", row)),
                    kind,
                    Text(table, "label", row),
                    Boolean(table, "blocksMovement", row),
                    semanticReference)));
        }
        return rows;
    }

    private static List<SceneLayoutInput> ReadLayout(TsonTable table)
    {
        var rows = new List<SceneLayoutInput>();
        for (int row = 0; row < table.RowCount; row++)
        {
            rows.Add(new SceneLayoutInput(
                new SceneId(Text(table, "sceneId", row)),
                new SceneLayoutRow(
                    new SceneObjectId(Text(table, "objectId", row)),
                    Integer(table, "x", row),
                    Integer(table, "y", row),
                    Integer(table, "width", row),
                    Integer(table, "height", row),
                    Integer(table, "layer", row))));
        }
        return rows;
    }

    private static List<SceneAnchorDefinition> ReadAnchors(TsonTable table)
    {
        var rows = new List<SceneAnchorDefinition>();
        for (int row = 0; row < table.RowCount; row++)
        {
            string? location = OptionalText(table, "semanticLocation", row);
            string? semanticObject = OptionalText(table, "semanticObject", row);
            string? facing = OptionalText(table, "facing", row);
            rows.Add(new SceneAnchorDefinition(
                new SceneAnchorId(Text(table, "anchorId", row)),
                new SceneId(Text(table, "sceneId", row)),
                ScenePosition.FromGrid(new GridPosition(Integer(table, "x", row), Integer(table, "y", row))),
                ParseEnum<SceneAnchorKind>(Text(table, "kind", row), "anchor kind"),
                location is null ? null : new LocationId(location),
                semanticObject is null ? null : new SceneObjectId(semanticObject),
                facing is null ? null : ParseEnum<ActorFacing>(facing, "facing"),
                Integer(table, "arrivalRadiusUnits", row)));
        }
        return rows;
    }

    private static List<SceneRoute> ReadRoutes(TsonTable table)
    {
        var rows = new List<SceneRoute>();
        for (int row = 0; row < table.RowCount; row++)
        {
            rows.Add(new SceneRoute(
                new SceneRouteId(Text(table, "routeId", row)),
                new SceneId(Text(table, "sourceScene", row)),
                new SceneObjectId(Text(table, "triggerObject", row)),
                new SceneId(Text(table, "targetScene", row)),
                new SceneAnchorId(Text(table, "targetAnchor", row)),
                Text(table, "interactionLabel", row)));
        }
        return rows;
    }

    private static void RejectUnknownSceneRows(
        IReadOnlyList<SceneDefinition> scenes,
        IEnumerable<SceneObjectRow> objects,
        IEnumerable<SceneLayoutInput> layout,
        IEnumerable<SceneAnchorDefinition> anchors,
        IEnumerable<SceneRoute> routes)
    {
        HashSet<SceneId> known = scenes.Select(scene => scene.Id).ToHashSet();
        IEnumerable<SceneId> referenced = objects.Select(item => item.Scene)
            .Concat(layout.Select(item => item.Scene))
            .Concat(anchors.Select(item => item.Scene))
            .Concat(routes.Select(item => item.SourceScene));
        foreach (SceneId scene in referenced)
        {
            if (!known.Contains(scene))
            {
                throw new InvalidDataException($"Scene content row references unknown scene '{scene}'.");
            }
        }
    }

    private static TsonTable ReadTable(
        string source,
        string sourceName,
        string expectedTable,
        IReadOnlyList<(string Name, TsonTypeKind Kind)> expectedColumns)
    {
        TsonReadResult read = TsonDocumentReader.ReadSelfDescribed(source, TsonDocumentProfile.ObjectTypeScript);
        if (!read.Success || read.Document?.Root is not TsonTable table)
        {
            string diagnostics = string.Join("; ", read.SyntaxDiagnostics
                .Select(item => item.ToString())
                .Concat(read.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
            throw new InvalidDataException($"TinyFarm TSON table '{sourceName}' is invalid: {diagnostics}");
        }
        if (!string.Equals(table.Schema.Name, expectedTable, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"TinyFarm TSON table '{sourceName}' has root '{table.Schema.Name}', expected '{expectedTable}'.");
        }
        if (table.Columns.Count != expectedColumns.Count)
        {
            throw new InvalidDataException(
                $"TinyFarm TSON table '{sourceName}' has {table.Columns.Count} columns, expected {expectedColumns.Count}.");
        }
        for (int index = 0; index < expectedColumns.Count; index++)
        {
            (string expectedName, TsonTypeKind expectedKind) = expectedColumns[index];
            TsonTableColumn column = table.Columns[index];
            if (!string.Equals(column.Schema.Name, expectedName, StringComparison.Ordinal)
                || column.Schema.ElementType.Kind != expectedKind)
            {
                throw new InvalidDataException(
                    $"TinyFarm TSON table '{sourceName}' column {index} must be '{expectedName}: {expectedKind}'.");
            }
        }
        return table;
    }

    private static TsonValue Cell(TsonTable table, string column, int row)
    {
        return table.Columns.Single(item => item.Schema.Name == column).Cells[row];
    }

    private static string Text(TsonTable table, string column, int row)
    {
        return ((TsonString)Cell(table, column, row)).Value;
    }

    private static bool Boolean(TsonTable table, string column, int row)
    {
        return ((TsonBoolean)Cell(table, column, row)).Value;
    }

    private static int Integer(TsonTable table, string column, int row)
    {
        double value = ((TsonNumber)Cell(table, column, row)).Value;
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new InvalidDataException(
                $"TinyFarm TSON table '{table.Schema.Name}' column '{column}' row {row} requires an exact 32-bit integer.");
        }
        return (int)value;
    }

    private static double Number(TsonTable table, string column, int row)
    {
        return ((TsonNumber)Cell(table, column, row)).Value;
    }

    private static TinyFarmScheduleDay ScheduleDay(TsonTable table, string column, int row)
    {
        TsonEnum value = (TsonEnum)Cell(table, column, row);
        if (!value.EnumIdentity.EndsWith("#ScheduleDay", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"TinyFarm TSON table '{table.Schema.Name}' column '{column}' row {row} requires ScheduleDay.");
        }
        if (value.CaseName == "Every" && value.Payloads.Count == 0)
        {
            return TinyFarmScheduleDay.EveryDay;
        }
        if (value.CaseName == "Day"
            && value.Payloads.Count == 1
            && value.Payloads[0].Value is TsonNumber dayValue)
        {
            try
            {
                double number = dayValue.Value;
                if (number != Math.Truncate(number))
                {
                    throw new ArgumentOutOfRangeException(nameof(row));
                }
                return TinyFarmScheduleDay.Day((int)number);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException(
                    $"TinyFarm TSON table '{table.Schema.Name}' column '{column}' row {row} has invalid day payload.",
                    exception);
            }
        }
        throw new InvalidDataException(
            $"TinyFarm TSON table '{table.Schema.Name}' column '{column}' row {row} requires ScheduleDay.Every or ScheduleDay.Day(1..7).");
    }

    private static TinyFarmScheduleRegime ScheduleRegime(TsonTable table, string column, int row)
    {
        TsonEnum value = (TsonEnum)Cell(table, column, row);
        if (!value.EnumIdentity.EndsWith("#ScheduleRegime", StringComparison.Ordinal)
            || value.Payloads.Count != 0
            || !Enum.TryParse(value.CaseName, out TinyFarmScheduleRegime regime))
        {
            throw new InvalidDataException($"TinyFarm TSON table '{table.Schema.Name}' column '{column}' row {row} requires ScheduleRegime.Required or ScheduleRegime.Open.");
        }
        return regime;
    }

    private static SceneAnchorId? OptionalAnchor(string value)
    {
        return value.Length == 0 ? null : new SceneAnchorId(value);
    }

    private static string ScheduleSemanticHash(
        IEnumerable<TinyFarmScheduleWindow> windows,
        IEnumerable<TinyFarmUtilityCandidate> candidates)
    {
        IEnumerable<string> signatures = windows.Select(window => string.Join(
            '|',
            window.Id,
            window.Actor.Value,
            window.Day.ToString(),
            window.StartMinute,
            window.EndMinuteExclusive,
            window.Regime,
            window.RequiredAnchor?.Value ?? string.Empty,
            window.Priority,
            window.Reason))
            .Concat(candidates.Select(candidate => string.Join(
                '|',
                candidate.WindowId,
                candidate.Anchor.Value,
                candidate.ConsiderationKind,
                candidate.BaseScore.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                candidate.CurrentLocationBonus.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
        return Hash(Encoding.UTF8.GetBytes(string.Join('\n', signatures)));
    }

    private static string? OptionalText(TsonTable table, string column, int row)
    {
        TsonEnum value = (TsonEnum)Cell(table, column, row);
        if (!value.EnumIdentity.EndsWith("#OptionalText", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"TinyFarm TSON table '{table.Schema.Name}' column '{column}' row {row} requires the OptionalText nominal enum.");
        }
        if (value.CaseName == "None" && value.Payloads.Count == 0)
        {
            return null;
        }
        if (value.CaseName == "Some"
            && value.Payloads.Count == 1
            && value.Payloads[0].Value is TsonString text)
        {
            return text.Value;
        }
        throw new InvalidDataException(
            $"TinyFarm TSON table '{table.Schema.Name}' column '{column}' row {row} requires OptionalText.None or OptionalText.Some(string).");
    }

    private static T ParseEnum<T>(string value, string label)
        where T : struct, Enum
    {
        if (!Enum.TryParse(value, ignoreCase: false, out T result)
            || !string.Equals(result.ToString(), value, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unknown TinyFarm scene {label} '{value}'.");
        }
        return result;
    }

    private static void ValidateSemanticReference(SceneObjectKind kind, string? reference)
    {
        bool valid = kind switch
        {
            SceneObjectKind.Plot => reference == TinyFarmIds.PlotOne.Value || reference == TinyFarmIds.PlotTwo.Value,
            SceneObjectKind.Shop => reference == TinyFarmIds.GeneralStore.Value,
            SceneObjectKind.Bed => reference == TinyFarmIds.Elias.Value
                || reference == TinyFarmIds.Mara.Value
                || reference == TinyFarmIds.Sela.Value,
            _ => reference is null
        };
        if (!valid)
        {
            throw new InvalidDataException($"Scene object kind '{kind}' has invalid semantic reference '{reference}'.");
        }
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record SceneObjectRow(SceneId Scene, SceneObjectDefinition Definition);
    private sealed record SceneLayoutInput(SceneId Scene, SceneLayoutRow Row);
}
