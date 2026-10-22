using Aurelian.Ariadne.VnDemo;
using Aurelian.Beacon3D;
using Aurelian.Games;
using Aurelian.StrategyDemo;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class GameMigrationTests
{
    [Fact]
    public void BeaconUsesShellPauseFocusAndRebindingWithItsRealSimulation()
    {
        var game = new BeaconShellGame();
        using var session = Session(game);
        var target = new GamePlaytestTarget(session);
        session.Activate("start");
        var initialPosition = game.World.Position;
        target.Key(KeyboardKey.W, true);
        target.Frame(TimeSpan.FromSeconds(.2));
        target.Key(KeyboardKey.W, false);
        Assert.True(game.World.Time > 0);
        Assert.NotEqual(initialPosition, game.World.Position);
        string moved = game.World.SemanticHash();
        session.OnFocusChanged(false);
        target.Frame(TimeSpan.FromSeconds(.2));
        Assert.Equal("pause", session.Screen);
        Assert.Equal(moved, game.World.SemanticHash());
        session.OnFocusChanged(true);
        session.Activate("settings");
        session.Activate("controls");
        session.Activate("bindings");
        session.Activate("bindings.next");
        session.Activate("bind.view");
        target.Frame(TimeSpan.Zero);
        target.Key(KeyboardKey.B, true);
        target.Frame(TimeSpan.Zero);
        target.Key(KeyboardKey.B, false);
        target.Frame(TimeSpan.Zero);
        Assert.Equal(GameControl.Key(KeyboardKey.B), session.Input.Binding("view"));
        session.Activate("back");
        session.Activate("back");
        session.Activate("back");
        session.Activate("resume");
        Assert.Equal("playing", session.Screen);
        target.Key(KeyboardKey.B, true);
        target.Frame(TimeSpan.FromSeconds(.02));
        Assert.Equal(Aurelian.Runtime.CameraView.ThirdPerson, game.World.View);
        Assert.NotEmpty(game.View(new(960, 600)).Scene.Geometry);
    }

    [Fact]
    public async Task SunkillRestoresDialogueThroughShellSaveSlotsAndImportsTheOldSlot()
    {
        string directory = TemporaryDirectory();
        string legacy = Path.Combine(directory, "old");
        using var previous = new VnSession();
        previous.Advance();
        await new VnPersistence(legacy).SaveAsync(1, previous);
        var game = new SunkillGame(legacy);
        using var session = new GameSession(game, Path.Combine(directory, "shell"));
        Assert.True(session.ImportedLegacySave);
        await session.LoadAsync(GameSession.QuickSlot);
        Assert.Equal(previous.SemanticHash(), game.World.SemanticHash());
        await session.SaveAsync("roundtrip");
        game.World.Advance();
        await session.LoadAsync("roundtrip");
        Assert.Equal(previous.SemanticHash(), game.World.SemanticHash());
    }

    [Fact]
    public void SunkillClickableDialogueUsesTheSharedPointerPathAtOddResolution()
    {
        var game = new SunkillGame(Path.Combine(TemporaryDirectory(), "absent"));
        using var session = Session(game);
        var target = new GamePlaytestTarget(session);
        session.Activate("start");
        target.Resize(1537, 864);
        string before = game.World.SemanticHash();
        (double x, double y) = target.ActionPoint("ren.advance");
        target.Pointer(x, y, true);
        target.Frame(TimeSpan.Zero);
        target.Pointer(x, y, false);
        target.Frame(TimeSpan.FromSeconds(.02));
        Assert.NotEqual(before, game.World.SemanticHash());
        session.OnFocusChanged(false);
        string paused = game.World.SemanticHash();
        target.Key(KeyboardKey.Enter, true);
        target.Frame(TimeSpan.FromSeconds(.1));
        Assert.Equal(paused, game.World.SemanticHash());
    }

    [Fact]
    public void MosswardUsesShellCadencePauseAndGameActionsWithoutASecondInputEngine()
    {
        var game = new MosswardGame();
        using var session = Session(game);
        var target = new GamePlaytestTarget(session);
        session.Activate("start");
        target.Key(KeyboardKey.B, true);
        target.Frame(TimeSpan.FromSeconds(.1));
        target.Key(KeyboardKey.B, false);
        Assert.Equal("true", session.Observe().Values["placing"]);
        Assert.Equal(1, game.World.Tick);
        session.OnFocusChanged(false);
        target.Frame(TimeSpan.FromSeconds(.4));
        Assert.Equal(1, game.World.Tick);
        session.OnFocusChanged(true);
        session.Activate("resume");
        target.Key(KeyboardKey.C, true);
        target.Frame(TimeSpan.FromSeconds(.1));
        Assert.Equal("false", session.Observe().Values["placing"]);
        target.Key(KeyboardKey.C, false);
        target.Key(KeyboardKey.N, true);
        target.Frame(TimeSpan.FromSeconds(.1));
        Assert.NotEmpty(game.World.Production);
        session.OnFocusChanged(false);
        session.OnFocusChanged(true);
        session.Activate("title");
        session.Activate("start");
        Assert.Empty(game.World.Production);
        Assert.Equal(0, game.World.Tick);
        target.Resize(1537, 864);
        (double x, double y) = target.ActionPoint("unit.1");
        target.Pointer(x, y, true);
        target.Frame(TimeSpan.FromSeconds(.02));
        target.Pointer(x, y, false);
        target.Frame(TimeSpan.FromSeconds(.02));
        Assert.Contains(1, game.World.Selection.Snapshot());
    }

    private static GameSession Session(IGame game) => new(game, TemporaryDirectory());

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "aurelian-game-migration", Guid.NewGuid().ToString("N"));
}
