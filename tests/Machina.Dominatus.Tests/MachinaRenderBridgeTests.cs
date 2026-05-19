using Xunit;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Bridge;
using Machina.Dominatus.Rendering.Commands;
using Machina.Dominatus.Rendering.Snapshot;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Standard.Authoring;

namespace Machina.Dominatus.Tests;

public sealed class MachinaRenderBridgeTests
{
    [Fact]
    public void CoreRectText_EmitsFrameFillTextEnd()
    {
        var ui = UI.Rect(
            id: "panel",
            width: 200,
            height: 100,
            color: ColorToken.Hex(0x101820FF),
            child: UI.Text("Hello", id: "title", color: ColorToken.White));

        var commands = BuildCommands(ui, new MachinaRenderOptions(800, 600));

        Assert.IsType<BeginFrameCommand>(commands[0]);
        var fill = Assert.IsType<FillRectCommand>(commands[1]);
        var draw = Assert.IsType<DrawTextCommand>(commands[2]);
        Assert.IsType<EndFrameCommand>(commands[^1]);

        Assert.Equal("panel", fill.Id);
        Assert.Equal(ColorToken.Hex(0x101820FF), fill.Color);
        Assert.Equal("title", draw.Id);
        Assert.Equal("Hello", draw.Text);
        Assert.Equal(ColorToken.White, draw.Style.Color);
    }

    [Fact]
    public void StandardCardButton_EmitsDeterministicCommands()
    {
        var ui = StandardUI.Card(
            id: "card",
            child: UI.Column(
                id: "content",
                gap: 8,
                children:
                [
                    UI.Text("Profile", id: "title", size: TextSize.H1),
                    StandardUI.Button("Save", id: "save", action: UiAction.Named("save")),
                ]));

        var commands = BuildCommands(ui, new MachinaRenderOptions(800, 600));

        Assert.IsType<BeginFrameCommand>(commands[0]);
        Assert.IsType<EndFrameCommand>(commands[^1]);
        Assert.Contains(commands, cmd => cmd is FillRectCommand fill && fill.Id == "card");
        Assert.Contains(commands, cmd => cmd is FillRectCommand fill && fill.Id == "save");
        Assert.Contains(commands, cmd => cmd is DrawTextCommand draw && draw.Id == "title" && draw.Text == "Profile");
        Assert.Contains(commands, cmd => cmd is DrawTextCommand draw && draw.Id == "save" && draw.Text == "Save");
    }

    [Fact]
    public void StandardForm_EmitsDeterministicRenderCommandsWithoutActions()
    {
        var ui = StandardUI.Card(
            id: "settings-card",
            child: UI.Column(
                id: "settings-content",
                gap: 12,
                children:
                [
                    StandardUI.Field(
                        id: "username-field",
                        label: "Username",
                        control: StandardUI.Input(id: "username", value: "ada", placeholder: "Enter username")),
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

        var commands = BuildCommands(ui, new MachinaRenderOptions(800, 600));

        var textCommands = commands.OfType<DrawTextCommand>().ToList();
        Assert.Contains(textCommands, c => c.Text == "Username");
        Assert.Contains(textCommands, c => c.Text == "ada");
        Assert.Contains(textCommands, c => c.Text == "Email updates");
        Assert.Contains(textCommands, c => c.Text == "Notifications");
        Assert.Contains(textCommands, c => c.Text == "Save");

        Assert.Contains(commands, c => c is FillRectCommand fill && fill.Id == "settings-card");
        Assert.Contains(commands, c => c is FillRectCommand fill && fill.Id == "username");
        Assert.Contains(commands, c => c is FillRectCommand fill && fill.Id.Contains("email-updates", StringComparison.Ordinal));
        Assert.Contains(commands, c => c is FillRectCommand fill && fill.Id.Contains("notifications", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, c => c.GetType().Name.Contains("Action", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildCommands_IsDeterministicAcrossRepeatedBuilds()
    {
        var ui = UI.Text("Hello", id: "hello", color: ColorToken.White);
        var lowering = UiLowerer.Lower(ui);
        var resolved = ResolveLayout(lowering, 200, 100);

        var first = MachinaRenderBridge.BuildCommands(lowering, resolved, new MachinaRenderOptions(200, 100));
        var second = MachinaRenderBridge.BuildCommands(lowering, resolved, new MachinaRenderOptions(200, 100));

        Assert.Equal(first.Count, second.Count);

        var firstSnapshot = SnapshotCommands(first);
        var secondSnapshot = SnapshotCommands(second);
        Assert.Equal(firstSnapshot, secondSnapshot);
    }

    [Fact]
    public void RenderPass_EmitsCommandsThroughSnapshotActuator()
    {
        var ui = UI.Rect(
            id: "panel",
            width: 200,
            height: 100,
            color: ColorToken.Hex(0x101820FF),
            child: UI.Text("Hello", id: "title", color: ColorToken.White));

        var lowering = UiLowerer.Lower(ui);
        var resolved = ResolveLayout(lowering, 800, 600);

        var recorder = new RenderSnapshotRecorder();
        var host = new ActuatorHost().AddSnapshotRenderer(recorder);
        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = new StateId("Root") }
            .Add(new StateId("Root"), ctx => MachinaRenderPass.Render(ctx, lowering, resolved, new MachinaRenderOptions(800, 600)));

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        RunTicksUntil(world, () => recorder.CompletedSnapshots.Count > 0, 10);

        Assert.Single(recorder.CompletedSnapshots);
        var snapshot = recorder.LastSnapshot;
        Assert.NotNull(snapshot);
        Assert.Contains("beginFrame w=800 h=600", snapshot!.Commands);
        Assert.Contains(snapshot.Commands, line => line.Contains("fillRect id=panel", StringComparison.Ordinal));
        Assert.Contains(snapshot.Commands, line => line.Contains("drawText id=title", StringComparison.Ordinal));
        Assert.Contains("endFrame", snapshot.Commands);
        Assert.Empty(agent.InFlightActuations);
    }

    [Fact]
    public void TextOnlyUi_StillDrawsTextWithoutFill()
    {
        var commands = BuildCommands(UI.Text("Hello", id: "hello"), new MachinaRenderOptions(300, 200));

        Assert.Contains(commands, c => c is DrawTextCommand draw && draw.Id == "hello");
        Assert.DoesNotContain(commands, c => c is FillRectCommand);
    }

    [Fact]
    public void LayoutOnlyNodes_DoNotEmitFillOrText()
    {
        var ui = UI.Column(id: "root", children: [UI.Row(children: [], id: "child")]);
        var commands = BuildCommands(ui, new MachinaRenderOptions(200, 100));

        Assert.Equal(2, commands.Count);
        Assert.IsType<BeginFrameCommand>(commands[0]);
        Assert.IsType<EndFrameCommand>(commands[1]);
    }

    [Fact]
    public void InvalidRenderOptions_ThrowsDeterministicError()
    {
        var ui = UI.Text("Hello", id: "hello");
        var lowering = UiLowerer.Lower(ui);
        var resolved = ResolveLayout(lowering, 200, 100);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MachinaRenderBridge.BuildCommands(lowering, resolved, new MachinaRenderOptions(0, 100)));

        Assert.Contains("greater than zero", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<IActuationCommand> BuildCommands(UiNode ui, MachinaRenderOptions options)
    {
        var lowering = UiLowerer.Lower(ui);
        var resolved = ResolveLayout(lowering, options.Width, options.Height);
        return MachinaRenderBridge.BuildCommands(lowering, resolved, options);
    }

    private static ResolvedLayoutDocument ResolveLayout(UiLoweringResult lowering, int width, int height)
    {
        var document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        return LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, width, height));
    }

    private static string SnapshotCommands(IReadOnlyList<IActuationCommand> commands)
    {
        var recorder = new RenderSnapshotRecorder();
        foreach (var command in commands)
        {
            switch (command)
            {
                case BeginFrameCommand begin:
                    recorder.Record(begin);
                    break;
                case FillRectCommand fill:
                    recorder.Record(fill);
                    break;
                case DrawTextCommand draw:
                    recorder.Record(draw);
                    break;
                case EndFrameCommand end:
                    recorder.Record(end);
                    break;
            }
        }

        return string.Join("\n", recorder.LastSnapshot!.Commands);
    }

    private static void RunTicksUntil(AiWorld world, Func<bool> done, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            world.Tick(0.016f);
            if (done())
            {
                return;
            }
        }
    }
}
