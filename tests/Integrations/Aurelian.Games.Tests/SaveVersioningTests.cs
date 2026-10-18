using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Aurelian.Games;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed record CounterV1(int Count);
public sealed record CounterV2(int Total, string Label);

[JsonSerializable(typeof(CounterV1))]
[JsonSerializable(typeof(CounterV2))]
public partial class CounterJsonContext : JsonSerializerContext;

public sealed class SaveVersioningTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);

    [Fact]
    public async Task OlderSavesMigrateForwardAndNewerOnesAreRejected()
    {
        string directory = TemporaryDirectory();
        using (var old = new GameSession(new CounterGame(version: 1), directory))
        {
            old.Activate("start");
            ((CounterGame)old.Game).Count = 7;
            await old.SaveAsync("slot-1");
        }

        using (var current = new GameSession(new CounterGame(version: 2), directory))
        {
            await current.LoadAsync("slot-1");
            var game = (CounterGame)current.Game;
            Assert.Equal(7, game.Count);
            Assert.Equal("migrated", game.Label);
            await current.SaveAsync("slot-2");
        }

        // A v1 build cannot read a v2 save, and it must not touch live state trying.
        using var older = new GameSession(new CounterGame(version: 1), directory);
        older.Activate("start");
        ((CounterGame)older.Game).Count = 3;
        await Assert.ThrowsAnyAsync<Exception>(() => older.LoadAsync("slot-2"));
        Assert.Equal(3, ((CounterGame)older.Game).Count);
    }

    [Fact]
    public void MigrationsMustStartBelowTheSchemaVersion()
    {
        Assert.Throws<ArgumentException>(() => new GameSession(new CounterGame(version: 2, badMigration: true), TemporaryDirectory()));
    }

    [Fact]
    public async Task ACheckpointRequestedDuringATickCapturesThatTickExactly()
    {
        string directory = TemporaryDirectory();
        long savedTick;
        using (var session = new GameSession(new CounterGame(version: 2, checkpointAt: 5), directory))
        {
            session.Activate("start");
            for (int index = 0; index < 12; index++)
            {
                session.Advance(Tick);
            }
            session.FlushCheckpoint();
            Assert.Null(session.PersistenceError);
            Assert.Equal(1, session.Checkpoints);
            Assert.Equal("1", session.Observe().Values["save.checkpoints"]);
            savedTick = session.Tick;
            Assert.True(savedTick > 5);
        }
        using var restored = new GameSession(new CounterGame(version: 2), directory);
        await restored.LoadAsync(GameSession.QuickSlot);
        Assert.Equal(5, restored.Tick);
        Assert.Equal(5, ((CounterGame)restored.Game).Count);
    }

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "aurelian-save-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Counts ticks. Version 1 saves {Count}; version 2 renamed it to Total and added a Label.</summary>
    private sealed class CounterGame : IGame
    {
        private readonly int checkpointAt;
        private GameContext? context;

        public CounterGame(int version, int checkpointAt = -1, bool badMigration = false)
        {
            this.checkpointAt = checkpointAt;
            Manifest = new GameManifest("counter-game", "COUNTER", new GameInputLayout(
                [new("bump", "Bump", GameControl.Key(KeyboardKey.Space))]));
            if (version == 1)
            {
                Saves = GameSaveSchema.Create(CounterJsonContext.Default.CounterV1, "counter", () => new CounterV1(Count),
                    _ => { }, snapshot => Count = snapshot.Count);
            }
            else
            {
                Saves = new GameSaveSchema<CounterV2>(CounterJsonContext.Default.CounterV2, "counter", () => new CounterV2(Count, Label),
                    snapshot => ArgumentException.ThrowIfNullOrEmpty(snapshot.Label), snapshot => (Count, Label) = (snapshot.Total, snapshot.Label))
                    {
                        Version = 2,
                        Migrations = [new GameSaveMigration(badMigration ? 2 : 1, state => new JsonObject
                        {
                            ["Total"] = state["Count"]!.GetValue<int>(),
                            ["Label"] = "migrated",
                        })],
                    };
            }
        }

        public GameManifest Manifest { get; }
        public GameSaveSchema Saves { get; }
        public int Count { get; set; }
        public string Label { get; private set; } = "fresh";

        public void Start(GameContext context) => this.context = context;

        public void NewGame()
        {
            Count = 0;
            Label = "fresh";
        }

        public void FixedUpdate(GameTick tick)
        {
            Count++;
            if (tick.Index == checkpointAt)
            {
                context!.RequestSave();
            }
        }

        public void Dispose()
        {
        }
    }
}
