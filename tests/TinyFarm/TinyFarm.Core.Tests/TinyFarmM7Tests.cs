using System.Text;
using Copeland.TS.Tson;
using TinyFarm.Core;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM7Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void ProductionTsonCatalog_LoadsFiveTypedScenesAndPreservesSemanticQueries()
    {
        Assert.Equal(5, definitions.Scenes.All.Count);
        Assert.Equal(19, definitions.Scenes.All.Sum(scene => scene.Objects.Count));
        Assert.Equal(19, definitions.Scenes.All.Sum(scene => scene.Layout.Count));
        Assert.Equal(14, definitions.Scenes.All.Sum(scene => scene.Anchors.Count));
        Assert.Equal(8, definitions.Scenes.All.Sum(scene => scene.Routes.Count));
        Assert.Equal(
            new GridPosition(5, 3),
            definitions.Scenes.GetAnchor(TinyFarmAnchorIds.StoreCounter).Position.Tile);

        string[] townEntries = definitions.Scenes.All
            .SelectMany(scene => scene.Routes)
            .Where(route => route.TargetScene == TinyFarmSceneIds.Town)
            .Select(route => route.Id.Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["overworld-town", "store-town"], townEntries);

        string[] blockingFarmObjects = definitions.Scenes.Get(TinyFarmSceneIds.Farm).Objects
            .Where(item => item.BlocksMovement)
            .Select(item => item.Id.Value)
            .ToArray();
        Assert.Equal(["farmhouse", "fence"], blockingFarmObjects);
    }

    [Fact]
    public void GoldenValidFixture_IsSmallTypedAndInspectable()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "M7", "Valid");
        TinyFarmSceneCatalog catalog = TinyFarmDefinitionLoader.LoadSceneCatalog(directory).Catalog;
        SceneDefinition scene = Assert.Single(catalog.All);
        Assert.Equal(new SceneId("fixture"), scene.Id);
        Assert.Equal(new SceneObjectId("marker"), Assert.Single(scene.Objects).Id);
        Assert.Equal(new SceneAnchorId("fixture.spawn"), Assert.Single(scene.Anchors).Id);
        Assert.Empty(scene.Routes);
    }

    [Fact]
    public void SceneIdentity_DoesNotDependOnSceneTableRowOrder()
    {
        string directory = CopyProductionContent();
        try
        {
            Mutate(directory, "tiny-farm-scenes.obj.ts", source => source
                .Replace(
                    "[\"overworld\", \"farm\", \"town\", \"general-store\", \"riverside\"]",
                    "[\"riverside\", \"general-store\", \"town\", \"farm\", \"overworld\"]",
                    StringComparison.Ordinal)
                .Replace(
                    "[\"Overworld\", \"Farm\", \"Town\", \"General Store\", \"Riverside\"]",
                    "[\"Riverside\", \"General Store\", \"Town\", \"Farm\", \"Overworld\"]",
                    StringComparison.Ordinal)
                .Replace("[22, 18, 20, 10, 16]", "[16, 10, 20, 18, 22]", StringComparison.Ordinal)
                .Replace("[14, 12, 14, 8, 10]", "[10, 8, 14, 12, 14]", StringComparison.Ordinal));

            TinyFarmSceneCatalog reordered = TinyFarmDefinitionLoader.LoadSceneCatalog(directory).Catalog;
            Assert.Equal(CatalogSignature(definitions.Scenes), CatalogSignature(reordered));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AuthoredTables_CanonicalTsonRoundTripToSameSemanticCatalog()
    {
        string directory = CopyProductionContent();
        try
        {
            foreach (string path in Directory.GetFiles(directory, "tiny-farm-scene*.obj.ts"))
            {
                TsonReadResult authored = TsonDocumentReader.ReadSelfDescribed(
                    File.ReadAllText(path),
                    TsonDocumentProfile.ObjectTypeScript);
                Assert.True(authored.Success);
                File.WriteAllText(
                    path,
                    TsonCanonicalPrinter.Print(authored.Document!),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            TinyFarmSceneCatalog canonical = TinyFarmDefinitionLoader.LoadSceneCatalog(directory).Catalog;
            Assert.Equal(CatalogSignature(definitions.Scenes), CatalogSignature(canonical));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void InvalidContentFixtures_FailBeforePlayWithStructuredBoundaryDiagnostics()
    {
        var cases = new (string Name, string File, Func<string, string> Mutate)[]
        {
            ("duplicate scene ID", "tiny-farm-scenes.obj.ts", source => ReplaceRequired(source, "\"overworld\", \"farm\"", "\"overworld\", \"overworld\"")),
            ("duplicate object ID", "tiny-farm-scene-objects.obj.ts", source => ReplaceRequired(source, "\"farm-entrance\", \"town-entrance\"", "\"farm-entrance\", \"farm-entrance\"")),
            ("duplicate anchor ID", "tiny-farm-scene-anchors.obj.ts", source => ReplaceRequired(source, "\"farm.from-overworld\", \"farm.start\"", "\"farm.from-overworld\", \"farm.from-overworld\"")),
            ("duplicate route ID", "tiny-farm-scene-routes.obj.ts", source => ReplaceRequired(source, "\"overworld-farm\", \"overworld-town\"", "\"overworld-farm\", \"overworld-farm\"")),
            ("missing layout object", "tiny-farm-scene-layout.obj.ts", source => ReplaceRequired(source, "\"farm-entrance\", \"town-entrance\"", "\"missing-object\", \"town-entrance\"")),
            ("route to missing scene", "tiny-farm-scene-routes.obj.ts", source => ReplaceRequired(source, "\"farm\", \"town\", \"riverside\", \"overworld\"", "\"missing-scene\", \"town\", \"riverside\", \"overworld\"")),
            ("route to missing anchor", "tiny-farm-scene-routes.obj.ts", source => ReplaceRequired(source, "\"farm.from-overworld\", \"town.south-gate\"", "\"missing-anchor\", \"town.south-gate\"")),
            ("anchor outside bounds", "tiny-farm-scene-anchors.obj.ts", source => ReplaceRequired(source, "x: number = [3, 10, 18", "x: number = [99, 10, 18")),
            ("anchor on blocked cell", "tiny-farm-scene-anchors.obj.ts", source => ReplaceRequired(ReplaceRequired(source, "x: number = [3, 10, 18", "x: number = [7, 10, 18"), "y: number = [7, 5, 9", "y: number = [2, 5, 9")),
            ("invalid semantic ref", "tiny-farm-scene-objects.obj.ts", source => ReplaceRequired(source, "OptionalText.Some(\"plot-1\")", "OptionalText.Some(\"unknown-plot\")")),
            ("unknown enum value", "tiny-farm-scene-objects.obj.ts", source => ReplaceRequired(source, "\"Portal\", \"Portal\"", "\"Unknown\", \"Portal\"")),
            ("wrong numeric type", "tiny-farm-scenes.obj.ts", source => ReplaceRequired(source, "width: number = [22", "width: number = [\"wide\"")),
            ("missing required column", "tiny-farm-scenes.obj.ts", source => ReplaceRequired(source, "    width: number = [22, 18, 20, 10, 16];\n", string.Empty)),
            ("wrong root type", "tiny-farm-scenes.obj.ts", source => ReplaceRequired(source, "const $value = Scenes;", "const $value = [Scenes];"))
        };

        foreach ((string name, string file, Func<string, string> mutation) in cases)
        {
            string directory = CopyProductionContent();
            try
            {
                Mutate(directory, file, mutation);
                InvalidDataException exception = Assert.Throws<InvalidDataException>(
                    () => TinyFarmDefinitionLoader.LoadSceneCatalog(directory));
                Assert.False(string.IsNullOrWhiteSpace(exception.Message), name);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SceneCatalog_RemainsHeadlessAndRawTsonTypesStayAtLoaderBoundary()
    {
        Assert.DoesNotContain(
            typeof(SceneDefinition).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "MonoGame.Framework" or "Copeland.TS");
        Assert.DoesNotContain(
            typeof(SceneDefinition).Assembly.GetExportedTypes(),
            type => type.Namespace?.StartsWith("Copeland.TS.Tson", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CanonicalM7Proof_PreservesM1M2M4AndM6Behavior()
    {
        TinyFarmM7Proof proof = TinyFarmTsonSceneScenario.Prove().Proof;
        Assert.Equal("A", proof.Outcome);
        Assert.True(proof.TsonOnlySceneAuthority);
        Assert.True(proof.LegacyParity);
        Assert.True(proof.SaveLoadCompatible);
        Assert.Equal("dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333", proof.M1Hash);
        Assert.Equal("4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3", proof.M2Hash);
    }

    private static string CopyProductionContent()
    {
        string sourceDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
        string destination = Path.Combine(Path.GetTempPath(), $"tinyfarm-m7-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        foreach (string sourcePath in Directory.GetFiles(sourceDirectory, "*.obj.ts"))
        {
            File.Copy(sourcePath, Path.Combine(destination, Path.GetFileName(sourcePath)));
        }
        return destination;
    }

    private static void Mutate(string directory, string fileName, Func<string, string> mutation)
    {
        string path = Path.Combine(directory, fileName);
        string source = File.ReadAllText(path);
        File.WriteAllText(path, mutation(source), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ReplaceRequired(string source, string oldValue, string newValue)
    {
        string replaced = source.Replace(oldValue, newValue, StringComparison.Ordinal);
        Assert.NotEqual(source, replaced);
        return replaced;
    }

    private static string CatalogSignature(TinyFarmSceneCatalog catalog)
    {
        return string.Join('\n', catalog.All.SelectMany(scene =>
        {
            IEnumerable<string> header = [$"scene:{scene.Id}:{scene.Name}:{scene.Width}:{scene.Height}"];
            IEnumerable<string> objects = scene.Objects.Select(item => $"object:{scene.Id}:{item}");
            IEnumerable<string> layout = scene.Layout.Select(item => $"layout:{scene.Id}:{item}");
            IEnumerable<string> anchors = scene.Anchors.Select(item => $"anchor:{item}");
            IEnumerable<string> routes = scene.Routes.Select(item => $"route:{item}");
            return header.Concat(objects).Concat(layout).Concat(anchors).Concat(routes);
        }));
    }
}
