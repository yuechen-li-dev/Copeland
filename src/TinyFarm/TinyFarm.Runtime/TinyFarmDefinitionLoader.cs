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
        return LoadCore(path, null, contentMilestone: null);
    }

    public static TinyFarmDefinitions LoadM12(string? path = null)
    {
        string productPath = Path.GetFullPath(path ?? DefaultPath);
        string contentDirectory = Path.Combine(Path.GetDirectoryName(productPath)!, "M12");
        return LoadCore(productPath, contentDirectory, contentMilestone: "m12");
    }

    public static TinyFarmDefinitions LoadM14(string? path = null)
    {
        string productPath = Path.GetFullPath(path ?? DefaultPath);
        string contentDirectory = Path.Combine(Path.GetDirectoryName(productPath)!, "M14");
        return LoadCore(productPath, contentDirectory, contentMilestone: "m14");
    }

    public static TinyFarmDefinitions LoadM18(string? path = null)
    {
        string defaultProductPath = Path.Combine(
            Path.GetDirectoryName(DefaultPath)!,
            "M18",
            ProductFileName);
        string productPath = Path.GetFullPath(path ?? defaultProductPath);
        string contentDirectory = Path.Combine(Path.GetDirectoryName(DefaultPath)!, "M14");
        TinyFarmDefinitions baseline = LoadCore(productPath, contentDirectory, contentMilestone: "m18-base");
        string foragePath = Path.Combine(Path.GetDirectoryName(productPath)!, "tiny-farm-forage-nodes.obj.ts");
        string forageSource = File.ReadAllText(foragePath);
        TsonTable forageTable = ReadTable(
            forageSource,
            foragePath,
            "ForageNodes",
            [
                ("id", TsonTypeKind.String),
                ("sceneId", TsonTypeKind.String),
                ("x", TsonTypeKind.Number),
                ("y", TsonTypeKind.Number),
                ("productId", TsonTypeKind.String),
                ("yieldCount", TsonTypeKind.Number)
            ]);
        var forageNodes = new List<ForageNodeDefinition>();
        for (int row = 0; row < forageTable.RowCount; row++)
        {
            forageNodes.Add(new ForageNodeDefinition(
                new ForageNodeId(Text(forageTable, "id", row)),
                new SceneId(Text(forageTable, "sceneId", row)),
                ScenePosition.FromGrid(new GridPosition(
                    Integer(forageTable, "x", row),
                    Integer(forageTable, "y", row))),
                new ProductId(Text(forageTable, "productId", row)),
                Integer(forageTable, "yieldCount", row)));
        }

        TinyFarmSceneCatalog scenes = AddForageSceneObjects(baseline.Scenes, forageNodes, baseline.Items);
        string identity = baseline.Identity + ";m18-forage:" + Hash(Encoding.UTF8.GetBytes(forageSource));
        return new TinyFarmDefinitions(
            identity,
            baseline.Items,
            baseline.Crops,
            scenes,
            baseline.SceneContent,
            baseline.Schedules,
            baseline.ScheduleContent,
            forageNodes);
    }

    public static TinyFarmDefinitions LoadM19(string? path = null)
    {
        string contentRoot = Path.GetDirectoryName(DefaultPath)!;
        string m19Directory = Path.Combine(contentRoot, "M19");
        string productPath = Path.GetFullPath(path ?? Path.Combine(m19Directory, ProductFileName));
        string sceneDirectory = Path.Combine(contentRoot, "M14");
        TinyFarmDefinitions baseline = LoadCore(productPath, sceneDirectory, contentMilestone: "m19-base");

        string foragePath = Path.Combine(contentRoot, "M18", "tiny-farm-forage-nodes.obj.ts");
        string forageSource = File.ReadAllText(foragePath);
        TsonTable forageTable = ReadTable(
            forageSource,
            foragePath,
            "ForageNodes",
            [
                ("id", TsonTypeKind.String),
                ("sceneId", TsonTypeKind.String),
                ("x", TsonTypeKind.Number),
                ("y", TsonTypeKind.Number),
                ("productId", TsonTypeKind.String),
                ("yieldCount", TsonTypeKind.Number)
            ]);
        var forageNodes = new List<ForageNodeDefinition>();
        for (int row = 0; row < forageTable.RowCount; row++)
        {
            forageNodes.Add(new ForageNodeDefinition(
                new ForageNodeId(Text(forageTable, "id", row)),
                new SceneId(Text(forageTable, "sceneId", row)),
                ScenePosition.FromGrid(new GridPosition(
                    Integer(forageTable, "x", row),
                    Integer(forageTable, "y", row))),
                new ProductId(Text(forageTable, "productId", row)),
                Integer(forageTable, "yieldCount", row)));
        }

        TinyFarmSceneCatalog scenesWithForage = AddForageSceneObjects(baseline.Scenes, forageNodes, baseline.Items);
        string stationPath = Path.Combine(m19Directory, "tiny-farm-cooking-stations.obj.ts");
        string stationSource = File.ReadAllText(stationPath);
        TsonTable stations = ReadTable(
            stationSource,
            stationPath,
            "CookingStations",
            [
                ("id", TsonTypeKind.String),
                ("sceneId", TsonTypeKind.String),
                ("x", TsonTypeKind.Number),
                ("y", TsonTypeKind.Number),
                ("label", TsonTypeKind.String)
            ]);
        var stationRows = new List<(SceneObjectId Id, SceneId Scene, int X, int Y, string Label)>();
        for (int row = 0; row < stations.RowCount; row++)
        {
            stationRows.Add((
                new SceneObjectId(Text(stations, "id", row)),
                new SceneId(Text(stations, "sceneId", row)),
                Integer(stations, "x", row),
                Integer(stations, "y", row),
                Text(stations, "label", row)));
        }
        TinyFarmSceneCatalog scenes = AddCookingStations(scenesWithForage, stationRows);

        string recipesPath = Path.Combine(m19Directory, "tiny-farm-cooking-recipes.obj.ts");
        string recipesSource = File.ReadAllText(recipesPath);
        TsonTable recipes = ReadTable(
            recipesSource,
            recipesPath,
            "CookingRecipes",
            [
                ("recipeId", TsonTypeKind.String),
                ("stationKind", TsonTypeKind.String),
                ("outputProductId", TsonTypeKind.String),
                ("outputCount", TsonTypeKind.Number)
            ]);
        string inputsPath = Path.Combine(m19Directory, "tiny-farm-cooking-recipe-inputs.obj.ts");
        string inputsSource = File.ReadAllText(inputsPath);
        TsonTable inputs = ReadTable(
            inputsSource,
            inputsPath,
            "CookingRecipeInputs",
            [
                ("recipeId", TsonTypeKind.String),
                ("productId", TsonTypeKind.String),
                ("count", TsonTypeKind.Number)
            ]);
        var inputRows = new List<(CookingRecipeId Recipe, CookingRecipeInput Input)>();
        for (int row = 0; row < inputs.RowCount; row++)
        {
            inputRows.Add((
                new CookingRecipeId(Text(inputs, "recipeId", row)),
                new CookingRecipeInput(
                    new ProductId(Text(inputs, "productId", row)),
                    Integer(inputs, "count", row))));
        }
        var cookingRecipes = new List<CookingRecipeDefinition>();
        for (int row = 0; row < recipes.RowCount; row++)
        {
            var recipeId = new CookingRecipeId(Text(recipes, "recipeId", row));
            if (!Enum.TryParse(Text(recipes, "stationKind", row), out CookingStationKind stationKind))
            {
                throw new InvalidDataException($"Cooking recipe '{recipeId}' has an unknown station kind.");
            }
            cookingRecipes.Add(new CookingRecipeDefinition(
                recipeId,
                stationKind,
                inputRows.Where(input => input.Recipe == recipeId).Select(input => input.Input).ToArray(),
                new ProductId(Text(recipes, "outputProductId", row)),
                Integer(recipes, "outputCount", row)));
        }
        if (inputRows.Any(input => !cookingRecipes.Any(recipe => recipe.Id == input.Recipe)))
        {
            throw new InvalidDataException("Cooking recipe inputs reference an unknown recipe.");
        }
        if (stationRows.Count == 0 && cookingRecipes.Count > 0)
        {
            throw new InvalidDataException("Cooking recipes require one compatible authored cooking station.");
        }

        string identity = string.Join(
            ';',
            baseline.Identity,
            "m18-forage:" + Hash(Encoding.UTF8.GetBytes(forageSource)),
            "m19-stations:" + Hash(Encoding.UTF8.GetBytes(stationSource)),
            "m19-recipes:" + Hash(Encoding.UTF8.GetBytes(recipesSource + inputsSource)));
        return new TinyFarmDefinitions(
            identity,
            baseline.Items,
            baseline.Crops,
            scenes,
            baseline.SceneContent,
            baseline.Schedules,
            baseline.ScheduleContent,
            forageNodes,
            cookingRecipes);
    }

    public static TinyFarmDefinitions LoadM20(string? path = null)
    {
        string contentRoot = Path.GetDirectoryName(DefaultPath)!;
        string m20Directory = Path.Combine(contentRoot, "M20");
        string productPath = Path.GetFullPath(path ?? Path.Combine(m20Directory, ProductFileName));
        TinyFarmDefinitions baseline = LoadM19(productPath);
        string treePath = Path.Combine(m20Directory, "tiny-farm-trees.obj.ts");
        string treeSource = File.ReadAllText(treePath);
        TsonTable table = ReadTable(
            treeSource,
            treePath,
            "Trees",
            [
                ("id", TsonTypeKind.String),
                ("sceneId", TsonTypeKind.String),
                ("x", TsonTypeKind.Number),
                ("y", TsonTypeKind.Number),
                ("yieldProductId", TsonTypeKind.String),
                ("yieldCount", TsonTypeKind.Number)
            ]);
        var trees = new List<TreeDefinition>();
        for (int row = 0; row < table.RowCount; row++)
        {
            trees.Add(new TreeDefinition(
                new TreeId(Text(table, "id", row)),
                new SceneId(Text(table, "sceneId", row)),
                ScenePosition.FromGrid(new GridPosition(
                    Integer(table, "x", row),
                    Integer(table, "y", row))),
                new ProductId(Text(table, "yieldProductId", row)),
                Integer(table, "yieldCount", row)));
        }

        TinyFarmSceneCatalog scenes = AddTrees(baseline.Scenes, trees, baseline.Items);
        string identity = baseline.Identity + ";m20-trees:" + Hash(Encoding.UTF8.GetBytes(treeSource));
        return new TinyFarmDefinitions(
            identity,
            baseline.Items,
            baseline.Crops,
            scenes,
            baseline.SceneContent,
            baseline.Schedules,
            baseline.ScheduleContent,
            baseline.ForageNodes,
            baseline.CookingRecipes,
            trees);
    }

    private static TinyFarmSceneCatalog AddTrees(
        TinyFarmSceneCatalog baseline,
        IReadOnlyList<TreeDefinition> trees,
        IReadOnlyList<ItemDefinition> products)
    {
        if (trees.Select(tree => tree.Id).Distinct().Count() != trees.Count)
        {
            throw new InvalidDataException("TinyFarm tree identities must be unique.");
        }
        foreach (TreeDefinition tree in trees)
        {
            if (!products.Any(product => product.Id == tree.YieldProduct))
            {
                throw new InvalidDataException($"Tree '{tree.Id}' references unknown product '{tree.YieldProduct}'.");
            }
        }

        SceneDefinition[] scenes = baseline.All.Select(scene =>
        {
            TreeDefinition[] additions = trees.Where(tree => tree.Scene == scene.Id).ToArray();
            return new SceneDefinition(
                scene.Id,
                scene.Name,
                scene.Width,
                scene.Height,
                scene.Objects.Concat(additions.Select(tree => new SceneObjectDefinition(
                    new SceneObjectId(tree.Id.Value),
                    SceneObjectKind.Tree,
                    "Tree",
                    BlocksMovement: true,
                    SemanticReference: tree.YieldProduct.Value))),
                scene.Layout.Concat(additions.Select(tree => new SceneLayoutRow(
                    new SceneObjectId(tree.Id.Value),
                    tree.Position.Tile.X,
                    tree.Position.Tile.Y,
                    1,
                    1,
                    0))),
                scene.Anchors,
                scene.Routes);
        }).ToArray();
        if (trees.Any(tree => !scenes.Any(scene => scene.Id == tree.Scene)))
        {
            throw new InvalidDataException("TinyFarm tree references an unknown scene.");
        }
        return new TinyFarmSceneCatalog(scenes);
    }

    private static TinyFarmSceneCatalog AddCookingStations(
        TinyFarmSceneCatalog baseline,
        IReadOnlyList<(SceneObjectId Id, SceneId Scene, int X, int Y, string Label)> stations)
    {
        if (stations.Select(station => station.Id).Distinct().Count() != stations.Count)
        {
            throw new InvalidDataException("TinyFarm cooking station identities must be unique.");
        }
        SceneDefinition[] scenes = baseline.All.Select(scene =>
        {
            var additions = stations.Where(station => station.Scene == scene.Id).ToArray();
            return new SceneDefinition(
                scene.Id,
                scene.Name,
                scene.Width,
                scene.Height,
                scene.Objects.Concat(additions.Select(station => new SceneObjectDefinition(
                    station.Id,
                    SceneObjectKind.CookingStation,
                    station.Label,
                    BlocksMovement: true,
                    SemanticReference: CookingStationKind.Cooking.ToString()))),
                scene.Layout.Concat(additions.Select(station => new SceneLayoutRow(
                    station.Id,
                    station.X,
                    station.Y,
                    1,
                    1,
                    0))),
                scene.Anchors,
                scene.Routes);
        }).ToArray();
        if (stations.Any(station => !scenes.Any(scene => scene.Id == station.Scene)))
        {
            throw new InvalidDataException("TinyFarm cooking station references an unknown scene.");
        }
        return new TinyFarmSceneCatalog(scenes);
    }

    private static TinyFarmSceneCatalog AddForageSceneObjects(
        TinyFarmSceneCatalog baseline,
        IReadOnlyList<ForageNodeDefinition> forageNodes,
        IReadOnlyList<ItemDefinition> products)
    {
        foreach (ForageNodeDefinition node in forageNodes)
        {
            if (!products.Any(product => product.Id == node.Product))
            {
                throw new InvalidDataException($"Forage node '{node.Id}' references unknown product '{node.Product}'.");
            }
        }

        SceneDefinition[] scenes = baseline.All
            .Select(scene =>
            {
                ForageNodeDefinition[] additions = forageNodes
                    .Where(node => node.Scene == scene.Id)
                    .ToArray();
                SceneObjectDefinition[] objects = scene.Objects
                    .Concat(additions.Select(node => new SceneObjectDefinition(
                        new SceneObjectId(node.Id.Value),
                        SceneObjectKind.Forage,
                        products.Single(product => product.Id == node.Product).Name,
                        BlocksMovement: false,
                        SemanticReference: node.Product.Value)))
                    .ToArray();
                SceneLayoutRow[] layout = scene.Layout
                    .Concat(additions.Select(node => new SceneLayoutRow(
                        new SceneObjectId(node.Id.Value),
                        node.Position.Tile.X,
                        node.Position.Tile.Y,
                        1,
                        1,
                        0)))
                    .ToArray();
                return new SceneDefinition(
                    scene.Id,
                    scene.Name,
                    scene.Width,
                    scene.Height,
                    objects,
                    layout,
                    scene.Anchors,
                    scene.Routes);
            })
            .ToArray();
        return new TinyFarmSceneCatalog(scenes);
    }

    private static TinyFarmDefinitions LoadCore(
        string? path,
        string? contentOverride,
        string? contentMilestone)
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
        ForageNodeDefinition[] forageNodes = scenes.All
            .SelectMany(scene => scene.Objects
                .Where(sceneObject => sceneObject.Kind == SceneObjectKind.Forage
                    && items.Any(item => item.Id.Value == sceneObject.SemanticReference))
                .Select(sceneObject =>
                {
                    SceneLayoutRow placement = scene.Placement(sceneObject.Id);
                    var position = new ScenePosition(
                        (placement.X * ScenePosition.UnitsPerTile) + (placement.Width * ScenePosition.UnitsPerTile / 2),
                        (placement.Y * ScenePosition.UnitsPerTile) + (placement.Height * ScenePosition.UnitsPerTile / 2));
                    return new ForageNodeDefinition(
                        new ForageNodeId(sceneObject.Id.Value),
                        scene.Id,
                        position,
                        new ProductId(sceneObject.SemanticReference!),
                        1);
                }))
            .ToArray();
        (TinyFarmScheduleCatalog schedules, ScheduleContentProvenance scheduleProvenance) =
            LoadScheduleCatalog(Path.Combine(contentDirectory, ScheduleFileName), scenes);
        if (contentMilestone is not null)
        {
            identity = $"{identity};{contentMilestone}-scenes:{sceneProvenance.AggregateSha256};{contentMilestone}-schedules:{scheduleProvenance.AggregateSha256}";
        }
        return new TinyFarmDefinitions(
            identity,
            items,
            crops,
            scenes,
            sceneProvenance,
            schedules,
            scheduleProvenance,
            forageNodes);
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
            SceneObjectKind.Forage => reference == TinyFarmIds.HenOfTheWoods.Value,
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
