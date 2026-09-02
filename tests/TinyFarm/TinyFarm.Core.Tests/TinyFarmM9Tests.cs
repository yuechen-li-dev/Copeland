using System.Text;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmM9Tests
{
    private readonly TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();

    [Fact]
    public void ProductionScheduleTson_LoadsElevenTypedRowsWithProvenance()
    {
        Assert.Equal(11, definitions.Schedules.Windows.Count);
        Assert.Equal("tiny-farm-npc-schedules.obj.ts", definitions.ScheduleContent.FileName);
        Assert.Equal(64, definitions.ScheduleContent.SourceSha256.Length);
        Assert.Equal(64, definitions.ScheduleContent.AggregateSha256.Length);
        Assert.True(definitions.ScheduleContent.ByteLength > 0);
        Assert.All(definitions.Schedules.Windows, window => Assert.False(string.IsNullOrWhiteSpace(window.Reason)));
    }

    [Fact]
    public void AuthoredRowReorder_PreservesCanonicalCatalogAndAllM8Decisions()
    {
        TinyFarmScheduleWindow[] reversed = definitions.Schedules.Windows.Reverse().ToArray();
        using TemporaryScheduleFile fixture = WriteFixture(ScheduleSource(reversed));

        TinyFarmScheduleCatalog reordered = TinyFarmDefinitionLoader
            .LoadScheduleCatalog(fixture.Path, definitions.Scenes)
            .Catalog;

        Assert.Equal(definitions.Schedules.Windows, reordered.Windows);
        foreach (ActorId actor in Npcs())
        {
            for (int minute = 0; minute < 7 * 1440; minute++)
            {
                SceneAnchorId expected = TinyFarmNpcSchedule.Decide(
                    definitions.Schedules,
                    actor,
                    minute).SelectedAnchor;
                SceneAnchorId actual = TinyFarmNpcSchedule.Decide(
                    reordered,
                    actor,
                    minute).SelectedAnchor;
                Assert.Equal(expected, actual);
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidScheduleFixtures))]
    public void HostileScheduleFixtures_FailBeforePlay(string name, string source)
    {
        using TemporaryScheduleFile fixture = WriteFixture(source);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            TinyFarmDefinitionLoader.LoadScheduleCatalog(fixture.Path, definitions.Scenes));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message), name);
    }

    [Fact]
    public void MinimalGoldenFixture_LoadsThreeFullDaySchedules()
    {
        using TemporaryScheduleFile fixture = WriteFixture(ScheduleSource(MinimalRows()));

        TinyFarmScheduleCatalog catalog = TinyFarmDefinitionLoader
            .LoadScheduleCatalog(fixture.Path, definitions.Scenes)
            .Catalog;

        Assert.Equal(3, catalog.Windows.Count);
        Assert.Equal(TinyFarmAnchorIds.TownSquare, TinyFarmNpcSchedule.Decide(
            catalog,
            TinyFarmIds.Mara,
            1439).SelectedAnchor);
    }

    [Fact]
    public void AuthoredTableAloneAnswersTheFourReadabilityQuestions()
    {
        Assert.Equal(TinyFarmAnchorIds.RiversideMeetingPoint, Decide(TinyFarmIds.Mara, 13 * 60));
        Assert.Equal(TinyFarmAnchorIds.StoreCounter, Decide(TinyFarmIds.Mara, 5 * 1440 + 10 * 60));

        TinyFarmScheduleWindow selaStore = Assert.Single(definitions.Schedules.Windows, window =>
            window.Actor == TinyFarmIds.Sela
            && window.Anchor == TinyFarmAnchorIds.StoreCounter);
        Assert.Equal(480, selaStore.StartMinute);
        Assert.Equal(1080, selaStore.EndMinuteExclusive);

        TinyFarmScheduleWindow eliasRiverside = Assert.Single(definitions.Schedules.Windows, window =>
            window.Actor == TinyFarmIds.Elias
            && window.Anchor == TinyFarmAnchorIds.RiversideMeetingPoint);
        Assert.Equal(1080, eliasRiverside.EndMinuteExclusive);
    }

    [Fact]
    public void ScheduleCatalog_RemainsHeadlessAndRawTsonTypesStayAtLoaderBoundary()
    {
        Assert.DoesNotContain(
            typeof(TinyFarmScheduleCatalog).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "MonoGame.Framework" or "Copeland.TS");
        Assert.DoesNotContain(
            typeof(TinyFarmScheduleCatalog).Assembly.GetExportedTypes(),
            type => type.Namespace?.StartsWith("Copeland.TS.Tson", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CanonicalM9Proof_IsOutcomeAWithExactHistoricalHashes()
    {
        TinyFarmM9Proof proof = TinyFarmTsonScheduleScenario.Prove().Proof;

        Assert.Equal("A", proof.Outcome);
        Assert.Equal("10cdca5bf32bb96bf26d42abbc8ec8feb85983286fab35361c1c979a906796f6", proof.ScheduleDecisionHash);
        Assert.Equal("d763164039f2841ff6694f597df0610875ada968d0ad28a0fb9f76469fe59711", proof.AnchorSequenceHash);
        Assert.Equal("07dde9ac2f6c957017abe151320ee0a7d5c900f51ecd7901331c9d21a480d8fa", proof.NavigationHash);
        Assert.Equal("fe79f373643e1e3aa5df8f505e775cce7388206332831497fe12f8bed7e54afa", proof.SceneContentHash);
        Assert.Equal("dcc35869aba0eba979725b1871d0babfe127383123a1a5f665b666bc3488d333", proof.M1Hash);
        Assert.Equal("4a49e221d6ffe90304143cece5b1a20fe96eecc4d10d30cf1bde11922a18ced3", proof.M2Hash);
        Assert.True(proof.RowReorderPreserved);
        Assert.True(proof.ExhaustiveM8Parity);
        Assert.False(proof.RawTsonLeaksIntoDecisionRuntime);
    }

    public static IEnumerable<object[]> InvalidScheduleFixtures()
    {
        TinyFarmScheduleWindow[] rows = MinimalRows();
        yield return Fixture("unknown actor", ReplaceFirst(rows, rows[0] with { Actor = new ActorId("unknown") }));
        yield return Fixture("unknown anchor", ReplaceFirst(rows, rows[0] with { Anchor = new SceneAnchorId("missing.anchor") }));
        yield return Fixture("negative start", ReplaceFirst(rows, rows[0] with { StartMinute = -1 }));
        yield return Fixture("end above 1440", ReplaceFirst(rows, rows[0] with { EndMinuteExclusive = 1441 }));
        yield return Fixture("start not before end", ReplaceFirst(rows, rows[0] with
        {
            StartMinute = 720,
            EndMinuteExclusive = 720
        }));
        yield return Fixture("coverage hole", ReplaceFirst(rows, rows[0] with { EndMinuteExclusive = 1439 }));
        yield return Fixture("equal priority conflicting overlap", rows.Append(
            new TinyFarmScheduleWindow(
                TinyFarmIds.Mara,
                TinyFarmScheduleDay.Day(1),
                0,
                1440,
                TinyFarmAnchorIds.FarmHome,
                0,
                "conflict")).ToArray());
        yield return Fixture("duplicate semantic row", rows.Append(rows[0]).ToArray());
        yield return new object[]
        {
            "invalid day value",
            ScheduleSource(rows).Replace("\"Every\", \"Every\", \"Every\"", "\"Day8\", \"Every\", \"Every\"", StringComparison.Ordinal)
        };
        yield return new object[]
        {
            "invalid priority type",
            ScheduleSource(rows).Replace("priority: number = [0, 0, 0];", "priority: string = [\"0\", \"0\", \"0\"];", StringComparison.Ordinal)
        };
        yield return new object[]
        {
            "wrong root type",
            "const $schema: string = \"copeland://tiny-farm/tests/m9/wrong-root\"; const $value: string = \"not-a-table\";"
        };
        yield return new object[]
        {
            "missing required column",
            ScheduleSource(rows).Replace("    reason: string = [\"full-day\", \"full-day\", \"full-day\"];\n", string.Empty, StringComparison.Ordinal)
        };
        yield return new object[]
        {
            "wrong integer type",
            ScheduleSource(rows).Replace("priority: number = [0, 0, 0];", "priority: number = [0.5, 0, 0];", StringComparison.Ordinal)
        };
    }

    private SceneAnchorId Decide(ActorId actor, int minute)
    {
        return TinyFarmNpcSchedule.Decide(definitions.Schedules, actor, minute).SelectedAnchor;
    }

    private static object[] Fixture(string name, TinyFarmScheduleWindow[] rows)
    {
        return [name, ScheduleSource(rows)];
    }

    private static TinyFarmScheduleWindow[] ReplaceFirst(
        TinyFarmScheduleWindow[] rows,
        TinyFarmScheduleWindow replacement)
    {
        TinyFarmScheduleWindow[] copy = rows.ToArray();
        copy[0] = replacement;
        return copy;
    }

    private static TinyFarmScheduleWindow[] MinimalRows()
    {
        return
        [
            new(TinyFarmIds.Mara, TinyFarmScheduleDay.EveryDay, 0, 1440, TinyFarmAnchorIds.TownSquare, 0, "full-day"),
            new(TinyFarmIds.Elias, TinyFarmScheduleDay.EveryDay, 0, 1440, TinyFarmAnchorIds.FarmWorkArea, 0, "full-day"),
            new(TinyFarmIds.Sela, TinyFarmScheduleDay.EveryDay, 0, 1440, TinyFarmAnchorIds.FarmHome, 0, "full-day")
        ];
    }

    private static ActorId[] Npcs()
    {
        return [TinyFarmIds.Elias, TinyFarmIds.Mara, TinyFarmIds.Sela];
    }

    private static string ScheduleSource(IReadOnlyList<TinyFarmScheduleWindow> rows)
    {
        string Values(Func<TinyFarmScheduleWindow, string> selector)
        {
            return string.Join(", ", rows.Select(selector));
        }

        return $$"""
            const $schema: string = "copeland://tiny-farm/tests/m9/schedule-fixture";
            record table NpcSchedules {
                actorId: string = [{{Values(row => Quote(row.Actor.Value))}}];
                day: string = [{{Values(row => Quote(row.Day.IsEveryDay ? "Every" : $"Day{row.Day.SpecificDay}"))}}];
                startMinute: number = [{{Values(row => row.StartMinute.ToString(System.Globalization.CultureInfo.InvariantCulture))}}];
                endMinuteExclusive: number = [{{Values(row => row.EndMinuteExclusive.ToString(System.Globalization.CultureInfo.InvariantCulture))}}];
                anchorId: string = [{{Values(row => Quote(row.Anchor.Value))}}];
                priority: number = [{{Values(row => row.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture))}}];
                reason: string = [{{Values(row => Quote(row.Reason))}}];
            }
            const $value = NpcSchedules;
            """ + "\n";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static TemporaryScheduleFile WriteFixture(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "tiny-farm-m9", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "schedule.obj.ts");
        File.WriteAllText(path, source, Encoding.UTF8);
        return new TemporaryScheduleFile(directory, path);
    }

    private sealed class TemporaryScheduleFile(string directory, string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
