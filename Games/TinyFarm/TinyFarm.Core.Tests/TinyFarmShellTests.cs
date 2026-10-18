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
        using var reopened = new GameSession(new TinyFarmApp(legacySaveDirectory: NoLegacySaves), directory);
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

    [Fact]
    public void ClickingPocketsFiltersAndClosesThroughTheShellPointer()
    {
        using GameSession session = Start(out TinyFarmApp app);
        var target = new GamePlaytestTarget(session);
        Tap(session, KeyboardKey.I);
        Assert.Equal(TinyFarmScreen.Inventory, app.Game.Screen);

        Click(session, target, "equipment");
        Assert.True(app.Game.Menus.EquipmentOnly);
        Click(session, target, "bag");
        Assert.False(app.Game.Menus.EquipmentOnly);

        Click(session, target, "close");
        Assert.Equal(TinyFarmScreen.Playing, app.Game.Screen);
        Assert.Equal("playing", session.Screen);
    }

    [Fact]
    public void TheSearchBoxTakesTypingUntilEnterAndLettersDoNotLeakIntoPlay()
    {
        using GameSession session = Start(out TinyFarmApp app);
        var target = new GamePlaytestTarget(session);
        Tap(session, KeyboardKey.I);
        Click(session, target, "search");
        Assert.True(app.Game.Menus.SearchFocused);
        Assert.Equal([TinyFarmShellInput.Text], app.InputContexts);

        // I would close the pockets and E would interact; while searching they are just letters.
        session.Input.EnterText("swie");
        session.Input.EditText(GameTextEdit.Backspace);
        Tap(session, KeyboardKey.I);
        Tap(session, KeyboardKey.E);
        Assert.Equal("swi", app.Game.Menus.Search);
        Assert.Equal(TinyFarmScreen.Inventory, app.Game.Screen);

        Tap(session, KeyboardKey.Enter);
        Assert.False(app.Game.Menus.SearchFocused);
        Assert.Equal([TinyFarmShellInput.Menu], app.InputContexts);
        Assert.Equal("swi", app.Game.Menus.Search);
    }

    [Fact]
    public void TheWheelStepsToolsAndAClickAdvancesDialogue()
    {
        using GameSession session = Start(out TinyFarmApp app);
        int before = app.Game.State.SelectedHotbarSlot;
        session.Input.Adapter.RecordPointerPosition(new System.Numerics.Vector2(480, 300));
        session.Input.Adapter.RecordAxis(Controls.Mouse(MouseAxis.WheelY), -1);
        session.Advance(Tick);
        Assert.Equal(before % 4 + 1, app.Game.State.SelectedHotbarSlot);

        // Clicking Mara walks over to her (through ordinary movement ticks) and talks.
        Assert.True(app.WalkTo(app.Game.State.ActorScene(TinyFarmIds.Mara).WorldPosition));
        for (int tick = 0; tick < 600 && !app.Game.Dialogue.IsActive; tick++)
        {
            session.Advance(Tick);
        }
        Assert.True(app.Game.Dialogue.IsActive);
        Assert.False(app.Walking);
        string? line = app.Game.Dialogue.Presentation?.OperationId;
        MouseClick(session, MouseButton.Primary);
        Assert.NotEqual(line, app.Game.Dialogue.Presentation?.OperationId);
    }

    [Fact]
    public void ClickToGoWalksToAPointAndKeysCancelIt()
    {
        using GameSession session = Start(out TinyFarmApp app);
        var goal = ScenePosition.FromGrid(new GridPosition(14, 9));
        Assert.True(app.WalkTo(goal));
        for (int tick = 0; tick < 900 && app.Walking; tick++)
        {
            session.Advance(Tick);
        }
        ScenePosition reached = app.Game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        Assert.False(app.Walking);
        Assert.InRange(Math.Abs(reached.XUnits - goal.XUnits) + Math.Abs(reached.YUnits - goal.YUnits), 0, 512);

        Assert.True(app.WalkTo(ScenePosition.FromGrid(new GridPosition(3, 9))));
        session.Advance(Tick);
        Hold(session, KeyboardKey.W, 2);
        Assert.False(app.Walking);
    }

    [Fact]
    public void AGamepadWalksTalksAndOpensPockets()
    {
        using GameSession session = Start(out TinyFarmApp app);
        session.Input.Adapter.ConnectGamepad(0);
        ScenePosition before = app.Game.State.ActorScene(TinyFarmIds.Player).WorldPosition;
        session.Input.Adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftX), 1);
        for (int tick = 0; tick < 30; tick++)
        {
            session.Advance(Tick);
        }
        session.Input.Adapter.RecordAxis(Controls.Gamepad(GamepadAxis.LeftX), 0);
        Assert.True(app.Game.State.ActorScene(TinyFarmIds.Player).WorldPosition.XUnits > before.XUnits);

        Pad(session, GamepadButton.Back);
        Assert.Equal(TinyFarmScreen.Inventory, app.Game.Screen);
        Pad(session, GamepadButton.East);
        Assert.Equal(TinyFarmScreen.Playing, app.Game.Screen);

        var walkthrough = new TinyFarm.InputMan.TinyFarmWalkthrough(app.Game);
        walkthrough.Approach(app.Game.State.ActorScene(TinyFarmIds.Mara).WorldPosition);
        Pad(session, GamepadButton.South);
        Assert.True(app.Game.Dialogue.IsActive);
    }

    [Fact]
    public async Task ThePreShellSaveIsImportedIntoTheEmptyMenuSlotOnce()
    {
        string legacy = Path.Combine(Path.GetTempPath(), "tinyfarm-legacy", Guid.NewGuid().ToString("N"));
        var old = new TinyFarmGame(new Deliverance.Core.Storage.FileSaveStore(legacy), TinyFarmChapter.Shipping);
        old.Start();
        old.Execute(new SpatialMoveIntent(1, 0, 512));
        Assert.True(old.Save("sleeping-spring-a3"));
        ScenePosition oldPosition = old.State.ActorScene(TinyFarmIds.Player).WorldPosition;

        string shell = Path.Combine(Path.GetTempPath(), "tinyfarm-shell-tests", Guid.NewGuid().ToString("N"));
        using (var session = new GameSession(new TinyFarmApp(Path.Combine(AppContext.BaseDirectory, "Assets"), legacy), shell))
        {
            Assert.True(session.ImportedLegacySave);
            Assert.Null(session.PersistenceError);
            await session.LoadAsync(GameSession.QuickSlot);
            var app = (TinyFarmApp)session.Game;
            Assert.Equal(oldPosition, app.Game.State.ActorScene(TinyFarmIds.Player).WorldPosition);
            Assert.Equal(TinyFarmChapter.Supper, app.Game.Chapter);
        }
        // Once the slot exists, the old save is left alone.
        using var again = new GameSession(new TinyFarmApp(Path.Combine(AppContext.BaseDirectory, "Assets"), legacy), shell);
        Assert.False(again.ImportedLegacySave);
    }

    [Fact]
    public void ChapterUpgradesOnlyMoveForward()
    {
        var shipping = new TinyFarmGame(new Deliverance.Core.Storage.FileSaveStore(NoLegacySaves), TinyFarmChapter.Shipping);
        TinyFarmDefinitions supper = TinyFarmSupperContent.Load();
        TinyFarmSemanticSaveSnapshot upgraded = TinyFarmChapterUpgrade.Upgrade(shipping.Persistence.CaptureSnapshot(),
            shipping.Definitions.Identity, supper.Identity);
        Assert.Equal(supper.Identity, upgraded.DefinitionHash);
        Assert.Throws<InvalidDataException>(() => TinyFarmChapterUpgrade.Upgrade(upgraded, supper.Identity, shipping.Definitions.Identity));
    }

    private static string NoLegacySaves => Path.Combine(Path.GetTempPath(), "tinyfarm-no-legacy", Guid.NewGuid().ToString("N"));

    private static void Pad(GameSession session, GamepadButton button)
    {
        session.Input.Adapter.RecordButton(Controls.Gamepad(button), true);
        session.Advance(TimeSpan.Zero);
        session.Input.Adapter.RecordButton(Controls.Gamepad(button), false);
        session.Advance(Tick);
        session.Advance(Tick);
    }

    private static void Click(GameSession session, GamePlaytestTarget target, string action)
    {
        (double x, double y) = target.ActionPoint(action);
        target.Pointer(x, y, true);
        target.Frame(TimeSpan.Zero);
        target.Pointer(x, y, false);
        target.Frame(Tick);
        session.Advance(Tick);
    }

    private static void MouseClick(GameSession session, MouseButton button)
    {
        session.Input.Adapter.RecordButton(Controls.Mouse(button), true);
        session.Advance(TimeSpan.Zero);
        session.Input.Adapter.RecordButton(Controls.Mouse(button), false);
        session.Advance(Tick);
        session.Advance(Tick);
    }

    private static GameSession Start(out TinyFarmApp app, string? directory = null)
    {
        app = new TinyFarmApp(Path.Combine(AppContext.BaseDirectory, "Assets"), NoLegacySaves);
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
