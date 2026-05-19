using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Layout.Rows;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Runtime.Tests;

public sealed class UiHitTestIndexTests
{
    [Fact]
    public void ButtonHit_ReturnsAction()
    {
        var ui = StandardUI.Button("Increment", id: "increment", action: UiAction.Named("increment"));
        var (resolved, lowering) = LowerAndResolve(ui, 200, 100);
        var index = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);

        var rect = resolved.Nodes[new NodeId("increment")].Rect;
        var result = index.HitTest(new PointerPoint(rect.X + 1, rect.Y + 1));

        Assert.NotNull(result);
        Assert.Equal(new NodeId("increment"), result.NodeId);
        Assert.Equal("increment", result.Action.Name);
    }

    [Fact]
    public void OutsidePoint_ReturnsNull()
    {
        var ui = StandardUI.Button("Increment", id: "increment", action: UiAction.Named("increment"));
        var (resolved, lowering) = LowerAndResolve(ui, 200, 100);
        var index = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);

        var result = index.HitTest(new PointerPoint(-1, -1));

        Assert.Null(result);
    }

    [Fact]
    public void DisabledButton_DoesNotHit()
    {
        var ui = StandardUI.Button("Increment", id: "increment", action: UiAction.Named("increment"), disabled: true);
        var (resolved, lowering) = LowerAndResolve(ui, 200, 100);

        Assert.False(lowering.Actions.ContainsKey(new NodeId("increment")));

        var index = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);
        var rect = resolved.Nodes[new NodeId("increment")].Rect;
        var result = index.HitTest(new PointerPoint(rect.X + 1, rect.Y + 1));

        Assert.Null(result);
    }

    [Fact]
    public void LaterActionableNode_WinsWhenOverlapping()
    {
        var resolved = CreateManualResolvedDocument();
        var actions = new Dictionary<NodeId, UiAction>
        {
            [new NodeId("first")] = UiAction.Named("first"),
            [new NodeId("second")] = UiAction.Named("second"),
        };

        var index = UiHitTestIndex.Build(resolved, actions);
        var result = index.HitTest(new PointerPoint(20, 20));

        Assert.NotNull(result);
        Assert.Equal(new NodeId("second"), result.NodeId);
        Assert.Equal("second", result.Action.Name);
    }

    [Fact]
    public void ChildAction_BeatsParentAction()
    {
        var resolved = CreateManualResolvedDocument();
        var actions = new Dictionary<NodeId, UiAction>
        {
            [new NodeId("root")] = UiAction.Named("root"),
            [new NodeId("first")] = UiAction.Named("child"),
        };

        var index = UiHitTestIndex.Build(resolved, actions);
        var result = index.HitTest(new PointerPoint(20, 20));

        Assert.NotNull(result);
        Assert.Equal(new NodeId("first"), result.NodeId);
        Assert.Equal("child", result.Action.Name);
    }

    [Fact]
    public void HalfOpenBounds_AreApplied()
    {
        var resolved = CreateSingleNodeResolvedDocument(new Rect(10, 10, 20, 20));
        var actions = new Dictionary<NodeId, UiAction> { [new NodeId("target")] = UiAction.Named("target") };
        var index = UiHitTestIndex.Build(resolved, actions);

        Assert.NotNull(index.HitTest(new PointerPoint(10, 10)));
        Assert.NotNull(index.HitTest(new PointerPoint(29.999, 29.999)));
        Assert.Null(index.HitTest(new PointerPoint(30, 30)));
        Assert.Null(index.HitTest(new PointerPoint(9.999, 10)));
    }

    [Fact]
    public void ZeroSizeNode_DoesNotHit()
    {
        var resolved = CreateSingleNodeResolvedDocument(new Rect(10, 10, 0, 20));
        var actions = new Dictionary<NodeId, UiAction> { [new NodeId("target")] = UiAction.Named("target") };
        var index = UiHitTestIndex.Build(resolved, actions);

        Assert.Null(index.HitTest(new PointerPoint(10, 10)));
    }

    [Fact]
    public void StandardFormSample_HitsExpectedActions()
    {
        var ui = BuildSettingsForm();
        var (resolved, lowering) = LowerAndResolve(ui, 600, 600);
        var index = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);

        Assert.Equal("save", HitNodeAction(index, resolved, "save"));
        Assert.Equal("email-updates.changed", HitNodeAction(index, resolved, "email-updates"));
        Assert.Equal("notifications.changed", HitNodeAction(index, resolved, "notifications"));

        var labelRect = resolved.Nodes[new NodeId("title")].Rect;
        var labelHit = index.HitTest(new PointerPoint(labelRect.X + 1, labelRect.Y + 1));
        Assert.Null(labelHit);
    }

    [Fact]
    public void RepeatedBuild_IsDeterministic()
    {
        var ui = StandardUI.Button("Increment", id: "increment", action: UiAction.Named("increment"));
        var (resolved, lowering) = LowerAndResolve(ui, 200, 100);
        var point = new PointerPoint(20, 20);

        var first = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics).HitTest(point);
        var second = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics).HitTest(point);

        Assert.Equal(first, second);
    }

    private static string HitNodeAction(UiHitTestIndex index, ResolvedLayoutDocument resolved, string nodeId)
    {
        var rect = resolved.Nodes[new NodeId(nodeId)].Rect;
        var result = index.HitTest(new PointerPoint(rect.X + 1, rect.Y + 1));
        Assert.NotNull(result);
        return result.Action.Name;
    }

    private static (ResolvedLayoutDocument Resolved, UiLoweringResult Lowering) LowerAndResolve(UiNode ui, double width, double height)
    {
        var lowering = UiLowerer.Lower(ui);
        var document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, width, height));
        return (resolved, lowering);
    }

    private static ResolvedLayoutDocument CreateManualResolvedDocument()
    {
        var rootId = new NodeId("root");
        var firstId = new NodeId("first");
        var secondId = new NodeId("second");

        return new ResolvedLayoutDocument(
            rootId,
            new Dictionary<NodeId, ResolvedLayoutNode>
            {
                [rootId] = new(rootId, new Rect(0, 0, 100, 100), new RootFrame(), 0, 0, null, null, null, null),
                [firstId] = new(firstId, new Rect(10, 10, 50, 50), new AbsoluteFrame(10, 10, 50, 50), 0, 0, null, null, null, null),
                [secondId] = new(secondId, new Rect(10, 10, 50, 50), new AbsoluteFrame(10, 10, 50, 50), 1, 0, null, null, null, null),
            },
            new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                [rootId] = [firstId, secondId],
                [firstId] = [],
                [secondId] = [],
            });
    }

    private static ResolvedLayoutDocument CreateSingleNodeResolvedDocument(Rect rect)
    {
        var rootId = new NodeId("root");
        var targetId = new NodeId("target");

        return new ResolvedLayoutDocument(
            rootId,
            new Dictionary<NodeId, ResolvedLayoutNode>
            {
                [rootId] = new(rootId, new Rect(0, 0, 100, 100), new RootFrame(), 0, 0, null, null, null, null),
                [targetId] = new(targetId, rect, new AbsoluteFrame(rect.X, rect.Y, rect.Width, rect.Height), 0, 0, null, null, null, null),
            },
            new Dictionary<NodeId, IReadOnlyList<NodeId>>
            {
                [rootId] = [targetId],
                [targetId] = [],
            });
    }

    private static UiNode BuildSettingsForm()
    {
        return StandardUI.Card(
            id: "settings-card",
            child: UI.Column(
                id: "settings-content",
                gap: 12,
                children:
                [
                    UI.Text("Settings", id: "title", size: TextSize.H1),
                    StandardUI.Checkbox(
                        id: "email-updates",
                        label: "Email updates",
                        isChecked: true,
                        changed: UiAction.Named("email-updates.changed")),
                    StandardUI.Switch(
                        id: "notifications",
                        label: "Notifications",
                        isOn: false,
                        changed: UiAction.Named("notifications.changed")),
                    StandardUI.Button("Save", id: "save", action: UiAction.Named("save")),
                ]));
    }
}
