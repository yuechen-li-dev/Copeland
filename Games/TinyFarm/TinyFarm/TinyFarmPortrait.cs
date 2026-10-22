using Aurelian.Composition;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Shaders;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

/// <summary>
/// Mara's painted portrait, matching her world sprite, drawn beside the dialogue panel while she is speaking. It sits
/// between the world and the HUD so the panel text stays on top.
/// </summary>
internal sealed class TinyFarmPortrait(
    LayerId layer,
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram texturedProgram,
    TinyFarmGame game,
    string path,
    Func<TinyFarmPresentationLayout> getLayout) : INativeLayerPresenter
{
    private readonly SpriteAtlasResource resource = TinyFarmM24Assets.LoadResource("mara-portrait-v1", path,
        "ac55e0807d72338c22491e35c19bce6e4db3ca7f74bc9ff96e6210a6a1b0e0e8", SpriteSampling.Linear);
    private VulkanOrderedQuadRenderer? renderer;
    private NativeSpriteResourceScope? resources;

    public LayerId Layer => layer;

    public void Attach(VulkanNativeFrameTarget target)
    {
        renderer = new VulkanOrderedQuadRenderer(plant, texturedProgram, target, Native2DPipelineOptions.SpriteLinear);
        resources = new NativeSpriteResourceScope(renderer, SpriteSampling.Linear);
    }

    public void Resize(VulkanNativeFrameTarget target)
    {
        renderer?.Retarget(target);
    }

    public void Present(NativeLayerFrameContext context)
    {
        if (renderer is null || resources is null || !game.Dialogue.IsActive || !game.Presentation.HudVisible)
        {
            return;
        }
        Native2DTextureHandle texture = resources.Resolve(resource);
        // Left of the dialogue panel (which starts at x = 250 in the 1280x720 UI canvas), standing on its bottom edge.
        Native2DRect rect = getLayout().UiRect(new Native2DRect(24, 398, 224, 224));
        context.Present(renderer, pass => pass.SubmitQuad(new NativeQuadSubmission(rect, Native2DUvRect.Full, texture, Native2DTint.White)));
    }

    public void Detach()
    {
        resources?.Dispose();
        renderer?.Dispose();
        resources = null;
        renderer = null;
    }
}
