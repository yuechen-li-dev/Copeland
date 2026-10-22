using Aurelian.Composition;
using Aurelian.GameWorld2D;
using Aurelian.Graphics.Vulkan.Device;
using Aurelian.Graphics.Vulkan.Native2D;
using Aurelian.NativeComposition;
using Aurelian.Rendering.Contracts.Shaders;
using TinyFarm.InputMan;
using TinyFarm.Core;

namespace TinyFarm.Native;

/// <summary>
/// Identity portraits for dialogue speakers and the selected agent's character panel.
/// </summary>
internal sealed class TinyFarmPortrait(
    LayerId layer,
    AurelianVulkanPlant plant,
    CompiledGraphicsProgram texturedProgram,
    TinyFarmGame game,
    TinyFarmCharacterPortraits portraits,
    Func<TinyFarmPresentationLayout> getLayout) : INativeLayerPresenter
{
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
        ActorId? selected = SelectedActor(game);
        if (renderer is null || resources is null || selected is not ActorId actor
            || !portraits.TryGet(game.State.Actor(actor), out SpriteAtlasResource resource))
        {
            return;
        }
        Native2DTextureHandle texture = resources.Resolve(resource);
        // The agent panel reserves a square beside the title; dialogue uses the larger bust left of the text panel.
        Native2DRect bounds = game.Screen == TinyFarmScreen.Stats
            ? new Native2DRect(908, 60, 96, 96)
            : new Native2DRect(24, 398, 224, 224);
        Native2DRect rect = getLayout().UiRect(bounds);
        context.Present(renderer, pass => pass.SubmitQuad(new NativeQuadSubmission(rect, Native2DUvRect.Full, texture, Native2DTint.White)));
    }

    internal static ActorId? SelectedActor(TinyFarmGame game)
    {
        ActorId? actor = null;
        if (game.Screen == TinyFarmScreen.Stats)
        {
            actor = game.Menus.StatsAgent;
        }
        else if (game.Dialogue.IsActive && game.Presentation.HudVisible)
        {
            actor = game.Dialogue.SpeakingActor;
        }
        if (actor is not ActorId id || TinyFarmAgentPolicy.IsObject(game.State.Actor(id)))
        {
            return null;
        }
        return id;
    }

    public void Detach()
    {
        resources?.Dispose();
        renderer?.Dispose();
        resources = null;
        renderer = null;
    }
}
