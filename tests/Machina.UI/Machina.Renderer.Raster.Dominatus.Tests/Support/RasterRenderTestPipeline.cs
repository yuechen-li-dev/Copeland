using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Lowering;
using Machina.Core.Nodes;
using Machina.Dominatus.Rendering.Bridge;
using Machina.Layout.Compilation;
using Machina.Layout.Geometry;
using Machina.Layout.Resolving;
using Machina.Renderer.Raster.Dominatus.Actuation;
using Machina.Renderer.Raster.Dominatus.Models;
using Machina.Renderer.Raster.Text;

namespace Machina.Renderer.Raster.Dominatus.Tests.Support;

internal static class RasterRenderTestPipeline
{
    public static RasterFrame Render(
        UiNode ui,
        int width,
        int height,
        ITextRasterizer? textRasterizer = null)
    {
        var lowering = UiLowerer.Lower(ui);
        var document = LayoutCompiler.CompileLayoutRows(lowering.Rows);
        var resolved = LayoutDocumentResolver.ResolveLayoutDocument(document, new Rect(0, 0, width, height));
        var commands = MachinaRenderBridge.BuildCommands(lowering, resolved, new MachinaRenderOptions(width, height));

        var recorder = new RasterRenderRecorder();
        var options = new RasterRenderOptions(textRasterizer ?? new DebugBitmapTextRasterizer());
        var host = new ActuatorHost().AddRasterRenderer(recorder, options);
        var context = CreateContext(host);

        foreach (var command in commands)
        {
            var dispatch = host.Dispatch(context, command);
            if (!dispatch.Accepted || !dispatch.Completed || !dispatch.Ok)
            {
                throw new InvalidOperationException($"Render command dispatch failed for {command.GetType().Name}.");
            }
        }

        if (recorder.CompletedFrames.Count != 1)
        {
            throw new InvalidOperationException($"Expected exactly one completed frame but observed {recorder.CompletedFrames.Count}.");
        }

        return recorder.CompletedFrames[0];
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
