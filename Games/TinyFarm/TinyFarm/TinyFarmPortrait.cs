using System.Security.Cryptography;
using Aurelian.Composition;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Shaders;
using TinyFarm.InputMan;

namespace TinyFarm.Native;

/// <summary>
/// Mara's dialogue portrait from the supper prototype, drawn beside the dialogue panel while she is speaking. It sits
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
    private readonly SpriteAtlasResource resource = Load(path);
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

    private static SpriteAtlasResource Load(string path)
    {
        TinyFarmImage image = TinyFarmImage.Load(path);
        byte[] rgba = image.ToRgba();
        return new SpriteAtlasResource(new SpriteAssetId("mara-portrait"), Convert.ToHexStringLower(SHA256.HashData(rgba)),
            (uint)image.Width, (uint)image.Height, rgba, SpriteSampling.Linear);
    }
}
