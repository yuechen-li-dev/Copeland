using Deliverance.Core.Storage;
using Machina.Layout.Geometry;
using Machina.Presentation;
using TinyFarm.Core;
using TinyFarm.InputMan;
using TinyFarm.Native;
using Xunit;

namespace TinyFarm.Core.Tests;

public sealed class TinyFarmStandardAuthoringTests
{
    [Fact]
    public void LiveJournalFitsMeasuredObjectivesInsideItsPanelAndReusesUnchangedFrame()
    {
        var game = new TinyFarmGame(NewStore());
        game.Start();
        var ui = new TinyFarmNativeUi(game);
        TinyFarmFrame frame = TinyFarmFrameProjector.Project(game.State, game.Definitions);
        // Native rendering uses 16px MSDF text even for Caption/Sm. Bitmap metrics
        // underestimate these runs and allow words to overlap in the actual game.
        TinyFarmNativeUiFont font = TinyFarmNativeUiFont.Create(
            Path.Combine(AppContext.BaseDirectory, "Assets", "SpaceMono-Regular.ttf"));
        ui.Resource(frame);
        ui.SetTextMeasurer(font);
        MachinaPresentationFrame resource = ui.Resource(frame);
        var panel = Assert.Single(resource.Operations.OfType<MachinaAnalyticShapePrimitive>(),
            operation => operation.SourceId == TinyFarmUiIds.Nodes[TinyFarmUiNode.Journal].Value);
        string[] objectives = game.Objectives();
        Assert.NotEmpty(objectives);
        for (int index = 0; index < objectives.Length; index++)
        {
            string prefix = TinyFarmUiIds.Objectives[index].Child("content").Value;
            var text = resource.Operations.OfType<PositionedTextOperation>()
                .Where(operation => operation.SourceId.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(text);
            foreach (var operation in text)
            {
                AssertInside(operation.Rect, panel.DestinationRect);
                PositionedTextOperation qualified = font.Qualify(operation);
                Assert.NotNull(qualified.Primitive);
                double nativeAdvance = qualified.Primitive.GlyphRun.Glyphs.Max(glyph => glyph.OriginX + glyph.Advance);
                Assert.True(nativeAdvance <= operation.Rect.Width + 0.001);
                Assert.True(operation.Rect.Height >= 16);
            }
        }
        Assert.Same(resource, ui.Resource(frame));
        Assert.Equal(2, ui.Rebuilds);
    }

    [Fact]
    public void LiveDialoguePlacesChoicesAfterMeasuredBodyAndKeepsHintInsidePanel()
    {
        TinyFarmDefinitions definitions = TinyFarmDefinitionLoader.Load();
        TinyFarmState state = TinyFarmContent.CreateContinuousSceneState(definitions);
        ActorSceneState mara = state.ActorScene(TinyFarmIds.Mara);
        int playerSceneIndex = state.MutableActorScenes.FindIndex(actor => actor.Actor == TinyFarmIds.Player);
        state.MutableActorScenes[playerSceneIndex] = new ActorSceneState(
            TinyFarmIds.Player, mara.Scene, mara.WorldPosition, mara.Facing);
        int playerIndex = state.MutableActors.FindIndex(actor => actor.Id == TinyFarmIds.Player);
        state.MutableActors[playerIndex] = state.MutableActors[playerIndex] with
        {
            Location = TinyFarmIds.TownSquare,
            Inventory = [TinyFarmIds.WildMint],
        };
        int mintIndex = state.MutableItems.FindIndex(item => item.Id == TinyFarmIds.WildMint);
        state.MutableItems[mintIndex] = state.MutableItems[mintIndex] with
        {
            Owner = TinyFarmIds.Player,
            GroundLocation = null,
            GroundScene = null,
            GroundPosition = null,
        };
        var game = new TinyFarmGame(NewStore(), authored: new TinyFarmAuthoredWorld(definitions, state));
        game.Start();
        Assert.True(game.Dialogue.TryBeginFrom(game.Host.ExecuteIntent(new InteractIntent())));
        for (int index = 0; index < 5 && !game.Dialogue.Presentation!.IsAwaitingChoice; index++)
        {
            game.Dialogue.Apply(TinyFarmDialogueAction.Advance);
        }
        Assert.True(game.Dialogue.Presentation!.IsAwaitingChoice);
        var ui = new TinyFarmNativeUi(game);
        MachinaPresentationFrame resource = ui.Resource(TinyFarmFrameProjector.Project(game.State, definitions));
        var panel = Assert.Single(resource.Operations.OfType<MachinaAnalyticShapePrimitive>(),
            operation => operation.SourceId == TinyFarmUiIds.Nodes[TinyFarmUiNode.Dialogue].Value);
        var body = resource.Operations.OfType<PositionedTextOperation>()
            .Where(operation => new Machina.Layout.Rows.NodeId(operation.SourceId).IsWithin(TinyFarmUiIds.Nodes[TinyFarmUiNode.DialogueBody])).ToArray();
        Assert.NotEmpty(body);
        double bodyBottom = body.Max(operation => operation.Rect.Y + operation.Rect.Height);
        var choices = resource.Operations.OfType<PositionedTextOperation>()
            .Where(operation => new Machina.Layout.Rows.NodeId(operation.SourceId).IsWithin(TinyFarmUiIds.Nodes[TinyFarmUiNode.Dialogue].Child("choices"))).ToArray();
        Assert.Equal(game.Dialogue.Presentation.Choices.Count, choices.Length);
        foreach (var choice in choices)
        {
            Assert.True(choice.Rect.Y >= bodyBottom);
            AssertInside(choice.Rect, panel.DestinationRect);
        }
        var hint = Assert.Single(resource.Operations.OfType<PositionedTextOperation>(),
            operation => operation.SourceId == TinyFarmUiIds.Nodes[TinyFarmUiNode.DialogueHint].Value);
        Assert.True(hint.Rect.Y >= choices.Max(operation => operation.Rect.Y + operation.Rect.Height));
        AssertInside(hint.Rect, panel.DestinationRect);
    }

    private static void AssertInside(Rect child, Rect parent)
    {
        Assert.True(child.X >= parent.X);
        Assert.True(child.Y >= parent.Y);
        Assert.True(child.X + child.Width <= parent.X + parent.Width + 0.001);
        Assert.True(child.Y + child.Height <= parent.Y + parent.Height + 0.001);
    }

    private static FileSaveStore NewStore()
    {
        return new FileSaveStore(Path.Combine(Path.GetTempPath(), "tinyfarm-standard-authoring", Guid.NewGuid().ToString("N")));
    }
}
