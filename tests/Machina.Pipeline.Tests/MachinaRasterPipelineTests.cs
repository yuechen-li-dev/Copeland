using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Frames;
using Machina.Pipeline;
using Machina.Runtime.Input;
using Machina.Standard.Authoring;
using Xunit;

namespace Machina.Pipeline.Tests;

public sealed class MachinaRasterPipelineTests
{
    [Fact]
    public void Render_TextUi_ProducesFrame()
    {
        var pipeline = new MachinaRasterPipeline();

        var frame = pipeline.Render(
            UI.Text("Hello", id: "hello", color: ColorToken.White),
            width: 80,
            height: 40);

        Assert.NotNull(frame.Lowering);
        Assert.NotNull(frame.Document);
        Assert.NotNull(frame.Resolved);
        Assert.NotNull(frame.HitTest);
        Assert.Equal(80, frame.RasterFrame.Width);
        Assert.Equal(40, frame.RasterFrame.Height);
        Assert.True(CountNonTransparentPixels(frame.RasterFrame) > 0);
    }

    [Fact]
    public void Render_StandardButton_ProducesHitTestAction()
    {
        var pipeline = new MachinaRasterPipeline();
        UiNode ui = StandardUI.Button(
            "Increment",
            id: "increment",
            action: UiAction.Named("increment"));

        var frame = pipeline.Render(ui, width: 200, height: 100);
        var rect = frame.Resolved.Nodes[new Machina.Layout.Rows.NodeId("increment")].Rect;
        var point = new PointerPoint((float)(rect.X + (rect.Width / 2.0)), (float)(rect.Y + (rect.Height / 2.0)));

        var hit = frame.HitTest.HitTest(point);

        Assert.NotNull(hit);
        Assert.Equal("increment", hit!.Action.Name);
    }

    [Fact]
    public void Render_StandardCard_ProducesCommandsPixelsAndPpm()
    {
        var pipeline = new MachinaRasterPipeline();
        UiNode ui = StandardUI.Card(
            id: "card",
            width: 120,
            height: 80,
            children:
            [
                UI.Text("Card", id: "title", color: ColorToken.White),
                StandardUI.Button("Go", id: "go", action: UiAction.Named("go")),
            ]);

        var frame = pipeline.Render(ui, width: 160, height: 120);

        Assert.IsType<BeginFrameCommand>(frame.RenderCommands[0]);
        Assert.IsType<EndFrameCommand>(frame.RenderCommands[^1]);
        Assert.True(CountNonTransparentPixels(frame.RasterFrame) > 0);

        var ppm = frame.RasterFrame.ToPpm();
        Assert.NotNull(ppm);
        Assert.NotEmpty(ppm);
    }

    [Fact]
    public void Render_InvalidDimensions_Throws()
    {
        var pipeline = new MachinaRasterPipeline();

        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Render(UI.Text("X"), width: 0, height: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.Render(UI.Text("X"), width: 10, height: 0));
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        var pipeline = new MachinaRasterPipeline();
        UiNode ui = StandardUI.Button("Deterministic", id: "det", action: UiAction.Named("det"));

        var first = pipeline.Render(ui, width: 200, height: 100);
        var second = pipeline.Render(ui, width: 200, height: 100);

        Assert.Equal(first.RasterFrame.ToPpm(), second.RasterFrame.ToPpm());

        Assert.Equal(first.RenderCommands.Count, second.RenderCommands.Count);
        for (var i = 0; i < first.RenderCommands.Count; i++)
        {
            Assert.Equal(first.RenderCommands[i].GetType(), second.RenderCommands[i].GetType());
        }
    }

    [Fact]
    public void Render_DisabledButton_HasNoHitTestAction()
    {
        var pipeline = new MachinaRasterPipeline();
        UiNode ui = StandardUI.Button(
            "Disabled",
            id: "disabled",
            action: UiAction.Named("disabled"),
            disabled: true);

        var frame = pipeline.Render(ui, width: 200, height: 100);
        var rect = frame.Resolved.Nodes[new Machina.Layout.Rows.NodeId("disabled")].Rect;
        var point = new PointerPoint((float)(rect.X + (rect.Width / 2.0)), (float)(rect.Y + (rect.Height / 2.0)));

        var hit = frame.HitTest.HitTest(point);

        Assert.Null(hit);
    }


    [Fact]
    public void Pipeline_RendersFlatUiDocument()
    {
        var pipeline = new MachinaRasterPipeline();
        var document = UiDocument.Create(
            [
                Row.Root("root", View.Rect(background: ColorToken.Hex(0x202020FF))),
                Row.Anchor("button", "root", left: 20, top: 20, width: 120, height: 32, view: Machina.Standard.Authoring.StandardView.Button("Go", UiAction.Named("go")))
            ]);

        var frame = pipeline.Render(document, width: 220, height: 120);
        Assert.True(CountNonTransparentPixels(frame.RasterFrame) > 0);

        var rect = frame.Resolved.Nodes[new Machina.Layout.Rows.NodeId("button")].Rect;
        var point = new PointerPoint((float)(rect.X + rect.Width / 2.0), (float)(rect.Y + rect.Height / 2.0));
        var hit = frame.HitTest.HitTest(point);
        Assert.NotNull(hit);
        Assert.Equal("go", hit!.Action.Name);
        Assert.Equal(document.Rows.Count, frame.Lowering.Rows.Count);
    }

    [Fact]
    public void Pipeline_FlatDocumentSnapshot_IsUsefulForPresenterLikeDoc()
    {
        var document = CreatePresenterLikeFormDocument(emailUpdates: true, notifications: true);

        var snapshot = UiDocumentSnapshotWriter.Write(document);

        Assert.Contains("root parent=<none>", snapshot, StringComparison.Ordinal);
        Assert.Contains("settings-card parent=root", snapshot, StringComparison.Ordinal);
        Assert.Contains("increment parent=settings-card", snapshot, StringComparison.Ordinal);
        Assert.Contains("email-row parent=settings-card", snapshot, StringComparison.Ordinal);
        Assert.Contains("email-box parent=email-row", snapshot, StringComparison.Ordinal);
        Assert.Contains("email-label parent=email-row", snapshot, StringComparison.Ordinal);
        Assert.Contains("notifications-row parent=settings-card", snapshot, StringComparison.Ordinal);
        Assert.Contains("notifications-track parent=notifications-row", snapshot, StringComparison.Ordinal);
        Assert.Contains("notifications-thumb parent=notifications-track", snapshot, StringComparison.Ordinal);
        Assert.Contains("notifications-label parent=notifications-row", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void Pipeline_RendersFlatFormDocument_AndHitTestsCheckboxAndSwitch()
    {
        var pipeline = new MachinaRasterPipeline();
        var document = CreatePresenterLikeFormDocument(emailUpdates: true, notifications: false);
        var frame = pipeline.Render(document, width: 640, height: 360);

        var emailRect = frame.Resolved.Nodes[new Machina.Layout.Rows.NodeId("email-box")].Rect;
        var emailPoint = new PointerPoint((float)(emailRect.X + 2), (float)(emailRect.Y + 2));
        var emailHit = frame.HitTest.HitTest(emailPoint);

        Assert.NotNull(emailHit);
        Assert.Equal("settings.emailUpdates.toggle", emailHit!.Action.Name);

        var switchRect = frame.Resolved.Nodes[new Machina.Layout.Rows.NodeId("notifications-track")].Rect;
        var switchPoint = new PointerPoint((float)(switchRect.X + 2), (float)(switchRect.Y + 2));
        var switchHit = frame.HitTest.HitTest(switchPoint);

        Assert.NotNull(switchHit);
        Assert.Equal("settings.notifications.toggle", switchHit!.Action.Name);
    }

    private static UiDocument CreatePresenterLikeFormDocument(bool emailUpdates, bool notifications)
    {
        var emailStateText = emailUpdates ? "on" : "off";
        var notificationsStateText = notifications ? "on" : "off";

        return UiDocument.Create(
            [
                Row.Root("root", view: View.Rect(background: ColorToken.Hex(0xEDEFF0FF))),
                Row.Anchor("settings-card", "root", left: 72, top: 24, width: 500, height: 292, view: StandardView.Card()),
                Row.Anchor("increment", "settings-card", left: 20, top: 88, width: 180, height: 30, view: StandardView.Button("Increment", UiAction.Named("counter.increment"))),
                Row.Anchor("separator", "settings-card", left: 20, right: 20, top: 128, height: 1, view: StandardView.Separator()),
                Row.Anchor("email-row", "settings-card", left: 20, right: 20, top: 150, height: 24, arrange: new StackArrange(StackAxis.Horizontal, Gap: 8)),
                Row.Fixed("email-box", "email-row", width: 18, height: 18, view: StandardView.CheckboxBox(emailUpdates, UiAction.Named("settings.emailUpdates.toggle"))),
                Row.Fill("email-label", "email-row", view: StandardView.Text($"Email updates: {emailStateText}")),
                Row.Anchor("notifications-row", "settings-card", left: 20, right: 20, top: 184, height: 24, arrange: new StackArrange(StackAxis.Horizontal, Gap: 8)),
                Row.Fixed("notifications-track", "notifications-row", width: 42, height: 20, view: StandardView.SwitchTrack(notifications, UiAction.Named("settings.notifications.toggle"))),
                Row.Anchor("notifications-thumb", "notifications-track", left: notifications ? 22 : 2, top: 2, width: 16, height: 16, view: StandardView.SwitchThumb(notifications)),
                Row.Fill("notifications-label", "notifications-row", view: StandardView.Text($"Notifications: {notificationsStateText}"))
            ]);
    }
    private static int CountNonTransparentPixels(Machina.Renderer.Raster.Dominatus.Models.RasterFrame frame)
    {
        var count = 0;

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (frame.Surface.GetPixel(x, y).A > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
