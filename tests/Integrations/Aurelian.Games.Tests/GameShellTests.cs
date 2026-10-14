using System.Text.Json.Serialization;
using Aurelian.Audio;
using Aurelian.Games;
using InputMan.Core;
using Xunit;

namespace Aurelian.Games.Tests;

public sealed class GameShellTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromTicks(166667);

    [Fact]
    public void ManifestRejectsReservedAndDuplicateActionsBeforeAnyServiceStarts()
    {
        Assert.Contains("Escape", Assert.Throws<ArgumentException>(() => new GameSession(new DotGame(
            new GameInputLayout([new("menu", "Menu", GameControl.Key(KeyboardKey.Escape))])))).Message);
        Assert.Throws<ArgumentException>(() => new GameSession(new DotGame(new GameInputLayout(
        [
            new("jump", "Jump", GameControl.Key(KeyboardKey.Space)),
            new("jump", "Jump again", GameControl.Key(KeyboardKey.J)),
        ]))));
        Assert.Contains("more than one", Assert.Throws<ArgumentException>(() => new GameSession(new DotGame(new GameInputLayout(
        [
            new("jump", "Jump", GameControl.Key(KeyboardKey.Space)),
            new("dash", "Dash", GameControl.Key(KeyboardKey.Space)),
        ])))).Message);
    }

    [Fact]
    public void TapsBetweenTicksReachExactlyOneFixedUpdate()
    {
        using var session = Create(out DotGame game);
        session.Activate("start");
        Tap(session, KeyboardKey.Space);
        Assert.Equal(0, game.Jumps);
        session.Advance(Tick);
        Assert.Equal(1, game.Jumps);
        session.Advance(Tick);
        Assert.Equal(1, game.Jumps);
    }

    [Fact]
    public void PauseAndFocusLossFreezeTheSimulationAndReleaseHeldKeys()
    {
        using var session = Create(out DotGame game);
        session.Activate("start");
        Press(session, KeyboardKey.D, true);
        session.Advance(Tick);
        float moved = game.X;
        Assert.True(moved > 0);
        Tap(session, KeyboardKey.Escape);
        Assert.Equal("pause", session.Screen);
        session.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(moved, game.X);
        session.Activate("resume");
        session.OnFocusChanged(false);
        Assert.Equal("pause", session.Screen);
        session.OnFocusChanged(true);
        session.Activate("resume");
        session.Advance(Tick);
        Assert.Equal(moved, game.X);
    }

    [Fact]
    public void MenuConfirmDoesNotLeakIntoTheFirstGameplayTick()
    {
        using var session = Create(out DotGame game);
        Press(session, KeyboardKey.Space, true);
        Assert.True(session.Playing);
        session.Advance(Tick);
        session.Advance(Tick);
        Assert.Equal(0, game.Jumps);
    }

    [Fact]
    public async Task PreferencesPersistSeparatelyFromWorldSaves()
    {
        string directory = TemporaryDirectory();
        using (var first = Create(out _, directory))
        {
            first.Activate("settings");
            first.Activate("audio");
            first.Adjust("audio.master", -1);
            first.Adjust("audio.master", -1);
            Assert.Equal(0.8f, first.Settings.Audio.Master);
            first.Input.Rebind("jump", GameControl.Key(KeyboardKey.J));
            first.SaveSettings();
            Assert.Empty(await first.Saves.ListAsync());
        }
        using var second = Create(out _, directory);
        Assert.Equal(0.8f, second.Settings.Audio.Master);
        Assert.Equal(GameControl.Key(KeyboardKey.J), second.Input.Binding("jump"));
        Assert.Equal("title", second.Screen);
        Assert.Null(second.PersistenceError);
    }

    [Fact]
    public void RebindCaptureBindsTheNextKeySwapsConflictsAndEscapeCancels()
    {
        using var session = Create(out DotGame game);
        session.Activate("settings");
        session.Activate("controls");
        session.Activate("bindings");
        session.Activate("bind.jump");
        Assert.Equal("rebind", session.Screen);
        // Capture seeds already-held keys on its first frame, so a key pressed with the menu confirm never binds.
        session.Advance(TimeSpan.Zero);
        Tap(session, KeyboardKey.D);
        session.Advance(TimeSpan.Zero);
        Assert.Equal("bindings", session.Screen);
        Assert.Equal(GameControl.Key(KeyboardKey.D), session.Input.Binding("jump"));
        // D belonged to "right"; it receives jump's old control rather than leaving two actions on one key.
        Assert.Equal(GameControl.Key(KeyboardKey.Space), session.Input.Binding("right"));
        using (var reopened = new GameSession(new DotGame(), session.SaveDirectory))
        {
            Assert.Equal(GameControl.Key(KeyboardKey.D), reopened.Input.Binding("jump"));
        }

        session.Activate("bind.left");
        session.Advance(TimeSpan.Zero);
        Tap(session, KeyboardKey.Escape);
        session.Advance(TimeSpan.Zero);
        Assert.Equal("bindings", session.Screen);
        Assert.Equal(GameControl.Key(KeyboardKey.A), session.Input.Binding("left"));
        Assert.Equal(GameRebindStatus.Canceled, session.Input.RebindStatus);

        session.Activate("bindings.reset");
        Assert.Equal(GameControl.Key(KeyboardKey.Space), session.Input.Binding("jump"));
        Assert.Equal(GameControl.Key(KeyboardKey.D), session.Input.Binding("right"));

        // The newly bound key drives gameplay; the bound press itself does not leak into play.
        session.Input.Rebind("jump", GameControl.Key(KeyboardKey.K));
        Tap(session, KeyboardKey.Escape);
        Tap(session, KeyboardKey.Escape);
        Tap(session, KeyboardKey.Escape);
        Assert.Equal("title", session.Screen);
        session.Activate("start");
        Tap(session, KeyboardKey.K);
        session.Advance(Tick);
        Assert.Equal(1, game.Jumps);
    }

    [Fact]
    public void DisplayEditsStayPendingUntilAppliedThenReachTheHostAndPersist()
    {
        string directory = TemporaryDirectory();
        var host = new FakeDisplay();
        using (var session = Create(out _, directory))
        {
            session.AttachDisplay(host);
            Assert.Single(host.Applied);
            session.Activate("settings");
            session.Activate("display");
            session.Adjust("display.resolution", 1);
            session.Adjust("display.mode", 1);
            Assert.Single(host.Applied);
            Assert.Equal(new GameResolution(1280, 720), session.Settings.Display.Resolution);
            session.Activate("display.apply");
            Assert.Equal(2, host.Applied.Count);
            Assert.Equal(new GameDisplaySettings(1600, 900, GameWindowMode.Borderless), host.Applied[^1]);
            // Leaving the page discards unapplied edits.
            session.Adjust("display.vsync", 1);
            Assert.False(session.PendingDisplay.VSync);
            Tap(session, KeyboardKey.Escape);
            Assert.True(session.PendingDisplay.VSync);
            Assert.Equal(2, host.Applied.Count);
        }
        using var reopened = Create(out _, directory);
        Assert.Equal(new GameDisplaySettings(1600, 900, GameWindowMode.Borderless), reopened.Settings.Display);
    }

    [Fact]
    public async Task WorldSavesCarryCadencePhaseAndRejectIncompatibleOrInvalidStateAtomically()
    {
        string directory = TemporaryDirectory();
        using var first = Create(out DotGame firstGame, directory);
        first.Activate("start");
        Press(first, KeyboardKey.D, true);
        first.Advance(TimeSpan.FromTicks(123456));
        first.Advance(Tick);
        await first.SaveAsync("slot");
        GameShellSave saved = first.CaptureSave();

        using var second = Create(out DotGame secondGame, directory);
        await second.LoadAsync("slot");
        Assert.Equal(firstGame.X, secondGame.X);
        Press(first, KeyboardKey.D, false);
        first.Advance(Tick);
        second.Activate("start");
        second.RestoreSave(saved);
        second.Advance(Tick);
        Assert.Equal(firstGame.X, secondGame.X);
        Assert.Equal(first.Tick, second.Tick);

        float before = secondGame.X;
        Assert.Throws<InvalidDataException>(() => second.RestoreSave(saved with { Cadence = [] }));
        Assert.Throws<InvalidDataException>(() => second.RestoreSave(saved with { PendingPressed = ["teleport"] }));
        Assert.Equal(before, secondGame.X);

        using var other = new GameSession(new DotGame(identity: "dot-v2"), directory);
        await Assert.ThrowsAnyAsync<Exception>(() => other.LoadAsync("slot"));
    }

    [Fact]
    public void GamesWithoutASaveSchemaGetNoSaveEntries()
    {
        using var session = new GameSession(new DotGame(saves: false), TemporaryDirectory());
        Assert.DoesNotContain(session.Menu!.Entries, entry => entry.Id is "load" or "save");
        session.Activate("start");
        Tap(session, KeyboardKey.Escape);
        Assert.DoesNotContain(session.Menu!.Entries, entry => entry.Id is "load" or "save");
        Assert.False(session.SupportsSaves);
    }

    [Fact]
    public void SavedBindingsForAnOlderLayoutAreRejectedWithoutLosingOtherPreferences()
    {
        string directory = TemporaryDirectory();
        using (var first = Create(out _, directory))
        {
            first.ApplySettings(first.Settings with
            {
                Audio = new GameAudioSettings(Master: 0.5f),
                Controls = first.Settings.Controls with { Bindings = [new GameBindingOverride("jump", GameControl.Key(KeyboardKey.J))] },
            });
        }
        using var renamed = new GameSession(new DotGame(new GameInputLayout(
        [
            new("hop", "Hop", GameControl.Key(KeyboardKey.Space)),
        ])), directory);
        Assert.NotNull(renamed.PersistenceError);
        Assert.Equal(0.5f, renamed.Settings.Audio.Master);
        Assert.Equal(GameControl.Key(KeyboardKey.Space), renamed.Input.Binding("hop"));
    }

    [Fact]
    public void ArrowKeysAdjustValueRowsAndAudioBusesFollowSettings()
    {
        using var session = Create(out _);
        session.Activate("settings");
        session.Activate("audio");
        Tap(session, KeyboardKey.ArrowDown);
        Tap(session, KeyboardKey.ArrowLeft);
        Assert.Equal("audio.music", session.Observe().Values["menu.selected"]);
        Assert.Equal(0.7f, session.Settings.Audio.Music);
        Tap(session, KeyboardKey.ArrowRight);
        Tap(session, KeyboardKey.ArrowRight);
        Assert.Equal(0.9f, session.Settings.Audio.Music);
    }

    [Fact]
    public void PlaytestTargetAssertsAnyPublishedFact()
    {
        using var session = Create(out _);
        var target = new GamePlaytestTarget(session);
        target.Command("activate start");
        target.Key(KeyboardKey.Space, true);
        target.Frame(TimeSpan.Zero);
        target.Key(KeyboardKey.Space, false);
        target.Frame(Tick);
        target.Command("assert jumps 1");
        target.Command("assert screen playing");
        Assert.Throws<InvalidOperationException>(() => target.Command("assert jumps 2"));
        Assert.Contains("Known facts", Assert.Throws<ArgumentException>(() => target.Command("assert nonsense 1")).Message);
    }

    private static GameSession Create(out DotGame game, string? directory = null)
    {
        game = new DotGame();
        return new GameSession(game, directory ?? TemporaryDirectory());
    }

    private static void Tap(GameSession session, KeyboardKey key)
    {
        Press(session, key, true);
        Press(session, key, false);
    }

    private static void Press(GameSession session, KeyboardKey key, bool down)
    {
        session.Input.Adapter.RecordButton(Controls.Key(key), down);
        session.Advance(TimeSpan.Zero);
    }

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "aurelian-shell-tests", Guid.NewGuid().ToString("N"));

    private sealed class FakeDisplay : IGameDisplay
    {
        public List<GameDisplaySettings> Applied { get; } = [];
        public IReadOnlyList<GameResolution> Resolutions => [new(960, 600), new(1280, 720), new(1600, 900)];
        public void Apply(GameDisplaySettings settings) => Applied.Add(settings);
    }
}

public sealed record DotSnapshot(float X, int Jumps);

[JsonSerializable(typeof(DotSnapshot))]
public partial class DotJsonContext : JsonSerializerContext;

/// <summary>The smallest useful game: a dot that moves and jumps. Everything else comes from the shell.</summary>
public sealed class DotGame : IGame
{
    private readonly GameSaveSchema? saves;

    public DotGame(GameInputLayout? layout = null, string identity = "dot-v1", bool saves = true)
    {
        Manifest = new GameManifest("dot-game", "DOT", layout ?? new GameInputLayout(
        [
            new("left", "Left", GameControl.Key(KeyboardKey.A)),
            new("right", "Right", GameControl.Key(KeyboardKey.D)),
            new("jump", "Jump", GameControl.Key(KeyboardKey.Space)),
        ]));
        this.saves = saves
            ? GameSaveSchema.Create(DotJsonContext.Default.DotSnapshot, identity, () => new DotSnapshot(X, Jumps), Validate,
                snapshot => (X, Jumps) = (snapshot.X, snapshot.Jumps))
            : null;
    }

    public GameManifest Manifest { get; }
    public float X { get; private set; }
    public int Jumps { get; private set; }
    public GameSaveSchema? Saves => saves;

    public void NewGame()
    {
        X = 0;
        Jumps = 0;
    }

    public void FixedUpdate(GameTick tick)
    {
        if (Manifest.Input.Actions.Any(action => action.Id == "right"))
        {
            X += tick.Input.Axis("left", "right") * 3 * tick.Seconds;
        }
        if (Manifest.Input.Actions.Any(action => action.Id == "jump") && tick.Input.WasPressed("jump"))
        {
            Jumps++;
        }
    }

    public void Observe(GameFacts facts)
    {
        facts.Set("x", X);
        facts.Set("jumps", Jumps);
    }

    public void Dispose()
    {
    }

    private static void Validate(DotSnapshot snapshot)
    {
        if (snapshot is null || !float.IsFinite(snapshot.X) || snapshot.Jumps < 0)
        {
            throw new InvalidDataException("Invalid dot state.");
        }
    }
}
