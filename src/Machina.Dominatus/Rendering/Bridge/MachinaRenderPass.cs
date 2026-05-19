using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Machina.Core.Lowering;
using Machina.Layout.Documents;

namespace Machina.Dominatus.Rendering.Bridge;

public static class MachinaRenderPass
{
    public static IEnumerator<AiStep> Render(
        AiCtx ctx,
        UiLoweringResult lowering,
        ResolvedLayoutDocument resolved,
        MachinaRenderOptions? options = null)
    {

        var commands = MachinaRenderBridge.BuildCommands(lowering, resolved, options);

        foreach (var command in commands)
        {
            yield return Ai.Act(command);
        }

        yield return Ai.Succeed();
    }
}
