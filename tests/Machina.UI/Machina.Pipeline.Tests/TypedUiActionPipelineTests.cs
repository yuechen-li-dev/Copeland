using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Layout.Rows;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class TypedUiActionPipelineTests
{
    [Fact]
    public void TypedMessagesSurviveTableLoweringHitTestingAndPointerActivation()
    {
        var family = new UiActionFamily<RecipeId, Message>(new UiActionId("recipe"), key => key.Value,
            key => new Message.Choose(key));
        var key = new RecipeId("broth:with/salt.%2E");
        UiAction<Message> action = family[key];
        UiNode table = UiDataTable.Build("recipes", [new UiTableColumn("Recipe", 200)],
            [new UiTableRow(new NodeId("row").Child(key.Value), ["Cook"], action)]);
        var prepared = new MachinaPresentationPipeline().Prepare(UI.Surface(children:
            [StandardUI.Place(table, 30, 40, 200, 120)]), 400, 300);
        Assert.True(prepared.TryGetActionCenter(action, out var point));
        UiHitTestResult? hit = prepared.HitTest.HitTest(point);
        Assert.NotNull(hit);
        Assert.True(hit.Action.TryGetMessage<Message>(out var message));
        Assert.Equal(new Message.Choose(key), message);
        var pointer = new UiPointerActivation();
        pointer.Press(hit);
        Assert.Equal(action, pointer.Release(hit));
        Assert.Null(pointer.Release(hit));
    }

    [Fact]
    public void ChangedPayloadInvalidatesPreparationAndCancelsPressWithTheSameAlias()
    {
        UiActionId id = new("same-alias");
        UiAction firstAction = UiAction.Typed<Message>(id, new Message.Choose(new RecipeId("first")));
        UiAction secondAction = UiAction.Typed<Message>(id, new Message.Choose(new RecipeId("second")));
        static UiNode Build(UiAction action) => UI.Surface(children:
            [StandardUI.Button("Choose", action, 20, 30, 180)]);
        var cache = new MachinaPresentationCache();
        var first = cache.Prepare(Build(firstAction), 300, 200);
        var second = cache.Prepare(Build(secondAction), 300, 200);
        Assert.NotSame(first, second);
        Assert.Equal(2, cache.Preparations);
        Assert.Same(second, cache.Prepare(Build(secondAction), 300, 200));
        Assert.True(first.TryGetActionCenter(firstAction, out var point));
        var pointer = new UiPointerActivation();
        pointer.Press(first.HitTest.HitTest(point));
        Assert.Null(pointer.Release(second.HitTest.HitTest(point)));
    }

    [Fact]
    public void DiagnosticAliasLookupRejectsDistinctMessagesButTypedLookupFindsThem()
    {
        UiActionId id = new("shared");
        UiAction first = UiAction.Typed(id, new RecipeId("first"));
        UiAction second = UiAction.Typed(id, new RecipeId("second"));
        var prepared = new MachinaPresentationPipeline().Prepare(UI.Surface(children:
        [
            StandardUI.Button("First", first, 10, 10, 120, id: "first"),
            StandardUI.Button("Second", second, 150, 10, 120, id: "second"),
        ]), 300, 200);
        Assert.False(prepared.TryGetActionCenter(id, out _));
        Assert.True(prepared.TryGetActionCenter(first, out var firstPoint));
        Assert.True(prepared.TryGetActionCenter(second, out var secondPoint));
        Assert.NotEqual(firstPoint, secondPoint);
    }

    [Fact]
    public void TableRowIdentitySurvivesReorderingAndRejectsDuplicates()
    {
        NodeId firstId = new NodeId("items").Child("a.b");
        NodeId secondId = new NodeId("items").Child("a").Child("b");
        var first = new UiTableRow(firstId, ["First"], UiAction.Typed(new UiActionId("first"), 1));
        var second = new UiTableRow(secondId, ["Second"], UiAction.Typed(new UiActionId("second"), 2));
        var pipeline = new MachinaPresentationPipeline();
        var before = pipeline.Prepare(UiDataTable.Build("table", [new UiTableColumn("Name", 180)], [first, second]), 200, 200);
        var after = pipeline.Prepare(UiDataTable.Build("table", [new UiTableColumn("Name", 180)], [second, first]), 200, 200);
        NodeId cell = new NodeId("table").Child("row").Child(firstId.Value).Child(0);
        Assert.NotEqual(before.Resolved.Nodes[cell].Rect.Y, after.Resolved.Nodes[cell].Rect.Y);
        Assert.Throws<ArgumentException>(() => UiDataTable.Build("table", [new UiTableColumn("Name", 180)], [first, first]));
    }

    private readonly record struct RecipeId(string Value);
    private abstract record Message
    {
        public sealed record Choose(RecipeId Id) : Message;
    }
}
