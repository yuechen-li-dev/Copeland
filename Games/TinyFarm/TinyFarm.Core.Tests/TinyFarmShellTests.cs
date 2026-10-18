using Aurelian.Games;
using InputMan.Core;
using TinyFarm.InputMan;
using Xunit;

namespace TinyFarm.Core.Tests;

/// <summary>TinyFarm hosted by the Aurelian game shell, driven headlessly through the shell's own input path.</summary>
public sealed class TinyFarmShellTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);

    [Fact]
    public void NewGameStartsTheOpeningAndWalkingMovesThePlayer()
    {
        using GameSession session = Start(out TinyFarmApp app);
        Assert.Equal(TinyFarmScreen.Playing, app.Game.Screen);
        Assert.StartsWith("A turnip is ready", app.Game.Status, StringComparison.Ordinal);
        ScenePosition before = app.Game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        Hold(session, KeyboardKey.D, 40);
        Assert.NotEqual(before, app.Game.State.ActorScene(TinyFarmIds.Player).WorldPosition);
    }

    [Fact]
    public void EscapeClosesPocketsBeforeItPausesTheGame()
    {
        using GameSession session = Start(out TinyFarmApp app);
        Tap(session, KeyboardKey.I);
        Assert.Equal(TinyFarmScreen.Inventory, app.Game.Screen);
        Assert.Equal([TinyFarmShellInput.Menu], app.InputContexts);

        Tap(session, KeyboardKey.Escape);
        Assert.Equal(TinyFarmScreen.Playing, app.Game.Screen);
        Assert.Equal("playing", session.Screen);

        Tap(session, KeyboardKey.Escape);
        Assert.Equal("pause", session.Screen);
    }

    [Fact]
    public async Task ShellSavesRoundTripTheSemanticWorld()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tinyfarm-shell-tests", Guid.NewGuid().ToString("N"));
        string saved;
        using (GameSession session = Start(out TinyFarmApp app, directory))
        {
            Hold(session, KeyboardKey.D, 30);
            saved = TinyFarmSemanticHash.Compute(app.Game.State);
            await session.SaveAsync(GameSession.QuickSlot);
            Hold(session, KeyboardKey.S, 30);
            Assert.NotEqual(saved, TinyFarmSemanticHash.Compute(app.Game.State));
        }
        using var reopened = new GameSession(new TinyFarmApp(), directory);
        await reopened.LoadAsync(GameSession.QuickSlot);
        var restored = (TinyFarmApp)reopened.Game;
        Assert.Equal(saved, TinyFarmSemanticHash.Compute(restored.Game.State));
        Assert.Equal(TinyFarmScreen.Playing, restored.Game.Screen);
    }

    [Fact]
    public void SleepCheckpointsGoToTheShell()
    {
        using GameSession session = Start(out TinyFarmApp app);
        bool accepted = app.Game.CheckpointHost!();
        // Requested outside a tick, the save is captured right after the next one.
        session.Advance(Tick);
        session.FlushCheckpoint();
        Assert.True(accepted);
        Assert.Null(session.PersistenceError);
        Assert.Equal(1, session.Checkpoints);
    }

    private static GameSession Start(out TinyFarmApp app, string? directory = null)
    {
        app = new TinyFarmApp(Path.Combine(AppContext.BaseDirectory, "Assets"));
        var session = new GameSession(app, directory ?? Path.Combine(Path.GetTempPath(), "tinyfarm-shell-tests", Guid.NewGuid().ToString("N")));
        session.Activate("start");
        return session;
    }

    private static void Hold(GameSession session, KeyboardKey key, int ticks)
    {
        session.Input.Adapter.RecordButton(Controls.Key(key), true);
        for (int index = 0; index < ticks; index++)
        {
            session.Advance(Tick);
        }
        session.Input.Adapter.RecordButton(Controls.Key(key), false);
        session.Advance(Tick);
    }

    private static void Tap(GameSession session, KeyboardKey key)
    {
        session.Input.Adapter.RecordButton(Controls.Key(key), true);
        session.Advance(TimeSpan.Zero);
        session.Input.Adapter.RecordButton(Controls.Key(key), false);
        session.Advance(Tick);
        session.Advance(Tick);
    }
}
