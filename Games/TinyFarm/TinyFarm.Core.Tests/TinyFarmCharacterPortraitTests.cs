using Aurelian.GameWorld2D;
using TinyFarm.InputMan;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmCharacterPortraitTests
{
    [Fact]
    public void EveryCastIdentityKeepsItsPortraitAcrossControllerRoles()
    {
        var portraits = new TinyFarmCharacterPortraits(Path.Combine(AppContext.BaseDirectory, "Assets"));
        TinyFarmDefinitions definitions = TinyFarmSupperContent.Load();
        TinyFarmState state = TinyFarmSupperContent.Start(definitions);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (ActorId id in new[] { TinyFarmIds.Player, TinyFarmIds.Elias, TinyFarmIds.Mara, TinyFarmIds.Sela })
        {
            ActorState actor = state.Actor(id);
            Assert.True(portraits.TryGet(actor, out SpriteAtlasResource expected));
            Assert.True(hashes.Add(expected.ContentHash));
            foreach (TinyFarmAgentControl control in Enum.GetValues<TinyFarmAgentControl>())
            {
                var agent = new TinyFarmAgentState("portrait-test", TinyFarmAgentKind.Character, control,
                    null, 1, [], new TinyFarmEquipment(null, null), null, new TinyFarmAgentAppearance(TinyFarmAgentSprite.Gardener));
                ActorState controlled = actor with { IsPlayer = control == TinyFarmAgentControl.Human, Agent = agent };
                Assert.True(portraits.TryGet(controlled, out SpriteAtlasResource actual));
                Assert.Same(expected, actual);
            }
        }
    }

    [Fact]
    public void AuthoredPortraitKeyWorksForAnotherIdentityAndObjectsDoNotBorrowCharacterFaces()
    {
        var portraits = new TinyFarmCharacterPortraits(Path.Combine(AppContext.BaseDirectory, "Assets"));
        var agent = new TinyFarmAgentState("portrait-test", TinyFarmAgentKind.Character, TinyFarmAgentControl.Idle,
            null, 1, [], new TinyFarmEquipment(null, null), null, new TinyFarmAgentAppearance(TinyFarmAgentSprite.Mara, "mara"));
        var actor = new ActorState(new ActorId("another-instance"), "Another instance", TinyFarmIds.Farmhouse, 0, [], false, agent);
        Assert.True(portraits.TryGet(actor, out SpriteAtlasResource resource));
        Assert.Equal("portrait:mara", resource.Id.Value);
        Assert.False(portraits.TryGet(actor with { Agent = agent with { Kind = TinyFarmAgentKind.Object } }, out _));
        Assert.False(portraits.TryGet(actor with { Agent = null }, out _));
        var unpainted = actor with
        {
            Agent = agent with
            {
                Appearance = agent.Appearance with { ConversationSprite = "unpainted-character" }
            }
        };
        Assert.False(portraits.TryGet(unpainted, out _));
    }

    [Fact]
    public void AgentPanelSelectsTheInspectedIdentityWithoutChangingWorldOrControl()
    {
        var game = new TinyFarmGame(new TinyFarmMemorySaveStore(), TinyFarmChapter.Supper);
        game.Start();
        string before = TinyFarmSemanticHash.Compute(game.State);
        Assert.Null(TinyFarmPortrait.SelectedActor(game));
        game.OpenStats(false);
        Assert.Equal(TinyFarmIds.Player, TinyFarmPortrait.SelectedActor(game));
        game.Menus.ChangeStatsAgent(1, game.State);
        Assert.Equal(game.Menus.StatsAgent, TinyFarmPortrait.SelectedActor(game));
        Assert.NotEqual(TinyFarmIds.Player, TinyFarmPortrait.SelectedActor(game));
        Assert.Equal(before, TinyFarmSemanticHash.Compute(game.State));
        game.OpenInventory(false);
        Assert.Null(TinyFarmPortrait.SelectedActor(game));
    }
}
