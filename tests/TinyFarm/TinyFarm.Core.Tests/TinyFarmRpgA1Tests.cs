using System.Numerics;
using Deliverance.Core.Storage;
using InputMan.Core;
using Machina.Runtime.Input;
using TinyFarm.InputMan;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmRpgA1Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextEntryMapEmitsNoGameplayUiOrShortcutActions(bool inventory)
    {
        var game = NewGame();
        game.Start();
        if (inventory)
        {
            game.OpenInventory(false);
        }
        else
        {
            game.OpenStats(false);
        }
        game.DispatchMenu("search");
        Assert.Equal([GameControls.System, GameControls.TextEntry], game.Contexts);
        var engine = new InputManEngine(GameControls.CreateProfile(slice: true));
        engine.SetMaps(game.Contexts);
        string hash = TinyFarmSemanticHash.Compute(game.State);
        string field = game.Host.Session.Field.SemanticHash;
        KeyboardKey[] keys = [KeyboardKey.W, KeyboardKey.A, KeyboardKey.S, KeyboardKey.D,
            KeyboardKey.E, KeyboardKey.I, KeyboardKey.C, KeyboardKey.J, KeyboardKey.K,
            KeyboardKey.F, KeyboardKey.N, KeyboardKey.Q, KeyboardKey.R, KeyboardKey.B,
            KeyboardKey.Number1, KeyboardKey.ArrowDown, KeyboardKey.Space];
        engine.Tick(Snapshot(keys), .016f, 0);
        Assert.Equal(Vector2.Zero, engine.CurrentFrame.GetAxis2(GameControls.Move));
        ActionId[] blocked = [GameControls.Interact, GameControls.ToggleInventory, GameControls.ToggleStats,
            GameControls.Sword, GameControls.Dodge, GameControls.UseSelected, GameControls.Hotbar1,
            GameControls.Eat, GameControls.Sleep, GameControls.UiConfirm, GameControls.UiCancel,
            GameControls.UiDown, GameControls.Save, GameControls.Load, GameControls.Quit];
        Assert.All(blocked, action => Assert.False(engine.CurrentFrame.WasPressed(action), action.Name));
        game.EnterMenuText("wasdefnqic");
        game.Handle(engine.CurrentFrame);
        game.Advance(TimeSpan.FromSeconds(5), engine.CurrentFrame, true);
        Assert.Equal("wasdefnqic", game.Menus.Search);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(game.State));
        Assert.Equal(field, game.Host.Session.Field.SemanticHash);
        Assert.False(game.ShouldQuit);
    }

    [Fact]
    public void StatsModalBlocksMovementAtTheInputEngineAndReturnsToItsParent()
    {
        var game = NewGame();
        game.Start();
        var engine = new InputManEngine(GameControls.CreateProfile(slice: true));
        Press(game, engine, KeyboardKey.C);
        Assert.Equal(TinyFarmScreen.Stats, game.Screen);
        engine.SetMaps(game.Contexts);
        engine.Tick(Snapshot(KeyboardKey.W, KeyboardKey.J), .016f, 0);
        Assert.Equal(Vector2.Zero, engine.CurrentFrame.GetAxis2(GameControls.Move));
        Assert.False(engine.CurrentFrame.WasPressed(GameControls.Sword));
        game.BackFromMenu();
        Assert.Equal(TinyFarmScreen.Playing, game.Screen);
        Press(game, engine, KeyboardKey.Escape);
        game.DispatchMenu("stats");
        game.BackFromMenu();
        Assert.Equal(TinyFarmScreen.Paused, game.Screen);
    }

    [Fact]
    public void EscapeExitsTypingBeforeMenuAndHeldActionDoesNotRepressOnReturn()
    {
        var game = NewGame();
        game.Start();
        game.OpenStats(false);
        game.DispatchMenu("search");
        var engine = new InputManEngine(GameControls.CreateProfile(slice: true));
        engine.SetMaps(game.Contexts);
        engine.Tick(Snapshot(KeyboardKey.J), .016f, 0);
        game.Handle(engine.CurrentFrame);
        engine.Tick(Snapshot(KeyboardKey.J, KeyboardKey.Escape), .016f, .016f);
        game.Handle(engine.CurrentFrame);
        Assert.False(game.Menus.SearchFocused);
        Assert.Equal(TinyFarmScreen.Stats, game.Screen);
        engine.SetMaps(game.Contexts);
        engine.Tick(Snapshot(KeyboardKey.J), .016f, .032f);
        game.BackFromMenu();
        engine.SetMaps(game.Contexts);
        engine.Tick(Snapshot(KeyboardKey.J), .016f, .048f);
        Assert.False(engine.CurrentFrame.WasPressed(GameControls.Sword));
        engine.Tick(InputSnapshot.Empty, .016f, .064f);
        engine.Tick(Snapshot(KeyboardKey.J), .016f, .080f);
        Assert.True(engine.CurrentFrame.WasPressed(GameControls.Sword));
    }

    [Fact]
    public void PresentationKeysRemainAvailableDuringTyping()
    {
        var game = NewGame();
        game.Start();
        game.OpenStats(false);
        game.DispatchMenu("search");
        var engine = new InputManEngine(GameControls.CreateProfile(slice: true));
        Press(game, engine, KeyboardKey.F9);
        Assert.False(game.Presentation.HudVisible);
        Assert.True(game.Menus.SearchFocused);
        Assert.Equal(TinyFarmScreen.Stats, game.Screen);
    }

    [Fact]
    public void ProfileSaveRoundTripHashCanonicalizationAndDeepCopyUseTheRealPath()
    {
        TinyFarmAuthoredWorld world = TinyFarmAgentExamples.Create();
        var session = new TinyFarmSession(world.State, world.Definitions);
        string hash = TinyFarmSemanticHash.Compute(session.State);
        TinyFarmSession restored = TinyFarmChunkedSaveCodec.Read(
            TinyFarmChunkedSaveCodec.Write(session, world.Definitions), world.Definitions);
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(restored.State));
        var ivy = new ActorId("ivy");
        Assert.Equal(11, restored.State.Actor(ivy).Rpg!.BaseAbilities.Strength);
        Assert.Contains("green-thumb", restored.State.Actor(ivy).Rpg!.Traits);
        TinyFarmState copy = world.State.DeepCopy();
        int index = copy.MutableActors.FindIndex(actor => actor.Id == ivy);
        ActorState actor = copy.MutableActors[index];
        Assert.NotSame(world.State.Actor(ivy).Rpg!.Skills, actor.Rpg!.Skills);
        copy.MutableActors[index] = actor with { Rpg = actor.Rpg with { Skills = actor.Rpg.Skills.Reverse().ToArray() } };
        Assert.Equal(hash, TinyFarmSemanticHash.Compute(copy));
        copy.MutableActors[index] = actor with { Rpg = actor.Rpg with { BaseAbilities = new TinyFarmAbilities(Strength: 17) } };
        Assert.NotEqual(hash, TinyFarmSemanticHash.Compute(copy));
    }

    [Fact]
    public void LegacyVersion13RemainsReadableWithoutManufacturingProfiles()
    {
        TinyFarmDefinitions definitions = TinyFarmSliceContent.Load();
        TinyFarmState state = TinyFarmSliceContent.Start(definitions);
        state.Version = TinyFarmState.AgentAuthoringSaveVersion;
        for (int index = 0; index < state.MutableActors.Count; index++)
        {
            state.MutableActors[index] = state.MutableActors[index] with { Rpg = null };
        }
        var session = new TinyFarmSession(state, definitions);
        TinyFarmSession restored = TinyFarmChunkedSaveCodec.Read(TinyFarmChunkedSaveCodec.Write(session, definitions), definitions);
        Assert.Null(restored.State.Actor(TinyFarmIds.Player).Rpg);
        Assert.Contains(TinyFarmAgentProperties.Project(restored.State, definitions, TinyFarmIds.Player),
            row => row.Name == "RPG profile" && row.Value == "Not authored in this save");
        state.MutableActors[0] = state.MutableActors[0] with { Rpg = TinyFarmRpgProfile.Starter() };
        Assert.Throws<InvalidDataException>(() => TinyFarmChunkedSaveCodec.Write(new TinyFarmSession(state, definitions), definitions));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(51, 0)]
    [InlineData(0, 25)]
    [InlineData(50, 1)]
    public void InvalidSkillProfilesFailBeforeAuthoringOrSave(int rank, int experience)
    {
        TinyFarmRpgProfile invalid = TinyFarmRpgProfile.Starter() with
        {
            Skills = [new TinyFarmSkillProgress(TinyFarmSkill.Farming, rank, experience)]
        };
        Assert.Throws<InvalidDataException>(invalid.Validate);
    }

    [Fact]
    public void PropertiesShowActualLegacyHealthNpcSkillsAndObjectInventory()
    {
        TinyFarmAuthoredWorld world = TinyFarmAgentExamples.Create();
        world.State.Slice = world.State.Slice! with { Health = 7 };
        Assert.Contains(TinyFarmAgentProperties.Project(world.State, world.Definitions, TinyFarmIds.Player),
            row => row.Name == "HP" && row.Value == "7 / 12" && row.Source == "Live opening combat");
        IReadOnlyList<TinyFarmAgentProperty> ivy = TinyFarmAgentProperties.Project(world.State, world.Definitions, new ActorId("ivy"));
        Assert.Contains(ivy, row => row.Name == "Farming" && row.Value.StartsWith("Rank 12"));
        Assert.Contains(ivy, row => row.Name == "green-thumb");
        Assert.Contains(ivy, row => row.Name == "well-rested" && row.Source.Contains("pending"));
        Assert.Contains(TinyFarmAgentProperties.Project(world.State, world.Definitions, TinyFarmIds.Mara),
            row => row.Name == "Controller" && row.Value == "Schedule");
        Assert.True(world.State.Actor(TinyFarmIds.Mara).Rpg!.ProgressionEnabled);
        IReadOnlyList<TinyFarmAgentProperty> cache = TinyFarmAgentProperties.Project(world.State, world.Definitions, new ActorId("garden-cache"));
        Assert.DoesNotContain(cache, row => row.Name == "Strength");
        Assert.Contains(cache, row => row.Group == "Inventory" && row.Value.StartsWith("3 /"));
    }

    [Fact]
    public void ActualMachinaTableScrollbarSupportsWheelDragAndFocusLossReset()
    {
        var game = NewGame();
        game.Start();
        game.OpenStats(false);
        game.Menus.SetStatsGroup("All");
        var ui = new TinyFarmNativeUi(game);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        ui.Resource(frame);
        Assert.True(ui.ScrollStats(new UiPointerWheel(new PointerPoint(500, 300), 0, -1, UiModifiers.None)));
        Assert.Equal(3, game.Menus.StatsOffset);
        int count = game.Menus.PropertyRows(game.State, game.Definitions).Count;
        var geometry = TinyFarmStatsScrollbar.Geometry(count, game.Menus.StatsOffset);
        var point = new PointerPoint(1190, geometry.ThumbRect.Y + 5);
        ui.Pointer(frame, point, true);
        ui.ScrollStats(new UiPointerMoved(new PointerPoint(1190, 590), point, UiModifiers.None));
        Assert.Equal(count - TinyFarmMenus.PageSize, game.Menus.StatsOffset);
        ui.Pointer(frame, new PointerPoint(1190, 590), false);
        ui.ResetPointer();
        int offset = game.Menus.StatsOffset;
        ui.ScrollStats(new UiPointerMoved(new PointerPoint(1190, 260), null, UiModifiers.None));
        Assert.Equal(offset, game.Menus.StatsOffset);
        game.Menus.SetSearch("wisdom");
        Assert.Single(game.Menus.PropertyRows(game.State, game.Definitions));
        Assert.Equal(0, game.Menus.StatsOffset);
    }

    private static TinyFarmGame NewGame() => new(new FileSaveStore(
        Path.Combine(AppContext.BaseDirectory, "rpg-a1-tests", Guid.NewGuid().ToString("N"))), slice: true);

    private static InputSnapshot Snapshot(params KeyboardKey[] keys) => new(
        keys.ToDictionary(key => Controls.Key(key), _ => true), new Dictionary<ControlKey, float>());

    private static void Press(TinyFarmGame game, InputManEngine engine, KeyboardKey key)
    {
        engine.SetMaps(game.Contexts);
        engine.Tick(Snapshot(key), .016f, 0);
        game.Handle(engine.CurrentFrame);
        engine.Tick(InputSnapshot.Empty, .016f, .016f);
        engine.SetMaps(game.Contexts);
    }
}
