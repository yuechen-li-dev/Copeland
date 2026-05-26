using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Flat;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Dominatus.Rendering.Bridge;
using Machina.Layout.Compilation;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Renderer.Raster.Dominatus;
using Machina.Renderer.Raster.Dominatus.Actuation;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Text;
using Machina.Runtime.Input;

namespace Machina.Pipeline;

public sealed class MachinaRasterPipeline
{
    public MachinaFrame Render(UiDocument document, int width, int height)
    {
        return Render(document, new MachinaRasterPipelineOptions(width, height));
    }

    public MachinaFrame Render(UiDocument document, MachinaRasterPipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        ValidateDimensions(options.Width, options.Height);

        UiLoweringResult lowering = UiDocumentLowerer.Lower(document);
        LayoutDocument layoutDocument = LayoutCompiler.CompileLayoutRows(lowering.Rows);

        var rootRect = new Rect(0, 0, options.Width, options.Height);
        ResolvedLayoutDocument resolved = LayoutDocumentResolver.ResolveLayoutDocument(layoutDocument, rootRect);

        UiHitTestIndex hitTest = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);

        IReadOnlyList<IActuationCommand> commands = MachinaRenderBridge.BuildCommands(
            lowering,
            resolved,
            new MachinaRenderOptions(options.Width, options.Height));

        RasterFrame frame = DispatchToRasterFrame(commands, options.TextRasterizer);

        return new MachinaFrame(lowering, layoutDocument, resolved, hitTest, commands, frame);
    }

    public MachinaFrame Render(UiNode ui, int width, int height)
    {
        return Render(ui, new MachinaRasterPipelineOptions(width, height));
    }

    public MachinaFrame Render(UiNode ui, MachinaRasterPipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(options);

        ValidateDimensions(options.Width, options.Height);

        UiLoweringResult lowering = UiLowerer.Lower(ui);
        LayoutDocument document = LayoutCompiler.CompileLayoutRows(lowering.Rows);

        var rootRect = new Rect(0, 0, options.Width, options.Height);
        ResolvedLayoutDocument resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, rootRect);

        UiHitTestIndex hitTest = UiHitTestIndex.Build(resolved, lowering.Actions, lowering.Semantics);

        IReadOnlyList<IActuationCommand> commands = MachinaRenderBridge.BuildCommands(
            lowering,
            resolved,
            new MachinaRenderOptions(options.Width, options.Height));

        RasterFrame frame = DispatchToRasterFrame(commands, options.TextRasterizer);

        return new MachinaFrame(lowering, document, resolved, hitTest, commands, frame);
    }

    private static RasterFrame DispatchToRasterFrame(IReadOnlyList<IActuationCommand> commands, ITextRasterizer? textRasterizer)
    {
        var recorder = new RasterRenderRecorder();
        var renderOptions = new RasterRenderOptions(TextRasterizer: textRasterizer ?? new ReadableBitmapTextRasterizer());
        var host = new ActuatorHost().AddRasterRenderer(recorder, renderOptions);
        AiCtx context = CreateContext(host);

        foreach (var command in commands)
        {
            var dispatch = host.Dispatch(context, command);
            if (!dispatch.Accepted || !dispatch.Completed || !dispatch.Ok)
            {
                throw new InvalidOperationException(
                    $"Render command dispatch failed for {command.GetType().Name}: " +
                    $"accepted={dispatch.Accepted}, completed={dispatch.Completed}, ok={dispatch.Ok}.");
            }
        }

        if (recorder.CompletedFrames.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one completed frame but observed {recorder.CompletedFrames.Count}.");
        }

        return recorder.CompletedFrames[0];
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
        }
    }

    private static AiCtx CreateContext(ActuatorHost host)
    {
        var graph = new HfsmGraph { Root = new StateId("Root") }
            .Add(new StateId("Root"), static _ => Idle());
        var agent = new AiAgent(new HfsmInstance(graph));
        var world = new AiWorld(host);
        world.Add(agent);

        return new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, host);
    }

    private static IEnumerator<AiStep> Idle()
    {
        yield return Ai.Succeed();
    }
}
